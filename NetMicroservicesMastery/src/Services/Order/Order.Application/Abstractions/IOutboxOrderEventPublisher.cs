namespace Order.Application.Abstractions;

/// <summary>
/// Modül 4 - Outbox Pattern'e ÖZEL soyutlama. `IOrderEventPublisher`'dan
/// (Modül 3'ten beri var, doğrudan Kafka'ya üretir) BİLİNÇLİ olarak ayrı
/// tutuldu — böylece "/submit-order" (sade, outbox'suz) ile
/// "/submit-order-outbox" (Outbox Pattern demo) birbirine karışmaz, her
/// biri kendi Command/Handler/Publisher üçlüsüne sahiptir.
/// </summary>
public interface IOutboxOrderEventPublisher
{
    /// <summary>
    /// OrderCreatedEvent + ProcessPaymentCommand'ı Kafka'ya DEĞİL, aynı
    /// DbContext'in outbox tablosuna ekler (henüz kaydetmez — çağıran taraf
    /// SaveChangesAsync'i Order kaydıyla BİRLİKTE, tek seferde çağırmalıdır).
    /// </summary>
    Task EnqueueOrderCreatedAsync(Guid orderId, string customerId, decimal totalAmount, CancellationToken cancellationToken = default);
}
