namespace Payment.Domain.Entities;

/// <summary>
/// Payment bounded context'inin kök agregatı (aggregate root).
/// TODO: Modül 4 (CQRS/Saga) ve Modül 5 (Idempotency) çalışmalarında
/// iş kurallarını ve durum (state) geçişlerini burada modelleyin.
/// </summary>
public class PaymentAggregate
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    // TODO: Payment'e özgü alanlar ve iş kuralları buraya eklenecek.
}
