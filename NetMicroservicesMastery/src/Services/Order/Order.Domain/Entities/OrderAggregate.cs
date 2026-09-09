using BuildingBlocks.Common.Exceptions;

namespace Order.Domain.Entities;

/// <summary>
/// Modül 4 - "CQRS" için Order bounded context'inin kök agregatı (aggregate
/// root). İş kuralları (invariant'lar) burada, static factory metodu
/// (Create) üzerinden korunur — geçersiz bir OrderAggregate ASLA
/// oluşturulamaz (constructor private, sadece Create() üzerinden erişilir).
/// Durum geçişleri (Mark...) Saga Pattern akışı tarafından kullanılır —
/// bkz. docs/BuildingBlocks.Messaging.md "Saga Pattern" bölümü.
/// </summary>
public class OrderAggregate
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public decimal TotalAmount { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // EF Core materyalizasyonu için (private) — doğrudan kod tarafından
    // çağrılmamalıdır, bkz. Create().
    private OrderAggregate() { }

    /// <summary>
    /// Yeni bir sipariş oluşturur. İş kuralı ihlallerinde (örn. tutar
    /// sıfır/negatif) DomainException fırlatır — bu, BuildingBlocks.Common'daki
    /// GlobalExceptionHandler tarafından otomatik olarak 400 Bad Request'e
    /// eşlenir (bkz. Modül 1).
    /// </summary>
    public static OrderAggregate Create(Guid orderId, string customerId, decimal totalAmount)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Müşteri kimliği boş olamaz.");
        }

        if (totalAmount <= 0)
        {
            throw new DomainException("Sipariş tutarı sıfırdan büyük olmalıdır.");
        }

        return new OrderAggregate
        {
            Id = orderId,
            CustomerId = customerId,
            TotalAmount = totalAmount,
            Status = OrderStatus.Created,
            CreatedAtUtc = DateTime.UtcNow
        };
    }


  
    // ==================== Saga Pattern — Durum Geçişleri ====================

    /// <summary>Saga başlatıldı, stok rezervasyonu bekleniyor.</summary>
    public void MarkAwaitingInventory() => Status = OrderStatus.AwaitingInventory;

    /// <summary>Stok rezerve edildi, ödeme bekleniyor.</summary>
    public void MarkAwaitingPayment() => Status = OrderStatus.AwaitingPayment;

    /// <summary>Saga başarıyla tamamlandı — ödeme alındı.</summary>
    public void MarkConfirmed() => Status = OrderStatus.Confirmed;

    /// <summary>Saga başarısız oldu (stok yok VEYA ödeme reddedildi) — sipariş iptal edildi.</summary>
    public void MarkCancelled() => Status = OrderStatus.Cancelled;
}

/// <summary>Modül 4 - Siparişin yaşam döngüsü boyunca alabileceği durumlar.</summary>
public enum OrderStatus
{
    Created = 0,

    /// <summary>Saga Pattern: stok rezervasyonu bekleniyor.</summary>
    AwaitingInventory = 1,

    /// <summary>Saga Pattern: stok rezerve edildi, ödeme bekleniyor.</summary>
    AwaitingPayment = 2,

    /// <summary>Saga Pattern: ödeme alındı, sipariş onaylandı (BAŞARI).</summary>
    Confirmed = 3,

    /// <summary>Saga Pattern: stok yok VEYA ödeme reddedildi (BAŞARISIZ).</summary>
    Cancelled = 4
}
