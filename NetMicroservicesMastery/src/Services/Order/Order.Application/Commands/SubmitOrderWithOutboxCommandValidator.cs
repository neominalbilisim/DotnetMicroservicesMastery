using FluentValidation;

namespace Order.Application.Commands;

/// <summary>Modül 4 - SubmitOrderWithOutboxCommand için validasyon kuralları (CreateOrderCommandValidator ile aynı).</summary>
public class SubmitOrderWithOutboxCommandValidator : AbstractValidator<SubmitOrderWithOutboxCommand>
{
    public SubmitOrderWithOutboxCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Müşteri kimliği (customerId) zorunludur.");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("Sipariş tutarı (totalAmount) sıfırdan büyük olmalıdır.");
    }
}
