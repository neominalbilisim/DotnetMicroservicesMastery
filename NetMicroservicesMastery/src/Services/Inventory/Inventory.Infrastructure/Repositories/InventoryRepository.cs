using Inventory.Domain.Entities;
using Inventory.Infrastructure.Persistence;

namespace Inventory.Infrastructure.Repositories;

public interface IInventoryRepository
{
    Task<InventoryAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(InventoryAggregate entity, CancellationToken ct = default);
}

public class InventoryRepository(InventoryDbContext dbContext) : IInventoryRepository
{
    public async Task<InventoryAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Inventorys.FindAsync([id], ct);

    public async Task AddAsync(InventoryAggregate entity, CancellationToken ct = default)
        => await dbContext.Inventorys.AddAsync(entity, ct);
}
