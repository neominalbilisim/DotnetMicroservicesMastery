using System.Text.Json;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Events;
using BuildingBlocks.Messaging.Outbox;
using Order.Application.Abstractions;
using Order.Infrastructure.Persistence;

namespace Order.Infrastructure.Messaging;

/// <summary>
/// Modül 4 - IOutboxOrderEventPublisher'ın implementasyonu. Sadece
/// "/submit-order-outbox" (dedike Outbox demo endpoint'i) tarafından
/// kullanılır — "/submit-order" (sade, Modül 3 tarzı) bunu KULLANMAZ,
/// onun yerine KafkaOrderEventPublisher (doğrudan Kafka) kullanır. Bu
/// bilinçli ayrım, hangi endpoint'in hangi konuyu gösterdiğinin
/// karışmaması içindir.
///
/// KRİTİK NOKTA: Bu metod SaveChangesAsync ÇAĞIRMAZ — çağıran taraf
/// (SubmitOrderWithOutboxCommandHandler), Order kaydı İLE bu outbox
/// satırlarını AYNI SaveChangesAsync çağrısında, tek transaction'da
/// kaydetmelidir (atomikliğin sırrı budur).
/// </summary>
public class OutboxOrderEventPublisher(OrderDbContext dbContext) : IOutboxOrderEventPublisher
{
    public async Task EnqueueOrderCreatedAsync(
        Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var orderCreatedEvent = new OrderCreatedEvent(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: orderId,
            CustomerId: customerId,
            TotalAmount: totalAmount);

        var processPaymentCommand = new ProcessPaymentCommand(
            CommandId: Guid.NewGuid(),
            OrderId: orderId,
            CustomerId: customerId,
            Amount: totalAmount);

        await dbContext.OutboxMessages.AddRangeAsync(
            [
                CreateOutboxRow(orderCreatedEvent, KafkaTopics.OrderCreated, orderCreatedEvent.PartitionKey),
                CreateOutboxRow(processPaymentCommand, KafkaTopics.ProcessPaymentCommand, processPaymentCommand.PartitionKey)
            ],
            cancellationToken);
    }

    private static OutboxMessage CreateOutboxRow<TMessage>(TMessage message, string topic, string partitionKey)
        where TMessage : class
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = typeof(TMessage).AssemblyQualifiedName!,
            Topic = topic,
            PartitionKey = partitionKey,
            Content = JsonSerializer.Serialize(message)
        };
    }
}
