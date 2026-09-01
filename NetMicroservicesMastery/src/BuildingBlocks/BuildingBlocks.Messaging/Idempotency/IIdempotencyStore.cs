using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildingBlocks.Messaging.Idempotency;

/// <summary>
/// Modül 5 - "Idempotent Consumer" tasarımı. Bir mesajın (MassTransit'in
/// otomatik ürettiği ConsumeContext.MessageId ile tanımlanır) DAHA ÖNCE
/// işlenip işlenmediğini kontrol eden ve işlendiğini kaydeden soyutlama.
///
/// NEDEN GEREKLİ: Bir mesaj broker tarafından (retry, redelivery, consumer
/// crash sonrası yeniden teslim vb. nedenlerle) AYNI MessageId ile birden
/// fazla kez teslim edilebilir. Consumer'ın yan etkisi (örn. "ödeme al",
/// "stok azalt") TEKRAR ÇALIŞTIRILIRSA gerçek bir soruna yol açar (örn.
/// müşteriden iki kez para çekilmesi). Bu arayüz, "bu mesajı daha önce
/// gördüm mü?" kontrolünü standartlaştırır.
///
/// Her servis, kendi veritabanına (kendi DbContext'ine) yazan bir
/// implementasyon sağlar — bkz. ilgili *.Infrastructure/Idempotency/ klasörü.
/// </summary>
public interface IIdempotencyStore
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default);
}
