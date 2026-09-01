using MediatR;
using Order.Application.Abstractions;
using Order.Domain.Entities;

namespace Order.Application.Commands;

/// <summary>
/// Modül 4 - Outbox Pattern'e ÖZEL, dedike Command. `CreateOrderCommand`
/// (POST /submit-order — sade, Modül 3 tarzı, DOĞRUDAN Kafka) ile BİLİNÇLİ
/// olarak ayrı tutuldu — böylece Outbox Pattern'in atomiklik garantisini
/// izole bir şekilde test edebilirsiniz (POST /submit-order-outbox),
/// diğer endpoint'in davranışını hiç etkilemeden.
/// </summary>
public record SubmitOrderWithOutboxCommand(string CustomerId, decimal TotalAmount, Guid? OrderId = null) : IRequest<CreateOrderResult>;

public class SubmitOrderWithOutboxCommandHandler(
    IOrderRepository repository,
    IOutboxOrderEventPublisher outboxPublisher) : IRequestHandler<SubmitOrderWithOutboxCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(SubmitOrderWithOutboxCommand request, CancellationToken cancellationToken)
    {
        var orderId = request.OrderId ?? Guid.NewGuid();

        // 1) Domain: agregat oluşturulur.
        var order = OrderAggregate.Create(orderId, request.CustomerId, request.TotalAmount);

        // 2) Persistence: DbContext'e EKLENİR (henüz kaydedilmez).
        await repository.AddAsync(order, cancellationToken);

        // 3) Outbox: OrderCreatedEvent + ProcessPaymentCommand, Kafka'ya
        //    DEĞİL, AYNI DbContext'in OutboxMessages tablosuna eklenir
        //    (henüz kaydedilmez).
        await outboxPublisher.EnqueueOrderCreatedAsync(orderId, request.CustomerId, request.TotalAmount, cancellationToken);

        // 4) TEK SaveChangesAsync çağrısı: Order kaydı + 2 outbox satırı AYNI
        //    transaction'da, ATOMİK olarak yazılır. (⚠️ Sıra kritiktir — bu
        //    satır MUTLAKA adım 2/3'ten SONRA, en sonda olmalıdır; aksi halde
        //    outbox satırları hiçbir zaman veritabanına yazılmaz.)
        await repository.SaveChangesAsync(cancellationToken);

        return new CreateOrderResult(orderId);
    }
}
