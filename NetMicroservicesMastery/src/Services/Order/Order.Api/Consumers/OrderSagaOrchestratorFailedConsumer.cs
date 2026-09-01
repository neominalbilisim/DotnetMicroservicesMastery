using BuildingBlocks.Messaging.Contracts.Saga;
using MassTransit;
using Microsoft.Extensions.Logging;
using Order.Application.Abstractions;

namespace Order.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION): Saga.Api saga'nın başarısız
/// olduğunu bildirdiğinde bu consumer tetiklenir (RabbitMQ, SagaQueues.OrderSagaFailed
/// kuyruğu). Compensation (stok iadesi) SAGA.API TARAFINDAN zaten tetiklenmiştir
/// (bkz. OrderSagaStateMachine.cs) — Order.Api burada sadece kendi durumunu günceller.
/// </summary>
public class OrderSagaOrchestratorFailedConsumer(
    IOrderRepository repository,
    ILogger<OrderSagaOrchestratorFailedConsumer> logger) : IConsumer<OrderSagaFailedEvent>
{
    public async Task Consume(ConsumeContext<OrderSagaFailedEvent> context)
    {
        var orderId = context.Message.CorrelationId;
        var order = await repository.GetByIdAsync(orderId, context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning("[Order.Api/SagaOrchestrator] OrderSagaFailedEvent alındı ama OrderId={OrderId} bulunamadı.", orderId);
            return;
        }

        order.MarkCancelled();
        await repository.SaveChangesAsync(context.CancellationToken);

        Console.WriteLine($"🔴 [Order.Api/SagaOrchestrator] SAGA (Saga.Api'den) BAŞARISIZ — OrderId={orderId}, Hata={context.Message.Reason}, Status=Cancelled.");
        logger.LogWarning("[Order.Api/SagaOrchestrator] Sipariş iptal edildi: OrderId={OrderId}, Hata={Reason}", orderId, context.Message.Reason);
    }
}
