namespace Order.Domain.Events;

/// <summary>
/// Modül 3/4 - Domain event'ler; Application katmanında yakalanıp
/// BuildingBlocks.Messaging.Contracts.IIntegrationEvent'e map edilerek
/// Outbox üzerinden Publish edilecektir.
/// TODO: Order'e özgü domain event'leri (örn. "OrderCreatedDomainEvent") tanımlayın.
/// </summary>
public record OrderPlaceholderDomainEvent(Guid AggregateId);
