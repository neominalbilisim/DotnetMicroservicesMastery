using MediatR;

namespace Payment.Application.Commands;

/// <summary>
/// Modül 4 - CQRS Command örneği (yazma tarafı).
/// TODO: Payment'e özgü alanları ve iş kuralı doğrulamalarını ekleyin.
/// </summary>
public record CreatePaymentCommand : IRequest<Guid>;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Guid>
{
    public Task<Guid> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        // TODO (Modül 4): Aggregate oluştur, repository'e kaydet,
        // Outbox'a domain event yaz (bkz. BuildingBlocks.Messaging.Outbox).
        throw new NotImplementedException("Modül 4 çalışmasında doldurulacak.");
    }
}
