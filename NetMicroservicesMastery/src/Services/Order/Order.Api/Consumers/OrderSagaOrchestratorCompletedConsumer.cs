using BuildingBlocks.Messaging.Contracts.Saga;
using MassTransit;
using Microsoft.Extensions.Logging;
using Order.Application.Abstractions;

namespace Order.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION): Saga.Api saga'yı başarıyla
/// tamamladığında bu consumer tetiklenir (RabbitMQ, SagaQueues.OrderSagaCompleted
/// kuyruğu). Choreography'deki PaymentCompletedConsumer ile KARIŞTIRILMAMALIDIR
/// — bu, Saga.Api'den (ayrı servis) gelen sonuçtur.
/// </summary>
public class OrderSagaOrchestratorCompletedConsumer(
    IOrderRepository repository,
    ILogger<OrderSagaOrchestratorCompletedConsumer> logger) : IConsumer<OrderSagaCompletedEvent>
{
    public async Task Consume(ConsumeContext<OrderSagaCompletedEvent> context)
    {
        var orderId = context.Message.CorrelationId;
        var order = await repository.GetByIdAsync(orderId, context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning("[Order.Api/SagaOrchestrator] OrderSagaCompletedEvent alındı ama OrderId={OrderId} bulunamadı.", orderId);
            return;
        }

        order.MarkConfirmed();
        await repository.SaveChangesAsync(context.CancellationToken);

        Console.WriteLine($"✅ [Order.Api/SagaOrchestrator] SAGA (Saga.Api'den) BAŞARIYLA TAMAMLANDI — OrderId={orderId}, Status=Confirmed.");
        logger.LogInformation("[Order.Api/SagaOrchestrator] Sipariş onaylandı: OrderId={OrderId}", orderId);
    }
}
