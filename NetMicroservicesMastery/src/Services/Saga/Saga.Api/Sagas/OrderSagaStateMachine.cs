using BuildingBlocks.Messaging.Contracts.Saga;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Saga.Api.Persistence;

namespace Saga.Api.Sagas;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION). Merkezi bir "beyin" — Order.Api,
/// Payment.Api, Inventory.Api'nin birbirinden HABERSİZ olduğu Choreography
/// örneğinin aksine, TÜM akış BURADA, tek bir yerde, açıkça tanımlıdır.
/// Bu, State Machine'in temel avantajıdır: akışı anlamak için birden fazla
/// servisin kodunu takip etmenize gerek yoktur — hepsi bu dosyada.
///
///   Initially(BeginOrderSagaCommand)
///     -> AwaitingInventory: ReserveInventoryCommand gönderilir
///   During(AwaitingInventory)
///     -> InventoryReservationSucceeded: ChargePaymentCommand gönderilir -> AwaitingPayment
///     -> InventoryReservationRejected:  OrderSagaFailedEvent gönderilir -> Failed (compensation YOK)
///   During(AwaitingPayment)
///     -> PaymentCharged:      OrderSagaCompletedEvent gönderilir -> Completed
///     -> PaymentChargeFailed: RevertInventoryReservationCommand (COMPENSATION) +
///                             OrderSagaFailedEvent gönderilir -> Failed
/// </summary>
public class OrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public State AwaitingInventory { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<BeginOrderSagaCommand> SagaRequested { get; private set; } = null!;
    public Event<InventoryReservationSucceededEvent> InventoryReserved { get; private set; } = null!;
    public Event<InventoryReservationRejectedEvent> InventoryRejected { get; private set; } = null!;
    public Event<PaymentChargedEvent> PaymentCharged { get; private set; } = null!;
    public Event<PaymentChargeFailedEvent> PaymentFailed { get; private set; } = null!;

    public OrderSagaStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => SagaRequested, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => InventoryReserved, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => InventoryRejected, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => PaymentCharged, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.CorrelationId));

        Initially(
            When(SagaRequested)
                .Then(context =>
                {
                    context.Saga.CustomerId = context.Message.CustomerId;
                    context.Saga.TotalAmount = context.Message.TotalAmount;
                    context.Saga.CreatedAtUtc = DateTime.UtcNow;
                    Console.WriteLine($"🟢 [Saga.Api] SAGA BAŞLADI (Orchestration) — OrderId={context.Saga.CorrelationId}, Müşteri={context.Message.CustomerId}");
                })
                .ThenAsync(context => RecordHistoryAsync(context.GetPayload<IServiceProvider>(),
                    context.Saga.CorrelationId, "Initial", "AwaitingInventory", nameof(BeginOrderSagaCommand),
                    context.Saga.CustomerId, context.Saga.TotalAmount))
                .Send(new Uri($"queue:{SagaQueues.ReserveInventory}"), context => new ReserveInventoryCommand(
                    context.Saga.CorrelationId, context.Saga.CustomerId, context.Saga.TotalAmount))
                .TransitionTo(AwaitingInventory)
        );

        During(AwaitingInventory,
            When(InventoryReserved)
                .Then(context => Console.WriteLine($"🟢 [Saga.Api] STOK REZERVE EDİLDİ — OrderId={context.Saga.CorrelationId}, ödeme talep ediliyor..."))
                .ThenAsync(context => RecordHistoryAsync(context.GetPayload<IServiceProvider>(),
                    context.Saga.CorrelationId, "AwaitingInventory", "AwaitingPayment", nameof(InventoryReservationSucceededEvent),
                    context.Saga.CustomerId, context.Saga.TotalAmount))
                .Send(new Uri($"queue:{SagaQueues.ChargePayment}"), context => new ChargePaymentCommand(
                    context.Saga.CorrelationId, context.Saga.CustomerId, context.Saga.TotalAmount))
                .TransitionTo(AwaitingPayment),

            When(InventoryRejected)
                .Then(context => Console.WriteLine($"🔴 [Saga.Api] SAGA BAŞARISIZ (stok yok) — OrderId={context.Saga.CorrelationId}, Hata={context.Message.Reason}. Compensation GEREKMİYOR."))
                .ThenAsync(context => RecordHistoryAsync(context.GetPayload<IServiceProvider>(),
                    context.Saga.CorrelationId, "AwaitingInventory", "Failed", nameof(InventoryReservationRejectedEvent),
                    context.Saga.CustomerId, context.Saga.TotalAmount))
                .Send(new Uri($"queue:{SagaQueues.OrderSagaFailed}"), context => new OrderSagaFailedEvent(
                    context.Saga.CorrelationId, context.Message.Reason))
                .TransitionTo(Failed)
                .Finalize()
        );

        During(AwaitingPayment,
            When(PaymentCharged)
                .Then(context => Console.WriteLine($"✅ [Saga.Api] SAGA BAŞARIYLA TAMAMLANDI — OrderId={context.Saga.CorrelationId}"))
                .ThenAsync(context => RecordHistoryAsync(context.GetPayload<IServiceProvider>(),
                    context.Saga.CorrelationId, "AwaitingPayment", "Completed", nameof(PaymentChargedEvent),
                    context.Saga.CustomerId, context.Saga.TotalAmount))
                .Send(new Uri($"queue:{SagaQueues.OrderSagaCompleted}"), context => new OrderSagaCompletedEvent(context.Saga.CorrelationId))
                .TransitionTo(Completed)
                .Finalize(),

            When(PaymentFailed)
                .Then(context => Console.WriteLine($"🔴 [Saga.Api] SAGA BAŞARISIZ (ödeme reddi) — OrderId={context.Saga.CorrelationId}, Hata={context.Message.Reason}. COMPENSATION tetikleniyor..."))
                .ThenAsync(context => RecordHistoryAsync(context.GetPayload<IServiceProvider>(),
                    context.Saga.CorrelationId, "AwaitingPayment", "Failed", nameof(PaymentChargeFailedEvent),
                    context.Saga.CustomerId, context.Saga.TotalAmount))
                // COMPENSATING ADIM: stok ZATEN rezerve edilmişti, geri bırakılmalı.
                .Send(new Uri($"queue:{SagaQueues.RevertInventoryReservation}"), context => new RevertInventoryReservationCommand(context.Saga.CorrelationId))
                .Send(new Uri($"queue:{SagaQueues.OrderSagaFailed}"), context => new OrderSagaFailedEvent(
                    context.Saga.CorrelationId, context.Message.Reason))
                .TransitionTo(Failed)
                .Finalize()
        );

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// "Event Streaming" izleme kaydı — bkz. OrderSagaStateHistory.cs. Saga
    /// state machine sınıfı singleton olduğundan (asıl durum OrderSagaState
    /// instance'larında tutulur), scoped bir DbContext'e erişmek için
    /// context.GetPayload&lt;IServiceProvider&gt;() ile o mesaja özel DI scope'undan
    /// yeni bir scope türetilir. CustomerId/TotalAmount de burada (denormalize)
    /// saklanır — MassTransit, saga Finalize() ile tamamlandığında OrderSagaState
    /// satırını OTOMATİK OLARAK SİLDİĞİ için (bkz. Saga.Api/Program.cs debug
    /// endpoint'indeki not), bu bilgiler aksi halde kalıcı olarak kaybolurdu.
    /// </summary>
    private static async Task RecordHistoryAsync(
        IServiceProvider serviceProvider, Guid orderId, string fromState, string toState, string triggeredByEvent,
        string customerId, decimal totalAmount)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();

        dbContext.OrderSagaHistory.Add(new OrderSagaStateHistory
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromState = fromState,
            ToState = toState,
            TriggeredByEvent = triggeredByEvent,
            OccurredOnUtc = DateTime.UtcNow,
            CustomerId = customerId,
            TotalAmount = totalAmount
        });

        await dbContext.SaveChangesAsync();
    }
}
