namespace Order.Application.Abstractions;

/// <summary>
/// Modül 4 - Clean Architecture ilkesi: Application katmanı, MassTransit/Kafka
/// gibi somut mesajlaşma teknolojilerine DOĞRUDAN bağımlı olmaz — sadece bu
/// soyutlamaya bağımlıdır. Gerçek Kafka implementasyonu Infrastructure
/// katmanında (KafkaOrderEventPublisher, doğrudan Kafka'ya üretir) yapılır.
/// Outbox Pattern'i test etmek için AYRI bir soyutlama vardır —
/// bkz. IOutboxOrderEventPublisher.cs.
/// </summary>
public interface IOrderEventPublisher
{
    /// <summary>
    /// Bir sipariş oluşturulduğunda hem OrderCreatedEvent'i (Publish/Event)
    /// hem ProcessPaymentCommand'ı (Send/Command) DOĞRUDAN Kafka'ya yayınlar
    /// (bkz. Modül 3 Command/Event ayrımı).
    /// </summary>
    Task PublishOrderCreatedAsync(Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default);
}
