using MediatR;

namespace Inventory.Application.Commands;

/// <summary>
/// Modül 4 - CQRS Command örneği (yazma tarafı).
/// TODO: Inventory'e özgü alanları ve iş kuralı doğrulamalarını ekleyin.
/// </summary>
public record CreateInventoryCommand : IRequest<Guid>;

public class CreateInventoryCommandHandler : IRequestHandler<CreateInventoryCommand, Guid>
{
    public Task<Guid> Handle(CreateInventoryCommand request, CancellationToken cancellationToken)
    {
        // TODO (Modül 4): Aggregate oluştur, repository'e kaydet,
        // Outbox'a domain event yaz (bkz. BuildingBlocks.Messaging.Outbox).
        throw new NotImplementedException("Modül 4 çalışmasında doldurulacak.");
    }
}
