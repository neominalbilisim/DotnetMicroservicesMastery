# Order.Infrastructure — CQRS + Outbox Pattern Implementasyonları

**Modül:** Modül 4 — Finansal Uygulamalarda Veri Tutarlılığı

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `Repositories/OrderRepository.cs` | `IOrderRepository`'nin (Order.Application) somut EF Core implementasyonu |
| `Messaging/OutboxOrderEventPublisher.cs` | `IOrderEventPublisher`'ın somut implementasyonu — Kafka'ya DOĞRUDAN değil, outbox tablosuna yazar |
| `Messaging/OutboxProcessorHostedService.cs` | Arka planda outbox tablosunu okuyup gerçek Kafka'ya ileten servis |
| `Persistence/OrderDbContext.cs` | `Orders` + `OutboxMessages` DbSet'leri |

## `OrderRepository`

```csharp
public class OrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    public async Task<OrderAggregate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Orders.FindAsync([id], ct);

    public async Task AddAsync(OrderAggregate entity, CancellationToken ct = default)
        => await dbContext.Orders.AddAsync(entity, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await dbContext.SaveChangesAsync(ct);
}
```

`IOrderRepository` arayüzü **Order.Application.Abstractions**'ta tanımlı
(bu proje sadece implemente eder) — bkz. `docs/Order.Application.md`
"Neden Repository Arayüzü Application'da" bölümü.

## Kayıt (Program.cs)

```csharp
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderEventPublisher, OutboxOrderEventPublisher>();
builder.Services.AddHostedService<OutboxProcessorHostedService>();
```

---

## Outbox Pattern

### Neden MassTransit'in Kendi `AddEntityFrameworkOutbox<T>()`'i Kullanılmadı?

MassTransit'in yerleşik EF Core Outbox mekanizması, TEMEL bus'ın
(`IPublishEndpoint`/`ISendEndpointProvider`) `Send()`/`Publish()` çağrılarını
yakalayacak şekilde tasarlanmıştır. Bizim mesajlarımız ise Kafka Rider'ın
kendine özgü `ITopicProducer<TKey,TValue>.Produce()` API'si üzerinden
gönderiliyor — bu iki mekanizma birbiriyle **entegre olmaz**. Bu yüzden
Outbox Pattern burada **elle** (ama basit ve anlaşılır şekilde) implemente
edildi.

### Akış

```
1) CreateOrderCommandHandler:
   repository.AddAsync(order)              -> DbContext'e eklenir (henüz kaydedilmez)
   eventPublisher.PublishOrderCreatedAsync  -> OutboxMessages'a 2 satır eklenir (henüz kaydedilmez)
   repository.SaveChangesAsync()            -> HEPSİ TEK TRANSACTION'DA kaydedilir (ATOMİK)

2) OutboxProcessorHostedService (arka planda, 5sn'de bir):
   OutboxMessages tablosundan ProcessedOnUtc=NULL satırları oku
   Her birini gerçek Kafka topic'ine PRODUCE et
   Başarılıysa ProcessedOnUtc'yi doldur; başarısızsa satır İŞLENMEMİŞ kalır
   (bir sonraki turda TEKRAR denenir — mesaj asla kaybolmaz)
```

### `OutboxOrderEventPublisher` — Yazma Tarafı

```csharp
public class OutboxOrderEventPublisher(OrderDbContext dbContext) : IOrderEventPublisher
{
    public async Task PublishOrderCreatedAsync(Guid orderId, string customerId, decimal totalAmount, CancellationToken ct)
    {
        // OrderCreatedEvent ve ProcessPaymentCommand, JSON'a serileştirilip
        // OutboxMessages tablosuna eklenir — SaveChangesAsync BURADA ÇAĞRILMAZ.
        await dbContext.OutboxMessages.AddRangeAsync([...], ct);
    }
}
```

**Kritik nokta:** `SaveChangesAsync()` burada değil, `CreateOrderCommandHandler`'da
(repository ile AYNI `OrderDbContext` instance'ı üzerinden, tek seferde)
çağrılır — bu, atomikliğin sırrıdır.

### `OutboxProcessorHostedService` — Okuma/İletim Tarafı

Her 5 saniyede bir, işlenmemiş (`ProcessedOnUtc == null`) en fazla 20 satırı
okur, tipine göre (`OrderCreatedEvent` / `ProcessPaymentCommand`) deserialize
edip ilgili `ITopicProducer`'a produce eder.

## Doğrulama

```bash
# 1) Sipariş oluşturun
curl -X POST http://localhost:5001/submit-order-outbox \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 500.00}'

# 2) Outbox tablosunun durumunu HEMEN kontrol edin (henüz "Bekliyor" olmalı)
curl http://localhost:5001/debug/outbox

# 3) ~5 saniye sonra tekrar kontrol edin — Status "İletildi" olmalı,
#    ProcessedOnUtc dolmuş olmalı.
curl http://localhost:5001/debug/outbox

# 4) Payment.Api/Inventory.Api konsollarında her zamanki
#    🟢/🟡/🟣 EVENT/COMMAND ALINDI satırlarını görün (davranış değişmedi,
#    sadece İLETİM yolu artık atomik).
```

**Atomikliği kanıtlamak için** (opsiyonel, ileri seviye test): Order.Api
çalışırken Postgres'i geçici olarak durdurursanız, `/submit-order-outbox` isteği
tamamen BAŞARISIZ olur (500) ve **outbox'a hiçbir satır yazılmaz** — yarım
kalan bir sipariş (DB'de yok ama Kafka'da event var) senaryosu asla oluşmaz.

## ⚠️ Veritabanı Şeması: EnsureCreated (Migrations Değil)

Bu proje henüz **EF Core Migrations** kullanmıyor — basitlik için `Program.cs`'te
`dbContext.Database.EnsureCreated()` çağrılır. Bu, uygulama her başladığında
çalışır ama **sadece veritabanı TAMAMEN BOŞSA** bir şey yapar (tablo oluşturur).

**Önemli sınırlama:** `EnsureCreated()`, veritabanında **zaten herhangi bir
tablo varsa hiçbir şey yapmaz** — model'e sonradan yeni bir DbSet (örn.
`OutboxMessages`) eklerseniz ve DB'de eski `Orders` tablosu zaten varsa, yeni
tablo **oluşturulmaz** ve `relation "OutboxMessages" does not exist` hatası alırsınız.

**Çözüm:** `order_db`'yi sıfırlayın:

```bash
docker exec -it neominal-postgres psql -U neominal -c "DROP DATABASE order_db;"
docker exec -it neominal-postgres psql -U neominal -c "CREATE DATABASE order_db;"
```

Ardından Order.Api'yi yeniden başlatın — `EnsureCreated()` artık boş DB'yi
bulup güncel model'e göre TÜM tabloları (`Orders`, `OutboxMessages`) oluşturur.

> **Üretim için:** Gerçek bir üretim ortamında `EnsureCreated()` yerine EF Core
> Migrations (`dotnet ef migrations add`, `dbContext.Database.Migrate()`)
> kullanılmalıdır — bu, şema değişikliklerini kademeli ve geri alınabilir
> şekilde uygular. Bu proje eğitim amaçlı basitlik için `EnsureCreated()`'i tercih etti.

## `/submit-order` vs `/submit-order-outbox` — İki Ayrı Endpoint

Karışıklığı önlemek için, sade (Outbox'suz) akış ile Outbox Pattern demosu
**bilinçli olarak iki ayrı endpoint/Command/Handler/Publisher üçlüsüne**
ayrıldı:

| | `POST /submit-order` | `POST /submit-order-outbox` |
|---|---|---|
| Command | `CreateOrderCommand` | `SubmitOrderWithOutboxCommand` |
| Publisher | `IOrderEventPublisher` → `KafkaOrderEventPublisher` | `IOutboxOrderEventPublisher` → `OutboxOrderEventPublisher` |
| Kafka'ya iletim | **Doğrudan**, DB kaydından hemen sonra | **Dolaylı** — önce outbox tablosuna, `OutboxProcessorHostedService` birkaç saniye içinde iletir |
| Atomiklik | ❌ Yok (Modül 3 tarzı) | ✅ Var (DB kaydı + outbox satırları TEK transaction) |
| Ne zaman kullanılır | Modül 3 senaryolarını (Command/Event, Partition Key, DLQ) test ederken | Outbox Pattern'i izole test ederken |

Her iki endpoint de aynı `SubmitOrderRequest` payload'ını kabul eder ve aynı
`OrderAggregate`'i oluşturur — sadece **mesajın Kafka'ya nasıl ulaştığı**
farklıdır.

## Bilinen Sınırlamalar / Sonraki Adımlar

- Outbox tablosu şu an sadece Order.Api'de var; Payment.Api/Inventory.Api
  CQRS'e geçirildiğinde onlarda da benzer bir yapı gerekebilir.
- ~~`OutboxProcessorHostedService` çoklu instance'a karşı korumasız~~ ✅
  Tamamlandı — bkz. aşağıdaki "Distributed Lock" bölümü.
- **Saga Pattern** her iki versiyonuyla (Choreography + Orchestration) tamamlandı.

---

## Distributed Lock (Redis / RedLock.net)

**Sorun:** `OutboxProcessorHostedService`, her Order.Api instance'ında
**bağımsız** olarak her 5 saniyede bir çalışır. Birden fazla Order.Api
instance'ı çalıştığında (bkz. Modül 2'deki Inventory.Api çoklu-instance
testiyle birebir aynı senaryo), her instance'ın kendi zamanlayıcısı AYNI
outbox satırlarını okuyabilir — bu, aynı mesajın Kafka'ya **iki kez**
üretilmesine yol açar.

**Çözüm:** Redis tabanlı bir dağıtık kilit (`IDistributedLockService`,
Redlock algoritması — `RedLock.net` paketi). Her poll turunda, önce kilidi
almayı dener; alamazsa (başka bir instance zaten tutuyorsa) o turu **hiç
işlem yapmadan** atlar.

```csharp
using var @lock = await distributedLockService.TryAcquireAsync(
    "order-service:outbox-processor", TimeSpan.FromSeconds(30), ct);

if (@lock is null)
{
    // Başka bir instance kilidi tutuyor — bu turu atla.
    return;
}

// ... outbox satırlarını oku, Kafka'ya üret ...
// 'using' bloğu sona erdiğinde kilit HEMEN serbest bırakılır (30sn'lik
// expiry'nin dolmasını beklemeye gerek yok).
```

**Kilit anahtarı TÜM instance'lar için AYNIDIR** (`"order-service:outbox-processor"`)
— bu kasıtlıdır: "hangi instance olursa olsun, aynı anda sadece biri bu
kaynağı işlesin" demektir.

### Doğrulama: Çoklu Instance Testi

Modül 2'deki Inventory.Api testiyle AYNI yöntemle, Order.Api'yi 2 farklı
portta çalıştırın (`dotnet run --launch-profile "..."` veya `ASPNETCORE_URLS`
ile). Her iki instance da aynı `order_db` ve aynı Redis'e bağlanmalıdır
(appsettings zaten böyle yapılandırılı).

Bir sipariş oluşturun (`POST /submit-order-outbox`) ve **her iki instance'ın
konsolunu** izleyin. Şunu göreceksiniz:

```
🔒 [Outbox][a1b2c3d4] Kilit ALINDI — 2 mesaj işlenecek.
📤 [Outbox][a1b2c3d4] Mesaj Kafka'ya iletildi: ...

🔓 [Outbox][e5f6g7h8] Kilit başka bir instance'ta — bu tur ATLANDI.
```

Her poll turunda **sadece bir instance** ("a1b2c3d4" gibi kısa, rastgele bir
kimlikle loglanır — hangi instance olduğunu ayırt etmeniz için) gerçekten
işlem yapar; diğeri sessizce atlar. Bir sonraki turda (5sn sonra) hangi
instance'ın kilidi alacağı belirsizdir (ilk isteyen alır) — bu normaldir,
önemli olan **asla ikisinin birden aynı anda işlem yapmamasıdır**.

---

## Consul KV: Merkezi/Dinamik Konfigürasyon

**Amaç:** `appsettings.json`'daki değerler sadece uygulama başlarken okunur —
değiştirmek için servisi yeniden başlatmanız gerekir. Consul KV ile bazı
değerler **servis çalışırken, yeniden başlatmadan** değiştirilebilir.

**Örnek:** Bir siparişin alabileceği **azami tutar** (`MaxOrderAmount`),
`config/order-service/max-order-amount` anahtarından okunur.

### Nasıl Çalışır

```
Consul KV (anahtar) --[her 10sn'de bir okunur]--> ConsulKvConfigurationWatcher
                                                          |
                                                          v
                                              OrderDynamicConfiguration
                                            (thread-safe, bellekte tutulan değer)
                                                          |
                                                          v
                                          CreateOrderCommandHandler bir sonraki
                                          istekte YENİ değeri kullanır
```

`ConsulKvConfigurationWatcher` (bir `BackgroundService`), her 10 saniyede
bir Consul'daki anahtarı okur; anahtar hiç tanımlı değilse (henüz kimse
ayarlamadıysa) varsayılan (1.000.000 — pratikte sınırsız) değer korunur.

> **Not:** Burada basit **polling** (periyodik okuma) kullanıldı. Consul'un
> "blocking query" (uzun polling) özelliği anlık bildirim için daha
> verimlidir ama karmaşıklığı artırır — bu eğitim projesinde anlaşılırlık
> için basit polling tercih edildi.

### Değeri Consul'da Ayarlamak

**Consul UI'dan** (`http://localhost:8500`):
1. Sol menüden **Key/Value** sekmesine gidin.
2. "Create" ile yeni bir anahtar oluşturun: `config/order-service/max-order-amount`
3. Değer olarak bir sayı girin, örn. `500` (Content-Type text olarak bırakın).
4. Save.

**CLI'dan** (Consul container içinde):
```bash
docker exec -it neominal-consul consul kv put config/order-service/max-order-amount 500
```

### Doğrulama

```bash
# 1) Şu anki değeri görün
curl http://localhost:5001/debug/config

# 2) Consul'da değeri 100'e düşürün (yukarıdaki adımlarla), ~10sn bekleyin

# 3) Tekrar kontrol edin — değer güncellenmiş olmalı
curl http://localhost:5001/debug/config

# 4) Limitin üzerinde bir sipariş verin -> 400 Bad Request almalısınız
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 999999}'

# 5) Limitin altında bir sipariş verin -> normal çalışmalı
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 50}'
```

Order.Api'nin konsolunda, değer her değiştiğinde şu satırı görürsünüz:
```
🔧 [ConsulKV] MaxOrderAmount değişti: 1000000,00 -> 500,00
```
