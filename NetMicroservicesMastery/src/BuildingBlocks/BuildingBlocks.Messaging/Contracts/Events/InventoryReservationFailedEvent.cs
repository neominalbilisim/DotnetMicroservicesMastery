using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 4 - Saga Pattern. Inventory.Api, stok rezervasyonu BAŞARISIZ
/// olduğunda yayınlar. Order.Api bunu dinleyip siparişi iptal eder.
/// NOT: Bu durumda COMPENSATION GEREKMEZ — henüz hiçbir şey rezerve
/// edilmediği için geri alınacak bir şey yoktur.
/// </summary>
public record InventoryReservationFailedEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId,
    string Reason) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
