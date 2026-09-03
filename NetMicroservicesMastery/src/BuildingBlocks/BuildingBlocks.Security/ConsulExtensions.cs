using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace BuildingBlocks.Security;

/// <summary>
/// Modül 2 - "Service Discovery: Consul".
/// Bir servisin ayağa kalktığında kendini Consul'a kaydetmesini (register) ve
/// health check endpoint'ini bildirmesini sağlayan ortak extension.
///
///   Servis A (Kendini kaydeder) -> Consul (Registry + Health Check) -> Servis B
///
/// appsettings.json'daki "Consul" bölümü kullanılır:
///   "Consul": {
///     "Address": "http://consul:8500",
///     "ServiceName": "order-service",
///     "ServiceAddress": "order-api",   // Docker'da container adı, lokalde host.docker.internal
///     "ServicePort": 8080
///   }
/// </summary>
public static class ConsulExtensions
{
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services, IConfiguration configuration, string defaultServiceName)
    {
        var consulAddress = configuration["Consul:Address"];
        if (string.IsNullOrWhiteSpace(consulAddress))
        {
            Console.Error.WriteLine("[Consul] Consul:Address tanımlı değil; servis kaydı atlanıyor.");
            return services;
        }

        var serviceName = configuration["Consul:ServiceName"] ?? defaultServiceName;
        var serviceAddress = configuration["Consul:ServiceAddress"] ?? "localhost";
        var servicePort = int.TryParse(configuration["Consul:ServicePort"], out var port) ? port : 8080;
        // Her instance'ın Consul'da tekil bir ID'si olmalı — çoklu instance (Modül 5)
        // senaryolarında aynı serviceName ile birden fazla kayıt aynı anda var olabilsin.
        var serviceId = $"{serviceName}-{serviceAddress}-{servicePort}";
        var healthCheckUrl = $"http://{serviceAddress}:{servicePort}/health/ready";

        services.AddSingleton<IConsulClient>(_ => new ConsulClient(cfg =>
        {
            cfg.Address = new Uri(consulAddress);
        }));

    services.AddHostedService(sp => new ConsulRegistrationHostedService(
        consulClient: sp.GetRequiredService<IConsulClient>(),
        serviceId: serviceId,
        serviceName: serviceName,
        serviceAddress: serviceAddress,
        servicePort: servicePort,
        healthCheckUrl: healthCheckUrl,
        logger: sp.GetRequiredService<ILogger<ConsulRegistrationHostedService>>(),
        appLifetime: sp.GetRequiredService<IHostApplicationLifetime>()
    ));

    return services;
    }
}
