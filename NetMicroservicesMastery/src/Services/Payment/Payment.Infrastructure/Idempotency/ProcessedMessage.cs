namespace Payment.Infrastructure.Idempotency;

/// <summary>
/// Modül 5 - "Idempotent Consumer". İşlenmiş bir mesajın kalıcı kaydı.
/// MessageId birincil anahtardır — aynı MessageId ile ikinci bir INSERT
/// denemesi (aynı mesaj tekrar gelirse) veritabanı seviyesinde de reddedilir
/// (ek bir güvence katmanı).
/// </summary>
public class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public string ConsumerName { get; set; } = default!;
    public DateTime ProcessedOnUtc { get; set; }
}
