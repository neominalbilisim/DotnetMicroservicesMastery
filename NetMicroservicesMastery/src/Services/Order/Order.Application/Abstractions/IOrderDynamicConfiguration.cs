namespace Order.Application.Abstractions;

/// <summary>
/// Modül 5 - "Consul KV: Merkezi/Dinamik Konfigürasyon".
/// appsettings.json'daki değerler SADECE başlangıçta okunur; servis yeniden
/// başlatılmadan DEĞİŞTİRİLEMEZ. Bu arayüz ise Consul KV'den periyodik
/// olarak okunan, SERVİS YENİDEN BAŞLATILMADAN canlı olarak değiştirilebilen
/// bir değeri temsil eder. Gerçek Consul KV okuma mantığı Infrastructure
/// katmanındadır (OrderDynamicConfiguration + ConsulKvConfigurationWatcher).
/// </summary>
public interface IOrderDynamicConfiguration
{
    /// <summary>
    /// Bir siparişin alabileceği azami tutar. Consul KV'deki
    /// "config/order-service/max-order-amount" anahtarından okunur;
    /// anahtar Consul'da değiştirildiğinde birkaç saniye içinde (bkz.
    /// ConsulKvConfigurationWatcher poll aralığı) burada da yansır.
    /// </summary>
    decimal MaxOrderAmount { get; }
}
