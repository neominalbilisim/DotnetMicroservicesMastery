using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ApiGateway.Services;

/// <summary>
/// Modül 2 - "Rate Limiting" (Redis tabanlı, DAĞITIK versiyon).
///
/// Neden Redis? ASP.NET Core'un yerleşik Microsoft.AspNetCore.RateLimiting
/// middleware'i tamamen IN-MEMORY çalışır — birden fazla ApiGateway instance'ı
/// (Modül 5'teki multi-instance senaryosu gibi) çalıştırıldığında, her
/// instance kendi sayacını ayrı tutar; "10sn'de 20 istek" limiti gerçekte
/// "instance başına 10sn'de 20 istek"e dönüşür. Redis'te tutulan PAYLAŞIMLI
/// bir sayaç ile, kaç ApiGateway instance'ı olursa olsun limit GERÇEKTEN
/// tüm ekosistem için geçerli olur.
///
/// Algoritma: Fixed Window Counter (Lua script ile atomik INCR+EXPIRE).
/// Basit ve anlaşılır olması için tercih edildi; pencere sınırlarında küçük
/// bir "burst" payı vardır (bilinen bir sınırlamadır — Sliding Window Redis
/// algoritmaları bunu çözer ama daha karmaşıktır).
///
///   İstek -> partitionKey (Keycloak "azp" claim'i) -> Redis INCR (Lua) ->
///   limit aşıldıysa 429, aşılmadıysa X-RateLimit-* header'larıyla devam
/// </summary>
public class RedisRateLimitingMiddleware(
    RequestDelegate next,
    IConnectionMultiplexer redis,
    IConfiguration configuration,
    ILogger<RedisRateLimitingMiddleware> logger)
{
  // Betik, belirli bir anahtarın (örneğin bir kullanıcı ID'si veya IP adresi) belirli bir süre zarfında kaç kez istek attığını hesaplar:
  // Lua betikleri Redis motorunda atomik (bölünemez tek bir işlem) olarak çalıştığı için eşzamanlılık (concurrency) sorunlarını tamamen ortadan kaldırır.
  private const string FixedWindowLuaScript = """
        local current = redis.call('INCR', KEYS[1])
        if tonumber(current) == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('TTL', KEYS[1])
        return { current, ttl }
        """;

  // Bir kullanıcı (veya IP adresi) 60 saniye içinde en fazla 5 istek atabilir.
  // KEYS[1]: Kullanıcının kimliği (örneğin: rate_limit:192.168.1.5)
  // ARGV[1]: Zaman penceresinin süresi (örneğin: 60 saniye)
  


  private readonly int _permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 20);
    private readonly int _windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 10);

    public async Task InvokeAsync(HttpContext context)
    {
        // Sadece proxy edilen /api/* trafiği kotaya tabidir; health check ve
        // root endpoint'leri (Consul'un sürekli attığı istekler dahil) muaftır.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var partitionKey = context.User.FindFirst("azp")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        var redisKey = $"ratelimit:gateway:{partitionKey}";

        try
        {
            var db = redis.GetDatabase();
            var result = (RedisResult[])(await db.ScriptEvaluateAsync(
                FixedWindowLuaScript,
                keys: [redisKey],
                values: [_windowSeconds]))!;

            var current = (long)result[0];
            var ttlSeconds = (long)result[1];
            var remaining = Math.Max(0, _permitLimit - current);

            context.Response.Headers["X-RateLimit-Limit"] = _permitLimit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
            context.Response.Headers["X-RateLimit-Reset"] = ttlSeconds.ToString();

            if (current > _permitLimit)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = ttlSeconds.ToString();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    $$"""{"title":"Çok fazla istek gönderildi","status":429,"detail":"Lütfen {{ttlSeconds}} saniye sonra tekrar deneyin."}""");
                return;
            }
        }
        catch (Exception ex)
        {
            // "Fail open": Redis'e ulaşılamazsa rate limiting bu istek için
            // ATLANIR — Gateway'in tamamen durması, rate limiting'in kendisinden
            // çok daha büyük bir sorun olurdu. Uyarı loglanır ama istek engellenmez.
            logger.LogWarning(ex, "[RateLimit] Redis'e ulaşılamadı, bu istek için rate limiting atlandı.");
        }

        await next(context);
    }
}
