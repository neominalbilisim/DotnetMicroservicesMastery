namespace Payment.Domain.Events;

/// <summary>
/// Modül 3/4 - Domain event'ler; Application katmanında yakalanıp
/// BuildingBlocks.Messaging.Contracts.IIntegrationEvent'e map edilerek
/// Outbox üzerinden Publish edilecektir.
/// TODO: Payment'e özgü domain event'leri (örn. "PaymentCreatedDomainEvent") tanımlayın.
/// </summary>
public record PaymentPlaceholderDomainEvent(Guid AggregateId);
