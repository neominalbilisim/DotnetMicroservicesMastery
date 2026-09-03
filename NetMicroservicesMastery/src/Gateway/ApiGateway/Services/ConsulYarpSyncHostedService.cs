using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;
// Consul paketiyle Yarp.ReverseProxy.Configuration arasındaki isim
// çakışmasını (RouteConfig/DestinationConfig) çözmek için açık alias.
using RouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;
using DestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;

namespace ApiGateway.Services;

/// <summary>
/// Modül 2 - "Service Discovery Entegrasyonu: YARP'ın hedef adresleri statik
/// olarak değil, Consul gibi bir service registry'den dinamik olarak alınması".
///
/// Periyodik olarak (10 sn) Consul'un Health.Service(...) API'sini sorgular;
/// Order/Payment/Inventory servislerinin O ANKİ SAĞLIKLI (passing health
/// check) instance'larını bulur ve YARP'ın InMemoryConfigProvider'ını
/// günceller:
///
///   İstemci -> YARP API Gateway -> Consul'dan Adres Sorgusu -> İlgili Mikroservis
///
/// Böylece appsettings.json'da hedef adres/port elle güncellenmesine gerek
/// kalmaz; yeni bir instance ayağa kalkınca otomatik trafiğe girer, bir
/// instance çökünce otomatik trafikten çıkar.
/// </summary>
public class ConsulYarpSyncHostedService(
    IConsulClient consulClient,
    InMemoryConfigProvider configProvider,
    ILogger<ConsulYarpSyncHostedService> logger) : BackgroundService
{
  // Bir Consul sorgusu boş dönerse (henüz sağlıklı instance yoksa), bir
  // önceki başarılı sorgudaki hedefleri korumak için kullanılır — böylece
  // geçici bir Consul aksaklığında YARP tüm hedefleri kaybetmez.
  private readonly Dictionary<string, ClusterConfig> _lastKnownClusters = new();

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      await SyncAsync(stoppingToken);
      try
      {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
      }
      catch (TaskCanceledException)
      {
        // Uygulama kapanıyor — normal.
      }
    }
  }

  private async Task SyncAsync(CancellationToken ct)
  {
    var routes = new List<RouteConfig>();

    foreach (var mapping in YarpRouteMap.All)
    {
      RouteConfig routeConfig = new RouteConfig
      {
        RouteId = mapping.RouteId,
        ClusterId = mapping.ClusterId,
        Match = new RouteMatch { Path = mapping.PathPattern }
      };

      routes.Add(routeConfig);

      var destinations = await GetHealthyDestinationsAsync(mapping.ConsulServiceName, ct);

      if (destinations.Count > 0)
      {
        _lastKnownClusters[mapping.ClusterId] = new ClusterConfig
        {
          ClusterId = mapping.ClusterId,
          // Birden fazla sağlıklı instance varsa (Modül 2 - Load
          // Balancing) aralarında RoundRobin (sırayla) dağıtım
          // yapılır. Belirtilmezse YARP varsayılanı PowerOfTwoChoices'tir
          // — RoundRobin, demo/test sırasında hangi instance'ın
          // seçildiğini öngörülebilir kılar.
          LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
          Destinations = destinations.ToDictionary(
                d => d.Key,
                d => new DestinationConfig { Address = d.Value })
        };

        logger.LogInformation(
            "[Consul->YARP] '{Service}' için {Count} sağlıklı instance bulundu: {Addresses}",
            mapping.ConsulServiceName, destinations.Count, string.Join(", ", destinations.Values));
      }
      else if (!_lastKnownClusters.ContainsKey(mapping.ClusterId))
      {
        logger.LogWarning(
            "[Consul->YARP] '{Service}' için hiç sağlıklı instance bulunamadı (henüz kayıt olmamış olabilir).",
            mapping.ConsulServiceName);
      }
      else
      {
        logger.LogWarning(
            "[Consul->YARP] '{Service}' için şu an sağlıklı instance yok; son bilinen hedefler korunuyor.",
            mapping.ConsulServiceName);
      }
    }

    if (_lastKnownClusters.Count > 0)
    {
      configProvider.Update(routes, _lastKnownClusters.Values.ToList());
    }
  }

  private async Task<Dictionary<string, string>> GetHealthyDestinationsAsync(string consulServiceName, CancellationToken ct)
  {
    var result = new Dictionary<string, string>();
    try
    {
      // passingOnly:true -> sadece health check'i "passing" (sağlıklı)
      // olan instance'lar döner; "critical" olanlar otomatik elenir.
      var queryResult = await consulClient.Health.Service(consulServiceName, tag: null, passingOnly: true, ct: ct);

      var index = 0;
      foreach (var entry in queryResult.Response)
      {
        var address = string.IsNullOrWhiteSpace(entry.Service.Address)
            ? entry.Node.Address
            : entry.Service.Address;
        var port = entry.Service.Port;
        result[$"destination-{index++}"] = $"http://{address}:{port}";
      }
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "[Consul->YARP] '{Service}' için Consul sorgulanamadı.", consulServiceName);
    }

    return result;
  }
}
