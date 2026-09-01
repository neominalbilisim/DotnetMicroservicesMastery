using MediatR;

namespace Payment.Application.Queries;

/// <summary>
/// Modül 4 - CQRS Query örneği (okuma tarafı).
/// TODO: Okuma tarafını yazma tarafından ayrı, performansa optimize bir modelle
/// (örn. Dapper ile düz SQL veya ayrı bir read-store) implemente edin.
/// </summary>
public record GetPaymentByIdQuery(Guid Id) : IRequest<object?>;

public class GetPaymentByIdQueryHandler : IRequestHandler<GetPaymentByIdQuery, object?>
{
    public Task<object?> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Modül 4 çalışmasında doldurulacak.");
    }
}
