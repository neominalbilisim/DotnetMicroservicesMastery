using MassTransit;

namespace Saga.Api.Sagas;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION). MassTransit'in Saga State
/// Machine'inin, her bir saga instance'ının (bir sipariş = bir saga
/// instance'ı) DURUMUNU sakladığı satır. CorrelationId, OrderId ile
/// AYNI değerdir — MassTransit gelen her mesajı bu alana göre doğru
/// instance'a yönlendirir.
/// </summary>
public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    /// <summary>MassTransit State Machine'in "hangi state'teyiz" bilgisini string olarak tuttuğu alan.</summary>
    public string CurrentState { get; set; } = default!;

    public string CustomerId { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
