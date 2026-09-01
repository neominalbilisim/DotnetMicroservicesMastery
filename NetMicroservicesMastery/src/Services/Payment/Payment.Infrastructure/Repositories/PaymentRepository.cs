using Payment.Domain.Entities;
using Payment.Infrastructure.Persistence;

namespace Payment.Infrastructure.Repositories;

public interface IPaymentRepository
{
    Task<PaymentAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PaymentAggregate entity, CancellationToken ct = default);
}

public class PaymentRepository(PaymentDbContext dbContext) : IPaymentRepository
{
    public async Task<PaymentAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Payments.FindAsync([id], ct);

    public async Task AddAsync(PaymentAggregate entity, CancellationToken ct = default)
        => await dbContext.Payments.AddAsync(entity, ct);
}
