using MediatR;
using Order.Application.Abstractions;
using Order.Domain.Entities;

namespace Order.Application.Commands;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION) — dedike Command. `StartOrderSagaCommand`
/// (Choreography, /submit-order-saga) ile KARIŞTIRILMAMALIDIR. Bu, ayrı bir
/// servis olan Saga.Api'ye RabbitMQ üzerinden saga başlatma talebi gönderir.
/// </summary>
public record StartOrderSagaOrchestratorCommand(string CustomerId, decimal TotalAmount, Guid? OrderId = null) : IRequest<CreateOrderResult>;

public class StartOrderSagaOrchestratorCommandHandler(
    IOrderRepository repository,
    IOrderSagaOrchestratorClient sagaClient) : IRequestHandler<StartOrderSagaOrchestratorCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(StartOrderSagaOrchestratorCommand request, CancellationToken cancellationToken)
    {
        var orderId = request.OrderId ?? Guid.NewGuid();

        var order = OrderAggregate.Create(orderId, request.CustomerId, request.TotalAmount);
        order.MarkAwaitingInventory();

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        // Saga.Api'ye saga başlatma talebi gönderilir (RabbitMQ, Send).
        await sagaClient.StartSagaAsync(orderId, request.CustomerId, request.TotalAmount, cancellationToken);

        return new CreateOrderResult(orderId);
    }
}
