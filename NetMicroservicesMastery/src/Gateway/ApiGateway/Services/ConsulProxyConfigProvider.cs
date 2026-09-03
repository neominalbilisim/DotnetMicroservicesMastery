using Consul;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Services
{

  public class ConsulProxyConfig : IProxyConfig
  {
    private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> Routes { get; }

    public IReadOnlyList<ClusterConfig> Clusters { get; }

    public IChangeToken ChangeToken { get; }

    public ConsulProxyConfig(IReadOnlyList<Yarp.ReverseProxy.Configuration.RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
      Routes = routes;
      Clusters = clusters;
      ChangeToken = new CancellationChangeToken(cancellationTokenSource.Token);
    }

    // Hosted Service bu metodu çağırarak YARP'a yenilenme emri verecek
    internal void SignalChange()
    {
      cancellationTokenSource.Cancel();
    }
  }
  public class ConsulProxyConfigProvider : IProxyConfigProvider
  {

    private readonly IConsulClient consulClient;
    private ConsulProxyConfig _currentConfig;

    public ConsulProxyConfigProvider(IConsulClient consulClient)
    {
      this.consulClient = consulClient;
    }

    public IProxyConfig GetConfig()
    {
      var routes = new List<Yarp.ReverseProxy.Configuration.RouteConfig>();
      var clusters = new List<Yarp.ReverseProxy.Configuration.ClusterConfig>();

      var services = consulClient.Agent.Services().Result;

      // Bir servisin birden fazla instance olabilir. Bu durumda servis ismlerini grouplayarak cluster'ı tek bir servis isiminden birden fazla destination verecek şekilde çıkarmalyız. 
      var serviceGroups = services.Response.Values.GroupBy(s => s.Service, StringComparer.OrdinalIgnoreCase);

      foreach (var group in serviceGroups)
      {
        var serviceName = group.Key;

       

        // 1. Rota Ekleme (Her servis adı için SADECE 1 KERE çalışır)
        routes.Add(new Yarp.ReverseProxy.Configuration.RouteConfig
        {
          RouteId = $"{serviceName}-route",
          ClusterId = serviceName, // YARP bu ID'yi cluster ile eşleştirir
          Match = new RouteMatch
          {
            Path = $"/{serviceName}/{{**catch-all}}"
          },
          Transforms = new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "PathRemovePrefix", $"/{serviceName}" }
                }
            }
        });

        // 2. Destinasyonları (Instance'ları) Toplama
        var destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in group)
        {
          destinations.Add(instance.ID, new Yarp.ReverseProxy.Configuration.DestinationConfig
          {
            Address = $"http://{instance.Address}:{instance.Port}"
          });
        }

        // 3. Cluster Ekleme (Her servis adı için SADECE 1 KERE çalışır)
        clusters.Add(new ClusterConfig
        {
          ClusterId = serviceName, // Benzersiz olmak zorundadır
          LoadBalancingPolicy = "RoundRobin",
          Destinations = destinations
        });
      }

      return new ConsulProxyConfig(routes, clusters);
    }

    // Hosted Service'in 15 saniyede bir çağıracağı metod
    public void Reload()
    {
      _currentConfig?.SignalChange();
    }
  }

}
