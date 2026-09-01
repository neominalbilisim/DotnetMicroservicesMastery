namespace Inventory.Domain.Events;

/// <summary>
/// Modül 3/4 - Domain event'ler; Application katmanında yakalanıp
/// BuildingBlocks.Messaging.Contracts.IIntegrationEvent'e map edilerek
/// Outbox üzerinden Publish edilecektir.
/// TODO: Inventory'e özgü domain event'leri (örn. "InventoryCreatedDomainEvent") tanımlayın.
/// </summary>
public record InventoryPlaceholderDomainEvent(Guid AggregateId);
