using BuildingBlocks.Common.Exceptions;
using MediatR;
using Order.Application.Abstractions;
using Order.Domain.Entities;

namespace Order.Application.Commands;

/// <summary>
/// Modül 4 - CQRS Command (yazma tarafı). POST /submit-order endpoint'i
/// artık iş mantığını doğrudan içermez — sadece bu Command'ı oluşturup
/// MediatR'a gönderir (bkz. Order.Api/Program.cs).
/// OrderId OPSİYONELDİR — verilmezse otomatik üretilir; Partition Key
/// testini elle kontrol etmek isterseniz (Modül 3) açıkça belirtebilirsiniz.
/// </summary>
public record CreateOrderCommandWithPartitionKey(string PartitionKey,string CustomerId, decimal TotalAmount, Guid? OrderId = null) : IRequest<CreateOrderResultWithPartitionKey>;

/// <summary>Command'ın başarılı sonucu — hangi sipariş oluşturuldu.</summary>
public record CreateOrderResultWithPartitionKey(Guid OrderId);

public class CreateOrderCommandWithPartitionKeyHandler(
    IOrderRepository repository,
    IOrderEventPublisher eventPublisher,
    IOrderDynamicConfiguration dynamicConfiguration) : IRequestHandler<CreateOrderCommandWithPartitionKey, CreateOrderResultWithPartitionKey>
{
    public async Task<CreateOrderResultWithPartitionKey> Handle(CreateOrderCommandWithPartitionKey  request, CancellationToken cancellationToken)
    {
        // Modül 5 - Consul KV: Bu limit appsettings.json'da DEĞİL, Consul
        // KV'de tutulur ve servis yeniden başlatılmadan değiştirilebilir
        // (bkz. ConsulKvConfigurationWatcher — her 10sn'de bir günceller).
        if (request.TotalAmount > dynamicConfiguration.MaxOrderAmount)
        {
            throw new DomainException(
                $"Sipariş tutarı ({request.TotalAmount:N2}), izin verilen azami tutarı ({dynamicConfiguration.MaxOrderAmount:N2}) aşıyor.");
        }

        var orderId = request.OrderId ?? Guid.NewGuid();

        // 1) Domain: agregat oluşturulur — iş kuralları (Create() içinde)
        //    ihlal edilirse DomainException fırlatılır (400 Bad Request'e eşlenir).
        var order = OrderAggregate.Create(orderId, request.CustomerId, request.TotalAmount);

        // 2) Persistence: veritabanına kaydedilir (Database-per-Service).
        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        // 3) Messaging: OrderCreatedEvent (Publish) + ProcessPaymentCommand
        //    (Send), DB kaydından SONRA, DOĞRUDAN Kafka'ya yayınlanır (Modül 3
        //    tarzı — outbox YOK). Bu iki adım arasında atomiklik garantisi
        //    isteyen bir senaryo test etmek isterseniz: POST /submit-order-outbox
        //    (bkz. SubmitOrderWithOutboxCommand.cs, docs/Order.Infrastructure.md).
        await eventPublisher.PublishOrderCreatedAsync(new Guid(request.PartitionKey), request.CustomerId, request.TotalAmount, cancellationToken);

        return new CreateOrderResultWithPartitionKey(orderId);
    }
}
