using BuildingBlocks.Common.Exceptions;
using FluentValidation;
using MediatR;

namespace Order.Application.Behaviors;

/// <summary>
/// Modül 4 - MediatR Pipeline Behavior. Her Command/Query, handler'ına
/// ulaşmadan ÖNCE bu behavior'dan geçer: ilgili tipte kayıtlı bir
/// FluentValidation validator'ı varsa çalıştırılır; hata varsa handler'a
/// hiç gidilmeden DomainException fırlatılır (bkz. Modül 1
/// GlobalExceptionHandler — 400 Bad Request'e otomatik eşlenir).
///
///   İstek -> ValidationBehavior (kurallar ihlal edildiyse burada durur)
///         -> Handler (sadece geçerli istekler buraya ulaşır)
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
        {
            var errorMessage = string.Join(" | ", failures.Select(f => f.ErrorMessage));
            throw new DomainException(errorMessage);
        }

        return await next();
    }
}
