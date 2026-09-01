using System;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Security;

/// <summary>
/// Modül 2 - "Service Discovery: Consul".
/// Uygulama başladığında kendini Consul'a kaydeder (Agent.ServiceRegister),
/// bir health check tanımlar ve uygulama kapanırken kaydını siler
/// (Agent.ServiceDeregister). Bu sayede Consul, servis çökerse/yanıt vermezse
/// otomatik olarak onu kayıt listesinden çıkarır (bkz. Ön Hazırlık Dökümanı
/// Modül 2 - "Service Discovery: Consul").
/// </summary>
public class ConsulRegistrationHostedService(
    IConsulClient consulClient,
    string serviceId,
    string serviceName,
    string serviceAddress,
    int servicePort,
    string healthCheckUrl,
    ILogger<ConsulRegistrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var registration = new AgentServiceRegistration
        {
            ID = serviceId,
            Name = serviceName,
            Address = serviceAddress,
            Port = servicePort,
            Tags = ["net-microservices-mastery"],
            Check = new AgentServiceCheck
            {
                HTTP = healthCheckUrl,
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(5),
                // Health check 1 dakikadır "critical" durumdaysa, Consul kaydı
                // otomatik olarak tamamen siler (kalıcı hayalet kayıt kalmaz).
                DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1)
            }
        };

        try
        {
            await consulClient.Agent.ServiceRegister(registration, cancellationToken);
            logger.LogInformation(
                "[Consul] '{ServiceName}' ({ServiceId}) {Address}:{Port} adresiyle kaydedildi. Health check: {HealthCheckUrl}",
                serviceName, serviceId, serviceAddress, servicePort, healthCheckUrl);
        }
        catch (Exception ex)
        {
            // Consul'a ulaşılamaması uygulamanın başlamasını ENGELLEMEMELİDİR
            // (örn. Consul henüz ayakta değilse) — sadece uyarı loglanır.
            logger.LogWarning(ex, "[Consul] '{ServiceName}' Consul'a kaydedilemedi. Consul olmadan çalışmaya devam edilecek.", serviceName);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await consulClient.Agent.ServiceDeregister(serviceId, cancellationToken);
            logger.LogInformation("[Consul] '{ServiceName}' ({ServiceId}) kaydı silindi.", serviceName, serviceId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Consul] '{ServiceName}' Consul kaydı silinirken hata oluştu.", serviceName);
        }
    }
}
