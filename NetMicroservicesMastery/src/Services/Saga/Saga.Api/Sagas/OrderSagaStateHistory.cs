namespace Saga.Api.Sagas;

/// <summary>
/// Modül 4 - "Event Streaming" izleme deseni. OrderSagaState (yukarıda),
/// saga'nın SADECE O ANKİ durumunu tutar (üzerine yazılır); bu tablo ise
/// HER durum geçişini AYRI, immutable (değiştirilemez) bir satır olarak
/// append-only şekilde biriktirir — "buraya nasıl geldik?" sorusunu
/// cevaplamak, denetim (audit) ve hata ayıklama için kullanılır.
///
/// NOT: Bu kayıt, OrderSagaState güncellemesiyle AYNI transaction'da
/// DEĞİLDİR (basitlik için) — sadece izleme/gözlem amaçlıdır, saga'nın
/// kendi doğruluğu OrderSagaState'e bağlıdır, bu tabloya değil.
/// </summary>
public class OrderSagaStateHistory
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string FromState { get; set; } = default!;
    public string ToState { get; set; } = default!;
    public string TriggeredByEvent { get; set; } = default!;
    public DateTime OccurredOnUtc { get; set; }

    // Modül 4 - MassTransit, saga Finalize() ile tamamlandığında OrderSagaState
    // satırını REPOSITORY'DEN OTOMATİK OLARAK SİLER (kendi housekeeping
    // davranışı) — bu yüzden CustomerId/TotalAmount burada da (denormalize
    // şekilde) tutulur; aksi halde saga tamamlandıktan sonra bu bilgiler
    // kalıcı olarak kaybolurdu.
    public string CustomerId { get; set; } = default!;
    public decimal TotalAmount { get; set; }
}
