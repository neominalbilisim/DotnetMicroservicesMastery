namespace Inventory.Infrastructure.Locking;

/// <summary>
/// Modül 5 - "Multi-Instance Senaryoları: Distributed Lock".
/// Redis (Redlock algoritması) üzerinden, aynı işin/kaynağın birden fazla
/// instance tarafından aynı anda işlenmesini engelleyen kilit servisi.
///
///   Instance A/B/C (Aynı Mesajı Alır) -> Distributed Lock (Redis) -> Idempotent İşleme
///
/// TODO (Modül 5):
///   1) RedLock.net ile RedLockFactory oluştur (appsettings "Redis:ConnectionString").
///   2) TryAcquireAsync(resource, expiry) ile kilidi al; alınamazsa işi atla/tekrar dene.
///   3) using bloğu ile kilit otomatik serbest bırakılacak şekilde tasarla.
/// </summary>
public interface IDistributedLockService
{
    Task<bool> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default);
}

public class DistributedLockService : IDistributedLockService
{
    public Task<bool> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default)
    {
        throw new NotImplementedException("Modül 5 çalışmasında RedLock.net ile doldurulacak.");
    }
}
