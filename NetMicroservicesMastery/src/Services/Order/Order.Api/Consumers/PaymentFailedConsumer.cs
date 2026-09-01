using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Order.Application.Abstractions;

namespace Order.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern: Payment.Api ödemeyi reddettiğinde bu consumer
/// tetiklenir — SAGA BAŞARISIZ olur (Order.Status = Cancelled) VE
/// COMPENSATION tetiklenir (ReleaseInventoryCommand gönderilir), çünkü bu
/// noktada stok ZATEN rezerve edilmişti — geri bırakılması gerekir.
/// </summary>
public class PaymentFailedConsumer(
    IOrderRepository repository,
    ISagaEventPublisher sagaPublisher,
    ILogger<PaymentFailedConsumer> logger) : IConsumer<PaymentFailedEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var orderId = context.Message.OrderId;
        var order = await repository.GetByIdAsync(orderId, context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning("[Order.Api/Saga] PaymentFailedEvent alındı ama OrderId={OrderId} bulunamadı.", orderId);
            return;
        }

        order.MarkCancelled();
        await repository.SaveChangesAsync(context.CancellationToken);

        Console.WriteLine($"🔴 [Order.Api] SAGA BAŞARISIZ (ödeme reddedildi) — OrderId={orderId}, Hata={context.Message.Reason}. Telafi (stok iadesi) tetikleniyor...");
        logger.LogWarning("[Order.Api/Saga] Sipariş iptal edildi (ödeme başarısız): OrderId={OrderId}, Hata={Reason}", orderId, context.Message.Reason);

        // COMPENSATING ADIM: stok geri bırakılsın.
        await sagaPublisher.SendReleaseInventoryCommandAsync(orderId, context.CancellationToken);
    }
}
