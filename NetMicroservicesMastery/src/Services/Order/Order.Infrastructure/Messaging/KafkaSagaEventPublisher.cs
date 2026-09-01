using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Order.Application.Abstractions;

namespace Order.Infrastructure.Messaging;

/// <summary>
/// Modül 4 - ISagaEventPublisher'ın somut Kafka implementasyonu. Sadece
/// "/submit-order-saga" (dedike Saga demo endpoint'i) tarafından kullanılır.
/// </summary>
public class KafkaSagaEventPublisher(
    ITopicProducer<string, OrderSagaStartedEvent> sagaStartedProducer,
    ITopicProducer<string, ReleaseInventoryCommand> releaseInventoryProducer) : ISagaEventPublisher
{
    public async Task PublishOrderSagaStartedAsync(
        Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var evt = new OrderSagaStartedEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: orderId,
            CustomerId: customerId,
            TotalAmount: totalAmount);

        await sagaStartedProducer.Produce(evt.PartitionKey, evt, cancellationToken);
    }

    public async Task SendReleaseInventoryCommandAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var command = new ReleaseInventoryCommand(
            CommandId: Guid.NewGuid(),
            OrderId: orderId);

        await releaseInventoryProducer.Produce(command.PartitionKey, command, cancellationToken);
    }
}
