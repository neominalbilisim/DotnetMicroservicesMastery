using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Order.Application.Abstractions;

namespace Order.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern: Inventory.Api stok rezervasyonunda başarısız
/// olduğunda bu consumer tetiklenir — SAGA BAŞARISIZ olur (Order.Status =
/// Cancelled). NOT: Bu durumda COMPENSATION GEREKMEZ — henüz hiçbir şey
/// rezerve edilmediği için geri alınacak bir şey yoktur (PaymentFailedConsumer
/// ile karşılaştırın: orada compensation VARDIR, çünkü stok ZATEN rezerve
/// edilmişti).
/// </summary>
public class InventoryReservationFailedConsumer(
    IOrderRepository repository,
    ILogger<InventoryReservationFailedConsumer> logger) : IConsumer<InventoryReservationFailedEvent>
{
    public async Task Consume(ConsumeContext<InventoryReservationFailedEvent> context)
    {
        var orderId = context.Message.OrderId;
        var order = await repository.GetByIdAsync(orderId, context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning("[Order.Api/Saga] InventoryReservationFailedEvent alındı ama OrderId={OrderId} bulunamadı.", orderId);
            return;
        }

        order.MarkCancelled();
        await repository.SaveChangesAsync(context.CancellationToken);

        Console.WriteLine($"🔴 [Order.Api] SAGA BAŞARISIZ (stok yok) — OrderId={orderId}, Hata={context.Message.Reason}. Compensation GEREKMİYOR (hiçbir şey rezerve edilmemişti).");
        logger.LogWarning("[Order.Api/Saga] Sipariş iptal edildi (stok yok): OrderId={OrderId}, Hata={Reason}", orderId, context.Message.Reason);
    }
}
