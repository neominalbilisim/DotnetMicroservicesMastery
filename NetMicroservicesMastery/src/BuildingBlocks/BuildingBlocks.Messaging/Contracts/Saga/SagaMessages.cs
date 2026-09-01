using System;

namespace BuildingBlocks.Messaging.Contracts.Saga;

// =====================================================================
// Modül 4 - Saga Pattern (ORCHESTRATION). Bu mesajlar RabbitMQ üzerinden
// taşınır ve SADECE Saga.Api'nin state machine'i tarafından yönetilir.
// Choreography örneğindeki (Contracts.Events/Commands, Kafka üzerinden)
// mesajlarla İSİM OLARAK BİLİNÇLİ ŞEKİLDE FARKLI tutuldu — ikisi tamamen
// izole, birbirinden habersiz iki ayrı akıştır.
//
// CorrelationId = OrderId: MassTransit'in Saga State Machine'i, gelen her
// mesajı BU alana göre doğru saga instance'ına yönlendirir (correlation).
// =====================================================================

/// <summary>Order.Api -> Saga.Api (Send). Saga'yı başlatır.</summary>
public record BeginOrderSagaCommand(Guid CorrelationId, string CustomerId, decimal TotalAmount);

/// <summary>Saga.Api -> Inventory.Api (Send). Stok rezervasyonu talebi.</summary>
public record ReserveInventoryCommand(Guid CorrelationId, string CustomerId, decimal TotalAmount);

/// <summary>Inventory.Api -> Saga.Api (Send/reply). Rezervasyon BAŞARILI.</summary>
public record InventoryReservationSucceededEvent(Guid CorrelationId);

/// <summary>Inventory.Api -> Saga.Api (Send/reply). Rezervasyon BAŞARISIZ (stok yok).</summary>
public record InventoryReservationRejectedEvent(Guid CorrelationId, string Reason);

/// <summary>Saga.Api -> Payment.Api (Send). Ödeme talebi.</summary>
public record ChargePaymentCommand(Guid CorrelationId, string CustomerId, decimal TotalAmount);

/// <summary>Payment.Api -> Saga.Api (Send/reply). Ödeme BAŞARILI.</summary>
public record PaymentChargedEvent(Guid CorrelationId);

/// <summary>Payment.Api -> Saga.Api (Send/reply). Ödeme BAŞARISIZ (reddedildi).</summary>
public record PaymentChargeFailedEvent(Guid CorrelationId, string Reason);

/// <summary>
/// Saga.Api -> Inventory.Api (Send). COMPENSATING ADIM: ödeme başarısız
/// olduğunda, az önce rezerve edilen stoğun geri bırakılması talep edilir.
/// </summary>
public record RevertInventoryReservationCommand(Guid CorrelationId);

/// <summary>Saga.Api -> Order.Api (Send). Saga BAŞARIYLA tamamlandı.</summary>
public record OrderSagaCompletedEvent(Guid CorrelationId);

/// <summary>Saga.Api -> Order.Api (Send). Saga BAŞARISIZ oldu (nedeniyle birlikte).</summary>
public record OrderSagaFailedEvent(Guid CorrelationId, string Reason);
