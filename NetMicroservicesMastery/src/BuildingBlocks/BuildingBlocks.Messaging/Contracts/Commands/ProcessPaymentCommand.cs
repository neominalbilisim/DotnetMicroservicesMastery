using System;

namespace BuildingBlocks.Messaging.Contracts.Commands;

/// <summary>
/// Modül 3 - "Kurumsal Mesajlaşma Konseptleri: Command ve Event Ayrımı".
/// Bu bir COMMAND'tır (Event değil): "ödemeyi işle" anlamına gelir — Order.Api,
/// bunu YALNIZCA Payment.Api'nin işlemesini bekleyerek gönderir (Send()
/// semantiği). OrderCreatedEvent'in aksine (Publish() — kimin dinlediği
/// önemsiz), bu mesajın "sahibi" bellidir: Payment.Api.
///
/// Kafka'da gerçek bir "queue" (point-to-point) kavramı olmadığından, bu
/// Command/Event ayrımı bir API farkıyla DEĞİL, bir KONVANSİYONLA temsil
/// edilir: "process-payment-command" topic'ini SADECE Payment.Api dinler
/// (Inventory.Api dinlemez) — "order-created" topic'ini ise HER İKİSİ de
/// bağımsız olarak dinler. Somut fark budur.
/// </summary>
public record ProcessPaymentCommand(
    Guid CommandId,
    Guid OrderId,
    string CustomerId,
    decimal Amount) : IIntegrationCommand
{
    /// <summary>Aynı siparişe ait ödeme komutlarının sıralı işlenmesi için.</summary>
    public string PartitionKey => OrderId.ToString();
}
