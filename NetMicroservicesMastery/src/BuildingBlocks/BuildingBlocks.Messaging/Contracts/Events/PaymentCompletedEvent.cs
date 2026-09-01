using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 4 - Saga Pattern. Payment.Api, ödeme BAŞARILI olduğunda yayınlar.
/// Order.Api bunu dinleyip siparişi onaylar — SAGA BAŞARIYLA TAMAMLANIR.
/// </summary>
public record PaymentCompletedEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
