using System.Text.Json;
using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Events;
using BuildingBlocks.Resilience;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Order.Infrastructure.Persistence;

namespace Order.Infrastructure.Messaging;

/// <summary>
/// Modül 4 - Outbox Pattern'in "Delivery Service" (2. adım) kısmı.
/// Periyodik olarak (5sn) OutboxMessages tablosundaki İŞLENMEMİŞ satırları
/// okur, her birini gerçek Kafka topic'ine PRODUCE eder, başarılıysa
/// ProcessedOnUtc'yi doldurur. Kafka geçici olarak erişilemez olsa bile
/// mesaj veritabanında KAYBOLMAZ — bir sonraki turda tekrar denenir.
///
///   DB'de bekleyen outbox satırları -> Kafka'ya PRODUCE -> ProcessedOnUtc doldurulur
///
/// Modül 5 - Distributed Lock: Birden fazla Order.Api instance'ı ÇALIŞIYORSA
/// (bkz. Modül 2'deki Inventory.Api multi-instance testi ile aynı senaryo),
/// her instance'ın KENDİ 5sn'lik zamanlayıcısı BAĞIMSIZ çalışır — bu,
/// AYNI outbox satırının iki instance tarafından AYNI ANDA okunup Kafka'ya
/// İKİ KEZ üretilmesi riskini doğurur. Redis tabanlı bir dağıtık kilit
/// (IDistributedLockService), her turda SADECE BİR instance'ın işlem
/// yapmasını garanti eder — kilidi alamayan instance o turu sessizce atlar.
/// </summary>
public class OutboxProcessorHostedService(
    IServiceScopeFactory scopeFactory,
    IDistributedLockService distributedLockService,
    ILogger<OutboxProcessorHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    // Konsolda hangi instance'ın kilidi aldığını/atladığını ayırt
    // edebilmeniz için (çoklu-instance testi sırasında) her instance
    // başlangıçta kendine kısa, rastgele bir kimlik üretir.
    private readonly string _instanceId = Guid.NewGuid().ToString()[..8];

    // Redis'teki kilit anahtarı — TÜM Order.Api instance'ları için AYNIDIR
    // (kasıtlı olarak): "hangi instance olursa olsun, aynı anda sadece
    // biri bu kaynağı işlesin" demektir.
    private const string LockResource = "order-service:outbox-processor";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[Outbox] Instance başlatıldı: {InstanceId}", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Outbox] İşleme döngüsünde beklenmeyen hata.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Uygulama kapanıyor — normal.
            }
        }
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        // Modül 5 - Distributed Lock: kilidi ALAMAZSAK (başka bir instance
        // zaten tutuyorsa) bu turu TAMAMEN atlıyoruz — outbox'a hiç bakmadan
        // çıkıyoruz. Bir sonraki 5sn'lik pollde tekrar deneriz.
        using var @lock = await distributedLockService.TryAcquireAsync(LockResource, LockExpiry, ct);
        if (@lock is null)
        {
            Console.WriteLine($"🔓 [Outbox][{_instanceId}] Kilit başka bir instance'ta — bu tur ATLANDI.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pendingMessages.Count == 0)
        {
            return;
        }

        Console.WriteLine($"🔒 [Outbox][{_instanceId}] Kilit ALINDI — {pendingMessages.Count} mesaj işlenecek.");

        foreach (var message in pendingMessages)
        {
            try
            {
                await DispatchAsync(message.Type, message.Content, message.PartitionKey, scope.ServiceProvider, ct);
                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = null;

                Console.WriteLine($"📤 [Outbox][{_instanceId}] Mesaj Kafka'ya iletildi: Topic={message.Topic}, PartitionKey={message.PartitionKey}");
            }
            catch (Exception ex)
            {
                // Mesaj İŞLENMEMİŞ olarak kalır (ProcessedOnUtc = null) — bir
                // sonraki turda TEKRAR denenir. Kafka geçici olarak erişilemez
                // olsa bile hiçbir mesaj kaybolmaz.
                message.Error = ex.Message;
                logger.LogWarning(ex,
                    "[Outbox][{InstanceId}] Mesaj iletilemedi, bir sonraki turda tekrar denenecek: Id={Id}, Topic={Topic}",
                    _instanceId, message.Id, message.Topic);
            }
        }

        await dbContext.SaveChangesAsync(ct);
        // Kilit, bu metod dönüşte 'using' bloğu sayesinde HEMEN serbest
        // bırakılır — 30sn'lik expiry'nin dolmasını beklemeye gerek yoktur,
        // bu da diğer instance'ın bir sonraki turda kilidi almasını kolaylaştırır.
    }

    private static async Task DispatchAsync(
        string messageType, string content, string partitionKey, IServiceProvider services, CancellationToken ct)
    {
        if (messageType.StartsWith(typeof(OrderCreatedEvent).FullName!, StringComparison.Ordinal))
        {
            var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(content)
                ?? throw new InvalidOperationException("OrderCreatedEvent deserialize edilemedi.");
            var producer = services.GetRequiredService<ITopicProducer<string, OrderCreatedEvent>>();
            await producer.Produce(partitionKey, evt, ct);
        }
        else if (messageType.StartsWith(typeof(ProcessPaymentCommand).FullName!, StringComparison.Ordinal))
        {
            var cmd = JsonSerializer.Deserialize<ProcessPaymentCommand>(content)
                ?? throw new InvalidOperationException("ProcessPaymentCommand deserialize edilemedi.");
            var producer = services.GetRequiredService<ITopicProducer<string, ProcessPaymentCommand>>();
            await producer.Produce(partitionKey, cmd, ct);
        }
        else
        {
            throw new InvalidOperationException($"Bilinmeyen outbox mesaj tipi: {messageType}");
        }
    }
}
