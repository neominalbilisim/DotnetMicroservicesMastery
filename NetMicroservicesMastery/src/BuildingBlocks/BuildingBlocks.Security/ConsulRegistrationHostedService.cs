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
    ILogger<ConsulRegistrationHostedService> logger,
    IHostApplicationLifetime appLifetime) : IHostedService // Kesterel sunucu tam olarak ayağa kalkıo htp isteklerini kabul etmeye hazırken consule kendini register eder. Consul kayıt işlemini doğru zamanda yapar.
{
  private AgentServiceRegistration _registration;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _registration = new AgentServiceRegistration
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
        DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1)
      }
    };

    // Kestrel tamamen ayağa kalkıp portları dinlemeye başladığında tetiklenir
    appLifetime.ApplicationStarted.Register(() =>
    {
      try
      {
        consulClient.Agent.ServiceRegister(_registration).Wait();
        logger.LogInformation(
            "[Consul] '{ServiceName}' ({ServiceId}) {Address}:{Port} adresiyle kaydedildi. Health check: {HealthCheckUrl}",
            serviceName, serviceId, serviceAddress, servicePort, healthCheckUrl);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "[Consul] '{ServiceName}' Consul'a kaydedilemedi.", serviceName);
      }
    });

    return Task.CompletedTask;
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