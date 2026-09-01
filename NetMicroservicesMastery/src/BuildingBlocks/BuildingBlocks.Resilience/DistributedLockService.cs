using Microsoft.Extensions.Configuration;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace BuildingBlocks.Resilience;

/// <summary>
/// Modül 5 - "Multi-Instance Senaryoları: Distributed Lock".
/// Redis (Redlock algoritması) üzerinden, aynı işin/kaynağın birden fazla
/// instance tarafından aynı anda işlenmesini engelleyen kilit servisi.
/// Ortak (paylaşılan) bir BuildingBlock olarak tanımlıdır — hem Order.Api'nin
/// OutboxProcessorHostedService'i hem JobService'in recurring job'ları bu
/// AYNI implementasyonu kullanır.
///
///   Instance A/B (Aynı işi yapmaya çalışır) -> Distributed Lock (Redis)
///     -> Sadece BİRİ kilidi alır, işi yapar; diğeri o turu ATLAR
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Kilidi almayı dener. Başarılıysa, Dispose edildiğinde (using ile)
    /// kilidi HEMEN serbest bırakan bir nesne döner. Başka bir instance
    /// kilidi zaten tutuyorsa null döner (BEKLEMEZ — hemen null döner,
    /// çağıran taraf "bu turu atla" kararını verebilsin diye).
    /// </summary>
    Task<IDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default);
}

public class DistributedLockService : IDistributedLockService, IDisposable
{
    private readonly RedLockFactory _redLockFactory;

    public DistributedLockService(IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379";
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);

        // RedLock algoritması normalde BİRDEN FAZLA bağımsız Redis instance'ı
        // üzerinden konsensüs kurarak çalışır (gerçek "dağıtık" kilit).
        // Bu eğitim projesinde TEK bir Redis instance'ı olduğu için sadece
        // bir tane RedLockMultiplexer veriliyor — pratikte "tekil Redis
        // üzerinde atomik kilit" davranışı gösterir (üretimde birden fazla
        // bağımsız Redis node'u önerilir).
        _redLockFactory = RedLockFactory.Create([new RedLockMultiplexer(multiplexer)]);
    }

    public async Task<IDisposable?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default)
    {
        var redLock = await _redLockFactory.CreateLockAsync(resource, expiry);

        if (redLock.IsAcquired)
        {
            return redLock; // IRedLock IDisposable'dır — Dispose() kilidi hemen serbest bırakır.
        }

        redLock.Dispose();
        return null;
    }

    public void Dispose() => _redLockFactory.Dispose();
}
