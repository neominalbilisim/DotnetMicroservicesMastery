using BuildingBlocks.Messaging.Contracts.Saga;
using MassTransit;
using Order.Application.Abstractions;

namespace Order.Infrastructure.Messaging;

/// <summary>
/// Modül 4 - IOrderSagaOrchestratorClient'in RabbitMQ implementasyonu.
/// BeginOrderSagaCommand'ı, Saga.Api'nin dinlediği TEK kuyruğa
/// (SagaQueues.OrderSagaOrchestrator) doğrudan SEND eder.
/// </summary>
public class RabbitMqOrderSagaOrchestratorClient(ISendEndpointProvider sendEndpointProvider) : IOrderSagaOrchestratorClient
{
    public async Task StartSagaAsync(
        Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{SagaQueues.OrderSagaOrchestrator}"));
        await endpoint.Send(new BeginOrderSagaCommand(orderId, customerId, totalAmount), cancellationToken);
    }
}
