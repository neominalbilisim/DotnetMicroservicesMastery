using System;

namespace BuildingBlocks.Messaging.Outbox;

/// <summary>
/// Modül 4 - "Outbox Pattern".
/// Veritabanı yazımı ile mesaj yayınlamayı AYNI transaction içinde atomik
/// hale getiren outbox tablosu satırı.
///
///   1) DB Kaydı + Outbox Kaydı (Tek Transaction — AYNI SaveChangesAsync)
///   2) Ayrı bir arka plan servisi (Outbox Processor) periyodik olarak
///      işlenmemiş satırları okur
///   3) Her satırı gerçek Kafka topic'ine PRODUCE eder, başarılıysa
///      ProcessedOnUtc'yi doldurur
///
/// ⚠️ NOT: MassTransit'in kendi yerleşik `AddEntityFrameworkOutbox&lt;T&gt;()`
/// mekanizması KULLANILMADI — çünkü o mekanizma, MassTransit'in TEMEL bus'ının
/// (`IPublishEndpoint`/`ISendEndpointProvider`) Send/Publish çağrılarını
/// yakalayacak şekilde tasarlanmıştır; Kafka Rider'ın kendine özgü
/// `ITopicProducer&lt;TKey,TValue&gt;.Produce()` API'siyle ENTEGRE OLMAZ. Bu
/// yüzden Outbox Pattern, bu projede ELLE (ama basit ve anlaşılır şekilde)
/// implemente edilmiştir — bkz. Order.Infrastructure/Messaging/
/// OutboxOrderEventPublisher.cs ve OutboxProcessorHostedService.cs.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    public DateTime OccurredOnUtc { get; set; }

    /// <summary>Mesajın .NET tipi (tam nitelikli ad) — deserialize ederken kullanılır.</summary>
    public string Type { get; set; } = default!;

    /// <summary>Mesajın gönderileceği Kafka topic'i.</summary>
    public string Topic { get; set; } = default!;

    /// <summary>Kafka Message Key (partition seçimini belirler).</summary>
    public string PartitionKey { get; set; } = default!;

    /// <summary>Mesajın JSON serileştirilmiş içeriği.</summary>
    public string Content { get; set; } = default!;

    /// <summary>NULL ise henüz Kafka'ya iletilmemiştir (bekliyor).</summary>
    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>Son iletim denemesinde oluşan hata (varsa) — teşhis amaçlı.</summary>
    public string? Error { get; set; }
}
