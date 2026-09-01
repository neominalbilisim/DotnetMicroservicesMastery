using Order.Application.Abstractions;
using Order.Domain.Entities;
using Order.Infrastructure.Persistence;

namespace Order.Infrastructure.Repositories;

/// <summary>
/// Modül 4 - IOrderRepository'nin (Order.Application.Abstractions) somut
/// EF Core implementasyonu. Arayüz Application'da tanımlıdır — bkz. o
/// dosyadaki Clean Architecture açıklaması.
/// </summary>
public class OrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    public async Task<OrderAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Orders.FindAsync([id], ct);

    public async Task AddAsync(OrderAggregate entity, CancellationToken ct = default)
        => await dbContext.Orders.AddAsync(entity, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await dbContext.SaveChangesAsync(ct);
}
