using FluentValidation;

namespace Order.Application.Commands;

/// <summary>Modül 4 - StartOrderSagaCommand için validasyon kuralları.</summary>
public class StartOrderSagaCommandValidator : AbstractValidator<StartOrderSagaCommand>
{
    public StartOrderSagaCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Müşteri kimliği (customerId) zorunludur.");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("Sipariş tutarı (totalAmount) sıfırdan büyük olmalıdır.");
    }
}
