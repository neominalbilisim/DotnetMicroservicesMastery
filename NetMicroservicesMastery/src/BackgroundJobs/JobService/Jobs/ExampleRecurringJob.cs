using BuildingBlocks.Resilience;
using Microsoft.Extensions.Logging;

namespace JobService.Jobs;

/// <summary>
/// Modül 5 - Hangfire iş örneği. Hem fire-and-forget/delayed (elle
/// tetiklenen) hem recurring (zamanlanmış, otomatik tetiklenen) senaryolarda
/// KULLANILAN AYNI iş.
///
/// Distributed Lock: JobService birden fazla instance ile (scale-out)
/// çalıştırılabilir. Hangfire'ın KENDİ recurring job mekanizması, aynı
/// job'un birden fazla worker tarafından PARALEL çalıştırılmamasını byte
/// düzeyinde zaten büyük ölçüde engeller (Hangfire storage'ın kendi
/// "distributed lock" benzeri mekanizması vardır) — ama bunu ekstra bir
/// güvence katmanıyla (aynı Redis tabanlı IDistributedLockService,
/// Order.Api'nin OutboxProcessorHostedService'te kullandığı İLE AYNI
/// implementasyon) pekiştiriyoruz; böylece Modül 5'in "Distributed Lock"
/// konusunu Hangfire bağlamında da somut olarak göstermiş oluyoruz.
/// </summary>
public interface IReportJob
{
    Task RunAsync(CancellationToken ct = default);
}

public class ExampleReportJob(
    IDistributedLockService distributedLockService,
    ILogger<ExampleReportJob> logger) : IReportJob
{
    private const string LockResource = "job-service:example-report-job";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromMinutes(2);

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var @lock = await distributedLockService.TryAcquireAsync(LockResource, LockExpiry, ct);
        if (@lock is null)
        {
            Console.WriteLine("🔓 [JobService] ExampleReportJob — kilit başka bir instance'ta, bu çalışma ATLANDI.");
            logger.LogInformation("[JobService] ExampleReportJob kilidi alınamadı, atlanıyor.");
            return;
        }

        Console.WriteLine($"🔒 [JobService] ExampleReportJob BAŞLADI — {DateTime.UtcNow:u}");
        logger.LogInformation("[JobService] ExampleReportJob çalışıyor...");

        // Gerçek rapor oluşturma mantığı burada simüle edilir.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        Console.WriteLine($"✅ [JobService] ExampleReportJob TAMAMLANDI — {DateTime.UtcNow:u}");
        logger.LogInformation("[JobService] ExampleReportJob tamamlandı.");
    }
}
