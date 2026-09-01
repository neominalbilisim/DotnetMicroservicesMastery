using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Order.Application.Abstractions;

namespace Order.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern: Payment.Api ödemeyi başarıyla aldığında bu
/// consumer tetiklenir — SAGA BAŞARIYLA TAMAMLANIR (Order.Status = Confirmed).
/// </summary>
public class PaymentCompletedConsumer(
    IOrderRepository repository,
    ILogger<PaymentCompletedConsumer> logger) : IConsumer<PaymentCompletedEvent>
{
    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var orderId = context.Message.OrderId;
        var order = await repository.GetByIdAsync(orderId, context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning("[Order.Api/Saga] PaymentCompletedEvent alındı ama OrderId={OrderId} bulunamadı.", orderId);
            return;
        }

        order.MarkConfirmed();
        await repository.SaveChangesAsync(context.CancellationToken);

        Console.WriteLine($"✅ [Order.Api] SAGA BAŞARIYLA TAMAMLANDI — OrderId={orderId}, Status=Confirmed.");
        logger.LogInformation("[Order.Api/Saga] Sipariş onaylandı: OrderId={OrderId}", orderId);
    }
}
