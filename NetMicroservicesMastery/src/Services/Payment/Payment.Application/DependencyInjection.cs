using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Payment.Application;

/// <summary>
/// Modül 4 - "CQRS (Command Query Responsibility Segregation)".
/// MediatR ile in-memory Command/Query yönlendirmesinin (dispatch) kaydı.
/// TODO: Command/Query handler'lar eklendikçe burada otomatik olarak taranacaktır.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        // TODO: services.AddValidatorsFromAssembly(...) — FluentValidation kayıtları.
        // TODO (Modül 4): ValidationBehavior, LoggingBehavior gibi MediatR pipeline behavior'larını ekle.
        return services;
    }
}
