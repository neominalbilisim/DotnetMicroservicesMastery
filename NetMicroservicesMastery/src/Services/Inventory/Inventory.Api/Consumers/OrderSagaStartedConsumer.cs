using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (Choreography): Order.Api'nin başlattığı saga'yı
/// dinler, stok rezervasyonunu SİMÜLE eder (gerçek bir stok tablosu henüz
/// yok — bu, Inventory.Domain'in ileride genişletileceği bir alandır).
///
/// TEST KANCASI: CustomerId "FAIL_INVENTORY" ise rezervasyon BAŞARISIZ
/// simüle edilir (stok yok senaryosu).
/// </summary>
public class OrderSagaStartedConsumer(
    ITopicProducer<string, InventoryReservedEvent> reservedProducer,
    ITopicProducer<string, InventoryReservationFailedEvent> reservationFailedProducer,
    ILogger<OrderSagaStartedConsumer> logger) : IConsumer<OrderSagaStartedEvent>
{
    public async Task Consume(ConsumeContext<OrderSagaStartedEvent> context)
    {
        var message = context.Message;

        if (message.CustomerId == "FAIL_INVENTORY")
        {
            Console.WriteLine($"🔴 [Inventory.Api/Saga] STOK REZERVASYONU BAŞARISIZ (simüle) — OrderId={message.OrderId}");
            logger.LogWarning("[Inventory.Api/Saga] Stok rezervasyonu başarısız: OrderId={OrderId}", message.OrderId);

            var failedEvent = new InventoryReservationFailedEvent(
                EventId: Guid.NewGuid(),
                OccurredOnUtc: DateTime.UtcNow,
                OrderId: message.OrderId,
                Reason: "Stok yetersiz (simüle edilmiş senaryo — CustomerId=FAIL_INVENTORY).");

            await reservationFailedProducer.Produce(failedEvent.PartitionKey, failedEvent, context.CancellationToken);
            return;
        }

        Console.WriteLine($"🟢 [Inventory.Api/Saga] STOK REZERVE EDİLDİ (simüle) — OrderId={message.OrderId}");
        logger.LogInformation("[Inventory.Api/Saga] Stok rezerve edildi: OrderId={OrderId}", message.OrderId);

        var reservedEvent = new InventoryReservedEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            TotalAmount: message.TotalAmount);

        await reservedProducer.Produce(reservedEvent.PartitionKey, reservedEvent, context.CancellationToken);
    }
}
