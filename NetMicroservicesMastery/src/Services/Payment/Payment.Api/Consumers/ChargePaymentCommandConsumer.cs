using BuildingBlocks.Messaging.Contracts.Saga;
using BuildingBlocks.Messaging.Idempotency;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Payment.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION): Saga.Api'nin gönderdiği ödeme
/// talebini işler, sonucu (başarı/hata) doğrudan Saga.Api'nin dinlediği
/// tek kuyruğa (SagaQueues.OrderSagaOrchestrator) geri gönderir. Choreography
/// örneğindeki InventoryReservedConsumer ile KARIŞTIRILMAMALIDIR — bu,
/// Saga.Api'den (ayrı servis) gelen bir Command'ı işler.
///
/// Modül 5 - "Idempotent Consumer": Bu, idempotency'nin EN KRİTİK olduğu
/// örnektir — "ödeme alma" işlemi TEKRAR çalıştırılırsa müşteriden İKİNCİ
/// KEZ para çekilmiş olur. Mesaj (context.MessageId ile) DAHA ÖNCE
/// işlenmişse, ödeme mantığı HİÇ ÇALIŞTIRILMADAN atlanır.
///
/// TEST KANCASI: CustomerId "FAIL_PAYMENT" ise ödeme BAŞARISIZ simüle edilir
/// (Choreography ile AYNI konvansiyon).
/// </summary>
public class ChargePaymentCommandConsumer(
    ISendEndpointProvider sendEndpointProvider,
    IIdempotencyStore idempotencyStore,
    ILogger<ChargePaymentCommandConsumer> logger) : IConsumer<ChargePaymentCommand>
{
    public async Task Consume(ConsumeContext<ChargePaymentCommand> context)
    {
        var message = context.Message;
        var messageId = context.MessageId;

        // ==================== Modül 5 - Idempotent Consumer KONTROLÜ ====================
        // MessageId yoksa (teorik olarak MassTransit her mesaja otomatik bir
        // tane atar) kontrol atlanır, güvenli tarafta kal(ın)arak işlenir.
        if (messageId is not null && await idempotencyStore.HasBeenProcessedAsync(messageId.Value, context.CancellationToken))
        {
            Console.WriteLine($"⏭️ [Payment.Api/Idempotency] Mesaj DAHA ÖNCE işlendi, ödeme TEKRAR ALINMAYACAK — MessageId={messageId}, OrderId={message.CorrelationId}");
            logger.LogInformation(
                "[Payment.Api/Idempotency] Mesaj zaten işlenmiş, atlanıyor: MessageId={MessageId}, OrderId={OrderId}",
                messageId, message.CorrelationId);
            return; // Ödeme mantığı HİÇ ÇALIŞTIRILMAZ.
        }
        // =================================================================================

        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{SagaQueues.OrderSagaOrchestrator}"));

        if (message.CustomerId == "FAIL_PAYMENT")
        {
            Console.WriteLine($"🔴 [Payment.Api/SagaOrchestrator] ÖDEME REDDEDİLDİ (simüle) — OrderId={message.CorrelationId}");
            logger.LogWarning("[Payment.Api/SagaOrchestrator] Ödeme başarısız: OrderId={OrderId}", message.CorrelationId);

            await endpoint.Send(new PaymentChargeFailedEvent(
                message.CorrelationId,
                "Ödeme reddedildi (simüle edilmiş senaryo — CustomerId=FAIL_PAYMENT)."), context.CancellationToken);
        }
        else
        {
            Console.WriteLine($"🟢 [Payment.Api/SagaOrchestrator] ÖDEME ALINDI (simüle) — OrderId={message.CorrelationId}, Tutar={message.TotalAmount}");
            logger.LogInformation("[Payment.Api/SagaOrchestrator] Ödeme tamamlandı: OrderId={OrderId}", message.CorrelationId);

            await endpoint.Send(new PaymentChargedEvent(message.CorrelationId), context.CancellationToken);
        }

        // İşlem (başarılı ya da başarısız SONUCU üretilmiş olsun) tamamlandı
        // — bu MessageId bir daha ASLA tekrar işlenmeyecek şekilde kaydedilir.
        if (messageId is not null)
        {
            await idempotencyStore.MarkAsProcessedAsync(messageId.Value, nameof(ChargePaymentCommandConsumer), context.CancellationToken);
        }
    }
}
