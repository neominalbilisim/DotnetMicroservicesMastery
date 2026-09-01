using FluentValidation;

namespace Order.Application.Commands;

/// <summary>
/// Modül 4 - CreateOrderCommand için FluentValidation kuralları. Bu
/// validasyon, ValidationBehavior (MediatR pipeline behavior) tarafından
/// handler ÇALIŞMADAN ÖNCE otomatik olarak uygulanır — handler kodunun
/// kendisi validasyon mantığı içermez (Single Responsibility).
/// NOT: Bu, OrderAggregate.Create() içindeki DOMAIN kurallarından farklıdır
/// — burada "girdi biçimi" (örn. boş string) kontrol edilir, orada "iş
/// kuralı" (örn. tutar > 0) korunur. İkisi de sonuçta DomainException
/// olarak GlobalExceptionHandler'a düşer (Modül 1 — 400 Bad Request).
/// </summary>
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Müşteri kimliği (customerId) zorunludur.");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("Sipariş tutarı (totalAmount) sıfırdan büyük olmalıdır.");
    }
}
