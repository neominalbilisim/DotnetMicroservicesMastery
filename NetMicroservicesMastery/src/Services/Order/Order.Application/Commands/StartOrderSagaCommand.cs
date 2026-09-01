using MediatR;
using Order.Application.Abstractions;
using Order.Domain.Entities;

namespace Order.Application.Commands;

/// <summary>
/// Modül 4 - Saga Pattern'i (Choreography) BAŞLATAN, dedike Command.
/// `CreateOrderCommand` (/submit-order) ve `SubmitOrderWithOutboxCommand`
/// (/submit-order-outbox) ile KARIŞTIRILMAMALIDIR — bu üçü tamamen izole,
/// kendi topic'lerine ve kendi akışlarına sahiptir.
/// </summary>
public record StartOrderSagaCommand(string CustomerId, decimal TotalAmount, Guid? OrderId = null) : IRequest<CreateOrderResult>;

public class StartOrderSagaCommandHandler(
    IOrderRepository repository,
    ISagaEventPublisher sagaPublisher) : IRequestHandler<StartOrderSagaCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(StartOrderSagaCommand request, CancellationToken cancellationToken)
    {
        var orderId = request.OrderId ?? Guid.NewGuid();

        var order = OrderAggregate.Create(orderId, request.CustomerId, request.TotalAmount);
        order.MarkAwaitingInventory();

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        // Saga'yı başlatan event — Inventory.Api dinleyip stok rezervasyonuna başlar.
        await sagaPublisher.PublishOrderSagaStartedAsync(orderId, request.CustomerId, request.TotalAmount, cancellationToken);

        return new CreateOrderResult(orderId);
    }
}
