namespace ApiGateway.Services
{
  public class ConsulConfigRefreshService : BackgroundService
  {
    private readonly ConsulProxyConfigProvider _configProvider;

    public ConsulConfigRefreshService(ConsulProxyConfigProvider configProvider)
    {
      _configProvider = configProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // 15 saniyede bir tetiklenecek zamanlayıcı
      using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        // YARP konfigürasyonunu yenilemesi için sinyal gönder
        _configProvider.Reload();
      }
    }
  }
}
