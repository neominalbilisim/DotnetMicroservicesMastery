namespace BuildingBlocks.Messaging.Contracts;

/// <summary>
/// Modül 3 - "Kurumsal Mesajlaşma Konseptleri: Command ve Event Ayrımı".
/// Publish() edilen, "bir şey oldu" anlamına gelen tüm servisler-arası olayların
/// (integration event) temel sözleşmesi. Command'lar (Send()) için ayrı bir
/// ICommand sözleşmesi kullanılmalıdır (bkz. IIntegrationCommand).
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
    /// <summary>Kafka Partition Key (Message Key) — sıralı işlenmesi gereken
    /// mesajlarda (örn. aynı OrderId) doldurulmalıdır. bkz. Modül 3.</summary>
    string PartitionKey { get; }
}

/// <summary>
/// "Bir şey yap" anlamına gelen, tek bir alıcıya (Send()) yönlendirilen komut sözleşmesi.
/// </summary>
public interface IIntegrationCommand
{
    Guid CommandId { get; }
}
