using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 3 - Dead Letter Queue. Bir consumer, OrderCreatedEvent'i TÜM retry
/// denemelerinden (bkz. UseMessageRetry) sonra hâlâ işleyemezse, orijinal
/// mesaj + hata bilgisiyle birlikte bu tipte bir mesaj "order-created-dlq"
/// topic'ine yazılır. Bu sayede:
///   - Mesaj KAYBOLMAZ (Kafka'da kalıcı olarak durur, incelenebilir).
///   - Hangi consumer'ın (ConsumerName) neden (FailureReason) başarısız
///     olduğu bilgisi korunur.
///   - İleride bu topic dinlenip mesajlar elle/otomatik yeniden işlenebilir
///     ("replay") veya arşivlenebilir.
/// </summary>
public record OrderCreatedDeadLetterMessage(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    string FailureReason,
    string ConsumerName) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
