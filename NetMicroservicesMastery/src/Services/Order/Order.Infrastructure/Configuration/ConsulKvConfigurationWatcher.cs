using System.Globalization;
using System.Text;
using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Order.Infrastructure.Configuration;

/// <summary>
/// Modül 5 - "Consul KV: Merkezi/Dinamik Konfigürasyon".
/// Her 10 saniyede bir Consul KV'deki "config/order-service/max-order-amount"
/// anahtarını okur ve OrderDynamicConfiguration'ı günceller. Bu sayede
/// azami sipariş tutarı, Order.Api'yi YENİDEN BAŞLATMADAN, sadece Consul
/// UI'dan (veya CLI ile) değiştirilebilir.
///
///   Consul KV (anahtar değişir) -> (≤10sn içinde) -> OrderDynamicConfiguration
///   güncellenir -> CreateOrderCommandHandler bir sonraki istekte YENİ
///   değeri kullanır.
///
/// NOT: Burada BASİT POLLING kullanıldı (her 10sn'de bir oku). Consul'un
/// "blocking query" (uzun polling / WaitIndex) özelliği anlık değişiklik
/// bildirimi için daha verimlidir, ama karmaşıklığı artırır — bu eğitim
/// projesinde anlaşılırlık için basit polling tercih edildi.
/// </summary>
public class ConsulKvConfigurationWatcher(
    IConsulClient consulClient,
    OrderDynamicConfiguration dynamicConfiguration,
    IConfiguration configuration,
    ILogger<ConsulKvConfigurationWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var key = configuration["ConsulKv:MaxOrderAmountKey"] ?? "config/order-service/max-order-amount";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(key, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ConsulKV] '{Key}' okunamadı — mevcut değer ({CurrentValue}) korunuyor.",
                    key, dynamicConfiguration.MaxOrderAmount);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Uygulama kapanıyor — normal.
            }
        }
    }

    private async Task RefreshAsync(string key, CancellationToken ct)
    {
        var result = await consulClient.KV.Get(key, ct);

        // Anahtar Consul'da HİÇ tanımlanmamışsa (henüz kimse ayarlamadıysa)
        // Response null gelir — bu bir hata değildir, sadece varsayılan
        // değer korunur.
        if (result?.Response?.Value is null)
        {
            return;
        }

        var rawValue = Encoding.UTF8.GetString(result.Response.Value);

        if (decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue))
        {
            var previousValue = dynamicConfiguration.MaxOrderAmount;
            if (previousValue != parsedValue)
            {
                dynamicConfiguration.Update(parsedValue);
                Console.WriteLine($"🔧 [ConsulKV] MaxOrderAmount değişti: {previousValue:N2} -> {parsedValue:N2}");
                logger.LogInformation("[ConsulKV] MaxOrderAmount güncellendi: {Previous} -> {New}", previousValue, parsedValue);
            }
        }
        else
        {
            logger.LogWarning("[ConsulKV] '{Key}' anahtarındaki değer ('{RawValue}') geçerli bir sayı değil, yok sayıldı.", key, rawValue);
        }
    }
}
