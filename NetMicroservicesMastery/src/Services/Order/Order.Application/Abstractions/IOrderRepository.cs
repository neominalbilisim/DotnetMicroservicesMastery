using Order.Domain.Entities;

namespace Order.Application.Abstractions;

/// <summary>
/// Modül 4 - Clean Architecture ilkesi: Repository ARAYÜZÜ Application
/// katmanında tanımlanır (Application, "ne" istediğini bilir); somut EF Core
/// implementasyonu ("nasıl" yapıldığı) Infrastructure katmanındadır
/// (OrderRepository, bkz. Order.Infrastructure/Repositories/). Bu sayede
/// Application katmanı EF Core'a DOĞRUDAN bağımlı değildir.
/// </summary>
public interface IOrderRepository
{
    Task<OrderAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(OrderAggregate entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
