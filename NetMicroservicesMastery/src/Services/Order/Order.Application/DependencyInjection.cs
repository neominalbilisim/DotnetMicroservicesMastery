using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Order.Application.Behaviors;
using System.Reflection;

namespace Order.Application;

/// <summary>
/// Modül 4 - "CQRS (Command Query Responsibility Segregation)".
/// MediatR ile in-memory Command/Query yönlendirmesinin (dispatch) kaydı +
/// FluentValidation validator'ları + ValidationBehavior pipeline'ı.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOrderApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        // Bu assembly'deki tüm AbstractValidator<T> sınıflarını otomatik kaydeder
        // (örn. CreateOrderCommandValidator).
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // Her Command/Query, handler'a ulaşmadan önce bu pipeline'dan geçer.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
