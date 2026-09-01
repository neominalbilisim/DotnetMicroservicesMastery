using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 4 - Saga Pattern (Choreography). Order.Api tarafından saga'yı
/// başlatmak için yayınlanır. Inventory.Api bunu dinleyip stok
/// rezervasyonuna başlar.
/// </summary>
public record OrderSagaStartedEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
