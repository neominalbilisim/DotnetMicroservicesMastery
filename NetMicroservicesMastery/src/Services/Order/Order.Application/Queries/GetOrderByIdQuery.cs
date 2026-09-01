using MediatR;
using Order.Application.Abstractions;

namespace Order.Application.Queries;

/// <summary>
/// Modül 4 - CQRS Query (okuma tarafı). Yazma tarafından (CreateOrderCommand)
/// ayrı, sade bir okuma modeli (OrderDto) döner — DomainException fırlatan
/// zengin agregat yerine, dışarıya sadece gösterilecek alanlar sızdırılır.
/// </summary>
public record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto?>;

/// <summary>Okuma tarafına özel, sade veri modeli (DTO).</summary>
public record OrderDto(Guid OrderId, string CustomerId, decimal TotalAmount, string Status, DateTime CreatedAtUtc);

public class GetOrderByIdQueryHandler(IOrderRepository repository) : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        return order is null
            ? null
            : new OrderDto(order.Id, order.CustomerId, order.TotalAmount, order.Status.ToString(), order.CreatedAtUtc);
    }
}
