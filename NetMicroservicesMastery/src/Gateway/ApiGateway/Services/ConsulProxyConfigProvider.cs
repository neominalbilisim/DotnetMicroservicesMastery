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

      // Consul'dan tüm hizmetleri al
      var services = consulClient.Agent.Services().Result;
      foreach (var service in services.Response)
      {
        var serviceId = service.Value.ID;
        var serviceName = service.Value.Service;
        var serviceAddress = service.Value.Address;
        var servicePort = service.Value.Port;

        // YARP yapılandırmasına ekle

        // EĞER GELEN SERVİS GATEWAY'İN KENDİSİYSE, BU ADIMI ATLA
        if (string.Equals(serviceName, "gateway-service", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }


        routes.Add(new Yarp.ReverseProxy.Configuration.RouteConfig
        {
          RouteId = serviceId,
          ClusterId = serviceName,
          Match = new RouteMatch
          {
            Path = $"/{serviceName}/{{**catch-all}}"
          },

          // PathRemovePrefix transform'u burada tanımlanıyor:
          Transforms = new List<IReadOnlyDictionary<string, string>>
                    {
                        new Dictionary<string, string>
                        {
                            { "PathRemovePrefix", $"/{serviceName}" }
                        }
                    },
          AuthorizationPolicy = $"{serviceName}"
        });

        var destinationAddress = $"http://{serviceAddress}:{servicePort}"; // Local http://localhost:5001

        // var destinationAddress = $"http://{serviceName}:{servicePort}"; // Docker http://api1:5001


        clusters.Add(new ClusterConfig
        {
          ClusterId = serviceName,
          Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>

                {
                    { serviceId, new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = destinationAddress } }
                }
        });


      }

      _currentConfig =  new ConsulProxyConfig(routes, clusters);

      return _currentConfig;
    }

    // Hosted Service'in 15 saniyede bir çağıracağı metod
    public void Reload()
    {
      _currentConfig?.SignalChange();
    }
  }

}
