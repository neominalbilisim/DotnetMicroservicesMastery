using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 4 - Saga Pattern. Inventory.Api, stok rezervasyonu BAŞARILI
/// olduğunda yayınlar. Payment.Api bunu dinleyip ödeme almaya başlar.
/// </summary>
public record InventoryReservedEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
