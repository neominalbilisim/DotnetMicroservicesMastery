using Order.Application.Abstractions;

namespace Order.Infrastructure.Configuration;

/// <summary>
/// Modül 5 - IOrderDynamicConfiguration'ın somut implementasyonu. Değeri
/// KENDİSİ Consul'dan okumaz — sadece thread-safe bir "son bilinen değer"
/// deposudur. Gerçek okuma/güncelleme ConsulKvConfigurationWatcher
/// (arka plan servisi) tarafından yapılır.
/// </summary>
public class OrderDynamicConfiguration : IOrderDynamicConfiguration
{
    private readonly object _lock = new();

    // Consul KV'de anahtar henüz yoksa veya Consul'a hiç ulaşılamazsa
    // kullanılacak, "pratikte sınırsız" sayılabilecek makul bir varsayılan.
    private decimal _maxOrderAmount = 1_000_000m;

    public decimal MaxOrderAmount
    {
        get { lock (_lock) { return _maxOrderAmount; } }
    }

    public void Update(decimal value)
    {
        lock (_lock) { _maxOrderAmount = value; }
    }
}
