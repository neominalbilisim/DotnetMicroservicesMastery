using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Payment.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (Choreography): Inventory.Api'nin stok rezerve
/// ettiğini bildiren event'i dinler, ödeme almayı SİMÜLE eder (gerçek bir
/// ödeme entegrasyonu henüz yok — bu, Payment.Domain'in ileride
/// genişletileceği bir alandır).
///
/// TEST KANCASI: CustomerId "FAIL_PAYMENT" ise ödeme BAŞARISIZ simüle
/// edilir (ödeme reddi senaryosu — Order.Api bunun üzerine COMPENSATION
/// tetikleyecektir).
/// </summary>
public class InventoryReservedConsumer(
    ITopicProducer<string, PaymentCompletedEvent> completedProducer,
    ITopicProducer<string, PaymentFailedEvent> failedProducer,
    ILogger<InventoryReservedConsumer> logger) : IConsumer<InventoryReservedEvent>
{
    public async Task Consume(ConsumeContext<InventoryReservedEvent> context)
    {
        var message = context.Message;

        if (message.CustomerId == "FAIL_PAYMENT")
        {
            Console.WriteLine($"🔴 [Payment.Api/Saga] ÖDEME REDDEDİLDİ (simüle) — OrderId={message.OrderId}");
            logger.LogWarning("[Payment.Api/Saga] Ödeme başarısız: OrderId={OrderId}", message.OrderId);

            var failedEvent = new PaymentFailedEvent(
                EventId: Guid.NewGuid(),
                OccurredOnUtc: DateTime.UtcNow,
                OrderId: message.OrderId,
                Reason: "Ödeme reddedildi (simüle edilmiş senaryo — CustomerId=FAIL_PAYMENT).");

            await failedProducer.Produce(failedEvent.PartitionKey, failedEvent, context.CancellationToken);
            return;
        }

        Console.WriteLine($"🟢 [Payment.Api/Saga] ÖDEME ALINDI (simüle) — OrderId={message.OrderId}, Tutar={message.TotalAmount}");
        logger.LogInformation("[Payment.Api/Saga] Ödeme tamamlandı: OrderId={OrderId}", message.OrderId);

        var completedEvent = new PaymentCompletedEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: message.OrderId);

        await completedProducer.Produce(completedEvent.PartitionKey, completedEvent, context.CancellationToken);
    }
}
