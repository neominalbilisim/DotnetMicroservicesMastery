using MediatR;

namespace Inventory.Application.Queries;

/// <summary>
/// Modül 4 - CQRS Query örneği (okuma tarafı).
/// TODO: Okuma tarafını yazma tarafından ayrı, performansa optimize bir modelle
/// (örn. Dapper ile düz SQL veya ayrı bir read-store) implemente edin.
/// </summary>
public record GetInventoryByIdQuery(Guid Id) : IRequest<object?>;

public class GetInventoryByIdQueryHandler : IRequestHandler<GetInventoryByIdQuery, object?>
{
    public Task<object?> Handle(GetInventoryByIdQuery request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Modül 4 çalışmasında doldurulacak.");
    }
}
