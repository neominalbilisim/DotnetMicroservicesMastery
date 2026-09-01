namespace Order.Infrastructure.Outbox;

/// <summary>
/// Modül 5 - "Idempotent Consumer" tasarımı.
/// Aynı mesajın (örn. retry sonucu) birden fazla kez gelmesi durumunda dahi
/// tek seferlik işlenmiş sonucu garanti etmek için işlenen mesaj ID'lerinin
/// tutulduğu tabloya karşılık gelen kayıt.
/// Not: MassTransit'in kendi InboxState mekanizması da (Modül 4/5) bu amaçla
/// kullanılabilir — bu sınıf, ek/manuel idempotency kontrolleri için örnektir.
/// TODO (Modül 5): Consumer içinde mesaj işlenmeden önce bu tabloyu kontrol et,
/// işlem sonunda kaydı ekle (mümkünse aynı DB transaction'ı içinde).
/// </summary>
public record ProcessedMessage(Guid MessageId, DateTime ProcessedOnUtc);
