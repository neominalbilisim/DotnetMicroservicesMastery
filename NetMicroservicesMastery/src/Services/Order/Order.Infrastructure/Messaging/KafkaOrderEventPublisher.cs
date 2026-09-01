using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Order.Application.Abstractions;

namespace Order.Infrastructure.Messaging;

/// <summary>
/// Modül 3/4 - IOrderEventPublisher'ın DOĞRUDAN Kafka'ya üreten (outbox'suz)
/// implementasyonu. `/submit-order` endpoint'i bunu kullanır — bu, Modül 3'ten
/// beri değişmeyen, "sade" CQRS + mesajlaşma davranışıdır.
///
/// Outbox Pattern'in (atomiklik garantisi) ayrı bir demo/test endpoint'inde
/// (`/submit-order-outbox`) gösterilmesi için bkz. OutboxOrderEventPublisher.cs
/// ve SubmitOrderWithOutboxCommand.cs — iki yaklaşımın birbirinden net
/// ayrılması, hangi endpoint'in hangi modülü/konuyu gösterdiğinin
/// karışmaması içindir.
/// </summary>
public class KafkaOrderEventPublisher(
    ITopicProducer<string, OrderCreatedEvent> eventProducer,
    ITopicProducer<string, ProcessPaymentCommand> commandProducer) : IOrderEventPublisher
{
    public async Task PublishOrderCreatedAsync(
        Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        // 1) EVENT (Publish semantiği): Payment.Api VE Inventory.Api bağımsız dinler.
        var orderCreatedEvent = new OrderCreatedEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: orderId,
            CustomerId: customerId,
            TotalAmount: totalAmount);
        await eventProducer.Produce(orderCreatedEvent.PartitionKey, orderCreatedEvent, cancellationToken);

        // 2) COMMAND (Send semantiği): SADECE Payment.Api dinler.
        var processPaymentCommand = new ProcessPaymentCommand(
            CommandId: Guid.NewGuid(),
            OrderId: orderId,
            CustomerId: customerId,
            Amount: totalAmount);
        await commandProducer.Produce(processPaymentCommand.PartitionKey, processPaymentCommand, cancellationToken);
    }
}
