using Microsoft.EntityFrameworkCore;
using BuildingBlocks.Messaging.Idempotency;
using Inventory.Infrastructure.Persistence;

namespace Inventory.Infrastructure.Idempotency;

/// <summary>Modül 5 - IIdempotencyStore'un Inventory.Api'ye özel EF Core implementasyonu.</summary>
public class InventoryIdempotencyStore(InventoryDbContext dbContext) : IIdempotencyStore
{
    public async Task<bool> HasBeenProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
        => await dbContext.ProcessedMessages.AnyAsync(x => x.MessageId == messageId, cancellationToken);

    public async Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default)
    {
        dbContext.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            ProcessedOnUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
