using BuildingBlocks.Messaging.Contracts.Saga;
using BuildingBlocks.Messaging.Idempotency;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION): Saga.Api'nin gönderdiği stok
/// rezervasyon talebini işler, sonucu (başarı/hata) doğrudan Saga.Api'nin
/// dinlediği tek kuyruğa (SagaQueues.OrderSagaOrchestrator) geri gönderir.
/// Choreography örneğindeki OrderSagaStartedConsumer ile KARIŞTIRILMAMALIDIR
/// — bu, Saga.Api'den (ayrı servis) gelen bir Command'ı işler.
///
/// Modül 5 - "Idempotent Consumer": Mesaj (context.MessageId ile) DAHA ÖNCE
/// işlenmişse, stok rezervasyon mantığı HİÇ ÇALIŞTIRILMADAN atlanır —
/// aksi halde aynı sipariş için stok İKİ KEZ rezerve edilmiş (düşülmüş) olurdu.
///
/// TEST KANCASI: CustomerId "FAIL_INVENTORY" ise rezervasyon BAŞARISIZ
/// simüle edilir (Choreography ile AYNI konvansiyon).
/// </summary>
public class ReserveInventoryCommandConsumer(
    ISendEndpointProvider sendEndpointProvider,
    IIdempotencyStore idempotencyStore,
    ILogger<ReserveInventoryCommandConsumer> logger) : IConsumer<ReserveInventoryCommand>
{
    public async Task Consume(ConsumeContext<ReserveInventoryCommand> context)
    {
        var message = context.Message;
        var messageId = context.MessageId;

        // ==================== Modül 5 - Idempotent Consumer KONTROLÜ ====================
        if (messageId is not null && await idempotencyStore.HasBeenProcessedAsync(messageId.Value, context.CancellationToken))
        {
            Console.WriteLine($"⏭️ [Inventory.Api/Idempotency] Mesaj DAHA ÖNCE işlendi, stok TEKRAR REZERVE EDİLMEYECEK — MessageId={messageId}, OrderId={message.CorrelationId}");
            logger.LogInformation(
                "[Inventory.Api/Idempotency] Mesaj zaten işlenmiş, atlanıyor: MessageId={MessageId}, OrderId={OrderId}",
                messageId, message.CorrelationId);
            return; // Rezervasyon mantığı HİÇ ÇALIŞTIRILMAZ.
        }
        // =================================================================================

        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{SagaQueues.OrderSagaOrchestrator}"));

        if (message.CustomerId == "FAIL_INVENTORY")
        {
            Console.WriteLine($"🔴 [Inventory.Api/SagaOrchestrator] STOK REZERVASYONU BAŞARISIZ (simüle) — OrderId={message.CorrelationId}");
            logger.LogWarning("[Inventory.Api/SagaOrchestrator] Stok rezervasyonu başarısız: OrderId={OrderId}", message.CorrelationId);

            await endpoint.Send(new InventoryReservationRejectedEvent(
                message.CorrelationId,
                "Stok yetersiz (simüle edilmiş senaryo — CustomerId=FAIL_INVENTORY)."), context.CancellationToken);
        }
        else
        {
            Console.WriteLine($"🟢 [Inventory.Api/SagaOrchestrator] STOK REZERVE EDİLDİ (simüle) — OrderId={message.CorrelationId}");
            logger.LogInformation("[Inventory.Api/SagaOrchestrator] Stok rezerve edildi: OrderId={OrderId}", message.CorrelationId);

            await endpoint.Send(new InventoryReservationSucceededEvent(message.CorrelationId), context.CancellationToken);
        }

        if (messageId is not null)
        {
            await idempotencyStore.MarkAsProcessedAsync(messageId.Value, nameof(ReserveInventoryCommandConsumer), context.CancellationToken);
        }
    }
}
