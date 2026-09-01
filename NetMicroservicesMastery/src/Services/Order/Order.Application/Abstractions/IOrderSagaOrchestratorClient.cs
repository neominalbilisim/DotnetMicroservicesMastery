namespace Order.Application.Abstractions;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION). Order.Api'nin, ayrı bir servis
/// olan Saga.Api'ye saga başlatma talebini iletmek için kullandığı
/// soyutlama. Gerçek implementasyon (RabbitMQ üzerinden Send) Infrastructure
/// katmanındadır (RabbitMqOrderSagaOrchestratorClient).
/// </summary>
public interface IOrderSagaOrchestratorClient
{
    Task StartSagaAsync(Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default);
}
