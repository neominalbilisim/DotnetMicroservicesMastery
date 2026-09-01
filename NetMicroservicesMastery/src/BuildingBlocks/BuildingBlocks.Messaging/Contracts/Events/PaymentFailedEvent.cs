using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 4 - Saga Pattern. Payment.Api, ödeme BAŞARISIZ olduğunda yayınlar.
/// Order.Api bunu dinleyip siparişi iptal eder VE COMPENSATION'ı
/// (ReleaseInventoryCommand) tetikler — çünkü bu noktada stok ZATEN
/// rezerve edilmişti, geri bırakılması gerekir.
/// </summary>
public record PaymentFailedEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId,
    string Reason) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
