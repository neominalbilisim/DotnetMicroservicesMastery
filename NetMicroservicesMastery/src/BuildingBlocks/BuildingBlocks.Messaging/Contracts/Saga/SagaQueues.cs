namespace BuildingBlocks.Messaging.Contracts.Saga;

/// <summary>
/// Modül 4 - Saga Orchestration (RabbitMQ) kuyruk isimlerinin TEK merkezi
/// kaynağı. MassTransit'in varsayılan (consumer sınıf adına dayalı) otomatik
/// kuyruk isimlendirmesine GÜVENİLMEDİ — her iki taraf (gönderen ve dinleyen)
/// da AYNI sabitleri kullanarak kuyruk isimlerinin birebir eşleştiğinden emin olunur.
/// </summary>
public static class SagaQueues
{
    /// <summary>
    /// Saga.Api'nin state machine'inin dinlediği TEK kuyruk. Hem saga'yı
    /// BAŞLATAN komut (BeginOrderSagaCommand) hem de katılımcılardan gelen
    /// TÜM reply event'leri (InventoryReservation*, PaymentCharge*) buraya
    /// gelir — bir saga'nın ilgili tüm mesajları geleneksel olarak TEK bir
    /// endpoint'te toplanır.
    /// </summary>
    public const string OrderSagaOrchestrator = "order-saga-orchestrator";

    /// <summary>Inventory.Api'nin dinlediği — stok rezervasyon talebi.</summary>
    public const string ReserveInventory = "reserve-inventory-command";

    /// <summary>Inventory.Api'nin dinlediği — COMPENSATING (telafi) talebi.</summary>
    public const string RevertInventoryReservation = "revert-inventory-reservation-command";

    /// <summary>Payment.Api'nin dinlediği — ödeme talebi.</summary>
    public const string ChargePayment = "charge-payment-command";

    /// <summary>Order.Api'nin dinlediği — saga BAŞARIYLA tamamlandı.</summary>
    public const string OrderSagaCompleted = "order-saga-completed-event";

    /// <summary>Order.Api'nin dinlediği — saga BAŞARISIZ oldu.</summary>
    public const string OrderSagaFailed = "order-saga-failed-event";
}
