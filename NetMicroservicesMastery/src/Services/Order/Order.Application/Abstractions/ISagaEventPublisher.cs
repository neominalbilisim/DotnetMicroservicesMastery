namespace Order.Application.Abstractions;

/// <summary>
/// Modül 4 - Saga Pattern (Choreography) için Order.Api'nin ihtiyaç duyduğu
/// iki mesajlaşma eylemi: saga'yı BAŞLATMA (Event/Publish) ve başarısızlık
/// durumunda telafi TETİKLEME (Command/Send). Gerçek Kafka implementasyonu
/// Infrastructure katmanındadır (KafkaSagaEventPublisher).
/// </summary>
public interface ISagaEventPublisher
{
    /// <summary>Saga'yı başlatır — Inventory.Api bunu dinler.</summary>
    Task PublishOrderSagaStartedAsync(Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default);

    /// <summary>
    /// COMPENSATING ADIM: Ödeme başarısız olduğunda, az önce rezerve edilen
    /// stoğun geri bırakılması için Inventory.Api'ye gönderilir (Send).
    /// </summary>
    Task SendReleaseInventoryCommandAsync(Guid orderId, CancellationToken cancellationToken = default);
}
