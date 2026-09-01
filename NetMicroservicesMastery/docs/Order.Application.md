# Order.Application — CQRS (MediatR)

**Modül:** Modül 4 — Finansal Uygulamalarda Veri Tutarlılığı
**Konu:** CQRS (Command Query Responsibility Segregation)

## Amaç

`/submit-order` endpoint'ine gömülü olan iş mantığı (Modül 3'te doğrudan
`Program.cs`'te yaşıyordu) artık **ince bir HTTP katmanı + MediatR handler'ları**
şeklinde ayrıştırıldı. Endpoint'ler artık sadece "HTTP isteğini bir
Command/Query'e çevirip MediatR'a gönderme" işi yapar — iş mantığının
kendisiyle hiç ilgilenmez.

```
HTTP İsteği -> (ince) Endpoint -> MediatR.Send(Command/Query)
            -> ValidationBehavior (girdi doğrulaması)
            -> Handler (asıl iş mantığı)
            -> Sonuç -> HTTP Cevabı
```

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `DependencyInjection.cs` | MediatR + FluentValidation + `ValidationBehavior` kaydı |
| `Abstractions/IOrderRepository.cs` | Repository **arayüzü** (Application'da — Clean Architecture) |
| `Abstractions/IOrderEventPublisher.cs` | Kafka'ya bağımlı olmadan "event yayınla" soyutlaması |
| `Commands/CreateOrderCommand.cs` | Yazma tarafı: Command + Handler + Result |
| `Commands/CreateOrderCommandValidator.cs` | FluentValidation kuralları (girdi biçimi kontrolü) |
| `Queries/GetOrderByIdQuery.cs` | Okuma tarafı: Query + Handler + `OrderDto` |
| `Behaviors/ValidationBehavior.cs` | MediatR pipeline behavior — handler'dan ÖNCE validasyon |

## Neden Repository Arayüzü Application'da (Infrastructure'da Değil)?

**Clean Architecture'ın temel kuralı:** bağımlılıklar her zaman **içe doğru**
akar. Application katmanı "ne istediğini" tanımlar (`IOrderRepository`
arayüzü); Infrastructure katmanı "nasıl yapılacağını" (EF Core ile
`OrderRepository` implementasyonu) sağlar ve Application'a bağımlıdır —
tam tersi değil.

```
Order.Infrastructure  ──implements──>  IOrderRepository  <──uses──  Order.Application
     (OrderRepository, EF Core)         (Abstractions/)              (CreateOrderCommandHandler)
```

Bu sayede Application katmanı (iş mantığının kalbi) **EF Core'a, MassTransit'e,
hiçbir dış teknolojiye doğrudan bağımlı değildir** — sadece kendi tanımladığı
soyutlamalara. Bu, ileride veritabanını veya mesajlaşma altyapısını
değiştirmek istediğinizde Application katmanına HİÇ dokunmadan yapabilmenizi
sağlar.

Aynı ilke `IOrderEventPublisher` için de geçerlidir — `CreateOrderCommandHandler`,
Kafka'nın veya MassTransit'in var olduğunu bile bilmez; sadece
"PublishOrderCreatedAsync" diye bir şey çağırır. Gerçek Kafka implementasyonu
(`KafkaOrderEventPublisher`) Infrastructure katmanındadır (bkz.
`docs/Order.Infrastructure.md`).

## `CreateOrderCommand` Akışı

```csharp
public record CreateOrderCommand(string CustomerId, decimal TotalAmount, Guid? OrderId = null) : IRequest<CreateOrderResult>;

public class CreateOrderCommandHandler(
    IOrderRepository repository,
    IOrderEventPublisher eventPublisher) : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var order = OrderAggregate.Create(orderId, request.CustomerId, request.TotalAmount); // 1) Domain kuralları
        await repository.AddAsync(order, ct);                                                 // 2) DB'ye EKLE (henüz kaydetme)
        await eventPublisher.PublishOrderCreatedAsync(orderId, ..., ct);                       // 3) Outbox'a EKLE (henüz kaydetme)
        await repository.SaveChangesAsync(ct);                                                // 4) TEK seferde KAYDET (atomik)
        return new CreateOrderResult(orderId);
    }
}
```

> ⚠️ **Sıra kritiktir:** `SaveChangesAsync()` MUTLAKA en sonda, adım 2 ve 3'ten
> SONRA çağrılmalıdır. Önce çağrılırsa, adım 3'te DbContext'e eklenen outbox
> satırları hiçbir zaman veritabanına yazılmaz — sadece bellekte "tracked"
> kalıp DbContext scope'u sonunda sessizce kaybolur (gerçek bir hata olarak
> yaşandı ve düzeltildi — bkz. `docs/Order.Infrastructure.md`).

### ✅ Atomiklik Sağlandı (Outbox Pattern)

DB kaydı (Order) ile mesaj yayınlama (Outbox satırları), artık **TEK
`SaveChangesAsync()` çağrısında, aynı transaction'da** yazılır — Outbox
Pattern'in sağladığı garanti budur. Detaylar için bkz. `docs/Order.Infrastructure.md`.

## Validasyon: `ValidationBehavior`

```csharp
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // İlgili tipte kayıtlı validator'lar varsa çalıştırılır; hata varsa
        // handler'a HİÇ gidilmeden DomainException fırlatılır.
    }
}
```

`CreateOrderCommandValidator`, `CreateOrderCommand`'a özel kuralları tanımlar
(örn. `CustomerId` boş olamaz, `TotalAmount > 0`). Bu kurallar **handler
çalışmadan önce** otomatik uygulanır — handler kodu validasyon mantığı
içermez.

**İki farklı doğrulama katmanı olduğuna dikkat edin:**

| Katman | Ne kontrol eder | Örnek |
|---|---|---|
| `CreateOrderCommandValidator` (FluentValidation) | Girdi **biçimi** | `CustomerId` boş mu? |
| `OrderAggregate.Create()` (Domain) | **İş kuralı** (invariant) | `TotalAmount > 0` mı? |

İkisi de sonuçta `DomainException` fırlatır — Modül 1'deki
`GlobalExceptionHandler` tarafından otomatik olarak `400 Bad Request`'e eşlenir.

## Doğrulama

```bash
# Geçerli istek -> 202 Accepted
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 250.00}'

# Geçersiz istek (boş customerId) -> 400 Bad Request (ValidationBehavior devreye girer)
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "", "totalAmount": 250.00}'

# Geçersiz istek (negatif tutar) -> 400 Bad Request (ValidationBehavior devreye girer)
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": -10}'

# Query: az önce oluşturduğunuz siparişi okuyun
curl http://localhost:5001/orders/<orderId>
```

## Bilinen Sınırlamalar / Sonraki Adımlar

- ~~**Outbox Pattern** henüz implemente edilmedi~~ ✅ Tamamlandı — bkz. `docs/Order.Infrastructure.md`.
- **Saga Pattern** henüz implemente edilmedi — Payment/Inventory'nin
  başarısız olması durumunda Order'ın telafi edilmesi (compensating
  transaction) henüz yok.
- Payment.Api ve Inventory.Api henüz CQRS'e geçirilmedi — sadece Order.Api
  bu turda güncellendi.
