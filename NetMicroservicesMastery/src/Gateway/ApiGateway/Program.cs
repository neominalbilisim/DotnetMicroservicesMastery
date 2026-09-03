using ApiGateway.Services;
using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Observability;
using BuildingBlocks.Security;
using Consul;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Yarp.ReverseProxy.Configuration;


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
builder.AddServiceObservability(serviceName: "gateway-service");

// =====================================================================
// Modül 2: Merkezi Kimlik Doğrulama — Keycloak (AuthServer)
// MİMARİ KARAR: JWT doğrulaması SADECE Gateway'de yapılır. Downstream
// servisler (Order/Payment/Inventory) token doğrulamaz.
// =====================================================================
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("order-service", policy =>
    {
        policy.RequireAuthenticatedUser().RequireClaim("scope", "order-admin");
    });
});

// =====================================================================
// Modül 2: API Gateway İmplementasyonu — YARP + Consul Dinamik Servis Keşfi
// Route tanımları sabit (YarpRouteMap); Cluster hedefleri appsettings.json
// yerine ConsulYarpSyncHostedService tarafından Consul'dan periyodik olarak
// beslenir (bkz. o dosyadaki açıklama).
// =====================================================================
// Önce somut sınıfın kendisini Singleton olarak kaydedin
builder.Services.AddSingleton<ConsulProxyConfigProvider>();
// YARP'ın ihtiyaç duyduğu IProxyConfigProvider arayüzünü, yukarıdaki kayda yönlendirin
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<ConsulProxyConfigProvider>());
// Son olarak Hosted Service'i ekleyin
builder.Services.AddHostedService<ConsulConfigRefreshService>();
// YARP'ı ekleyin
builder.Services.AddReverseProxy();

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

builder.Services.AddSingleton<IConsulClient, ConsulClient>(p => new ConsulClient(consulConfig =>
{
  consulConfig.Address = new Uri("http://localhost:8500"); // Local
  // consulConfig.Address = new Uri("http://consul1:8500"); // Docker Prod
}));
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

// Modül 2: Proxy edilen TÜM istekler geçerli bir JWT gerektirir
// (health/root endpoint'leri hariç — onlar yukarıda ayrıca map'lendi).
// Rate limiting, RedisRateLimitingMiddleware tarafından /api/* için zaten
// pipeline seviyesinde uygulanıyor (yukarıya bkz.) — endpoint'te ayrıca
// bir ".RequireRateLimiting()" çağrısına gerek yoktur.
app.MapReverseProxy().RequireAuthorization();

app.Run();
