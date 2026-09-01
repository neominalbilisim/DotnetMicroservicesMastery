namespace BuildingBlocks.Messaging.Contracts;

/// <summary>
/// Modül 3 - Kafka topic isimlerinin TEK merkezi kaynağı. Producer (Order.Api)
/// ve consumer'lar (Payment.Api, Inventory.Api) aynı sabitleri kullanarak,
/// topic ismi yazım hatası riskini ve dağınık string literal'leri önler.
/// </summary>
public static class KafkaTopics
{
    public const string OrderCreated = "order-created";

    /// <summary>
    /// Modül 3 - Command topic'i. Sadece Payment.Api dinler (Inventory.Api
    /// DİNLEMEZ) — Event'ten (order-created, her ikisi de dinler) farkı budur.
    /// </summary>
    public const string ProcessPaymentCommand = "process-payment-command";

    /// <summary>
    /// Modül 3 - Dead Letter Queue. Kafka'da RabbitMQ'daki gibi broker
    /// seviyesinde native bir DLQ yoktur — bu yüzden "tüm retry denemeleri
    /// tükendikten sonra başarısız olan mesaj" ayrı, sıradan bir Kafka
    /// topic'ine (bu) PRODUCE edilerek dead-letter deseni elle uygulanır.
    /// </summary>
    public const string OrderCreatedDeadLetter = "order-created-dlq";

    // ==================== Modül 4 - Saga Pattern (Choreography) ====================
    // Merkezi bir orkestratör YOKTUR — her servis bir öncekinin event'ini
    // dinler, kendi işini yapar ve kendi event'ini yayınlar. Order.Api hem
    // başlatıcı hem de "saga durumu"nu (OrderAggregate.Status) tutan taraftır.

    /// <summary>Saga'yı başlatır — Order.Api yayınlar, Inventory.Api dinler.</summary>
    public const string OrderSagaStarted = "order-saga-started";

    /// <summary>Stok rezervasyonu BAŞARILI — Inventory.Api yayınlar, Payment.Api dinler.</summary>
    public const string InventoryReserved = "inventory-reserved";

    /// <summary>Stok rezervasyonu BAŞARISIZ — Inventory.Api yayınlar, Order.Api dinler (compensation GEREKMEZ, henüz hiçbir şey rezerve edilmedi).</summary>
    public const string InventoryReservationFailed = "inventory-reservation-failed";

    /// <summary>Ödeme BAŞARILI — Payment.Api yayınlar, Order.Api dinler (saga TAMAMLANDI).</summary>
    public const string PaymentCompleted = "payment-completed";

    /// <summary>Ödeme BAŞARISIZ — Payment.Api yayınlar, Order.Api dinler (COMPENSATION tetiklenir).</summary>
    public const string PaymentFailed = "payment-failed";

    /// <summary>COMPENSATING COMMAND — Order.Api gönderir (Send), SADECE Inventory.Api dinler; rezerve edilen stoğu geri bırakır.</summary>
    public const string ReleaseInventoryCommand = "release-inventory-command";
}
