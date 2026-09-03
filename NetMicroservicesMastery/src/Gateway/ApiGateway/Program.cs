using ApiGateway.Authentication;
using ApiGateway.Extensions;
using ApiGateway.Services;
using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Observability;
using BuildingBlocks.Security;
using Consul;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Yarp.ReverseProxy.Configuration;
// Consul paketiyle Yarp.ReverseProxy.Configuration arasındaki isim
// çakışmasını (RouteConfig) çözmek için açık alias.
using RouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Modül 1: NET Core ve Kestrel — Self-Hosted Mimari
// =====================================================================
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    options.Limits.MaxConcurrentConnections = 100;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.AddServerHeader = false;
});

// =====================================================================
// Modül 1: Gözlemlenebilirlik — Serilog + OpenTelemetry (Prometheus/Jaeger)
// Gateway de dahil TÜM servislerde tracing/log/metric zinciri aktiftir.
// =====================================================================
builder.AddServiceObservability(serviceName: "ApiGateway");
builder.Services.AddTransient<IClaimsTransformation, ScopeClaimTransformation>();

// =====================================================================
// Modül 2: Merkezi Kimlik Doğrulama — Keycloak (AuthServer)
// MİMARİ KARAR: JWT doğrulaması SADECE Gateway'de yapılır. Downstream
// servisler (Order/Payment/Inventory) token doğrulamaz.
// =====================================================================
builder.Services.AddKeycloakAuthentication(builder.Configuration);
//builder.Services.AddTransient<IClaimsTransformation, ScopeClaimTransformation>();
builder.Services.AddAuthorization();

//builder.Services.AddAuthorization(options =>
//{
//  // Order route için özel policy: "order-admin" scope'u gerektirir
//  //options.AddPolicy("OrderAdmin", policy =>
//  //    policy
//  //        .RequireAuthenticatedUser());
//          //.RequireClaim("scope", "order-admin","email","profile"));

//  options.AddPolicy("PaymentAdmin", policy =>
//      policy
//          .RequireAuthenticatedUser()
//          .RequireClaim("scope", "payment-admin"));

//  options.AddPolicy("InventoryAdmin", policy =>
//      policy
//          .RequireAuthenticatedUser()
//          .RequireClaim("scope", "inventory-admin"));
//});

// =====================================================================
// Modül 2: API Gateway İmplementasyonu — YARP + Consul Dinamik Servis Keşfi
// Route tanımları sabit (YarpRouteMap); Cluster hedefleri appsettings.json
// yerine ConsulYarpSyncHostedService tarafından Consul'dan periyodik olarak
// beslenir (bkz. o dosyadaki açıklama).
// =====================================================================
var initialRoutes = YarpRouteMap.All
    .Select(m => new RouteConfig
    {
        RouteId = m.RouteId,
        ClusterId = m.ClusterId,
        Match = new RouteMatch { Path = m.PathPattern }
    })
    .ToList();

// Başlangıçta cluster/hedef listesi boştur — ilk Consul senkronizasyonu
// (birkaç saniye içinde) tamamlanana kadar bu route'lara gelen istekler
// "no available destinations" (503) döner. Bu normaldir.
var proxyConfigProvider = new InMemoryConfigProvider(initialRoutes, []);
builder.Services.AddSingleton(proxyConfigProvider);
builder.Services.AddSingleton<IProxyConfigProvider>(proxyConfigProvider);
builder.Services.AddReverseProxy();

// =====================================================================
// Modül 2: Service Discovery — Consul
// Gateway, diğer TÜM servisler gibi kendini de Consul'a kaydeder.
// =====================================================================
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "api-gateway");
builder.Services.AddHostedService<ConsulYarpSyncHostedService>();

// =====================================================================
// Modül 1: Health Check
// Gateway'in kendi bağımlılığı (DB/Redis) olmadığından temel bir health
// check yeterlidir; readiness Consul tarafından bu endpoint üzerinden izlenir.
// =====================================================================
builder.Services.AddHealthChecks();

// =====================================================================
// Modül 2: Rate Limiting — Redis Tabanlı (DAĞITIK)
// İstemci başına (Keycloak "azp"/client_id claim'i ile) 10 saniyede 20
// istek kotası uygulanır. Sayaç REDIS'te tutulur — birden fazla ApiGateway
// instance'ı çalışsa bile limit ekosistem genelinde GERÇEKTEN geçerlidir
// (bkz. RedisRateLimitingMiddleware.cs). Kota aşılırsa istek downstream
// servise HİÇ gitmez, doğrudan 429 döner.
// =====================================================================
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

var app = builder.Build();

app.UseServiceObservability();
app.UseAuthentication();
app.UseAuthorization();
// "azp" claim'ini partition key olarak kullanabilmek için UseAuthentication'dan
// SONRA çalışmalıdır.
app.UseMiddleware<RedisRateLimitingMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapGet("/", () => Results.Ok(new { service = "ApiGateway (YARP)", status = "up" }));

// DEBUG: Token claims'lerini kontrol etmek için (GELIŞTIRME AMAÇLI)
app.MapGet("/debug/claims", (HttpContext context) =>
{
    var user = context.User;
    if (!user.Identity?.IsAuthenticated ?? true)
    {
        return Results.BadRequest(new { message = "Not authenticated" });
    }

    var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(new
    {
        authenticated = user.Identity.IsAuthenticated,
        name = user.Identity.Name,
        claims = claims
    });
}).RequireAuthorization();

// Modül 2: Proxy edilen TÜM istekler geçerli bir JWT gerektirir
// (health/root endpoint'leri hariç — onlar yukarıda ayrıca map'lendi).
// Rate limiting, RedisRateLimitingMiddleware tarafından /api/* için zaten
// pipeline seviyesinde uygulanıyor (yukarıya bkz.) — endpoint'te ayrıca
// bir ".RequireRateLimiting()" çağrısına gerek yoktur.
// Route-specific authorization: her route için kendi policy'si uygulanır
var proxyBuilder = app.MapReverseProxy().RequireAuthorization();

app.Run();
