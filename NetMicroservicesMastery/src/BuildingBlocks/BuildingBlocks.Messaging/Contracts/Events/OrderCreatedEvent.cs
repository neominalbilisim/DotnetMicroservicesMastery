using System;

namespace BuildingBlocks.Messaging.Contracts.Events;

/// <summary>
/// Modül 3 - "Kurumsal Mesajlaşma Konseptleri: Command ve Event Ayrımı".
/// Bu bir EVENT'tir (Command değil): "bir sipariş oluşturuldu" olgusunu
/// bildirir, kimin dinleyeceğini üreten taraf (Order.Api) bilmez/umursamaz.
/// Publish() ile yayınlanır (Send() ile DEĞİL) — hem Payment.Api hem
/// Inventory.Api (ve ileride başka servisler) bağımsız olarak dinleyebilir.
///
/// PartitionKey = OrderId: aynı siparişe ait ardışık event'lerin (ileride
/// "OrderCancelledEvent" gibi) Kafka'da HER ZAMAN aynı partition'a düşmesini
/// ve dolayısıyla sıralı işlenmesini garanti eder (bkz. Ön Hazırlık
/// Dökümanı Modül 3 - "Message Key/Partition Key" bölümü).
/// </summary>
public record OrderCreatedEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount) : IIntegrationEvent
{
    public string PartitionKey => OrderId.ToString();
}
