# BuildingBlocks.Messaging

**Modül:** Modül 3 — Event-Driven Mimari ve MassTransit Temelleri
**Konu:** MassTransit + Kafka temel kurulumu (Producer/Consumer altyapısı)

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `MessagingExtensions.cs` | `AddDistributedMessaging()` — MassTransit'in Kafka Rider'ını tek noktadan yapılandıran ortak extension |
| `Contracts/KafkaTopics.cs` | Topic isimlerinin tek merkezi kaynağı (`order-created` vb.) |
| `Contracts/Events/OrderCreatedEvent.cs` | İlk somut Event contract'ı (Order.Api → Payment.Api + Inventory.Api) |
| `Contracts/IntegrationEvent.cs` | `IIntegrationEvent` / `IIntegrationCommand` temel sözleşmeleri (Modül 1'den beri var) |

## Neden "Rider"? (MassTransit Terminolojisi)

MassTransit, RabbitMQ gibi klasik broker'lar için "bus" kavramını kullanırken,
Kafka (bir broker değil, bir "log"tur) için ayrı bir soyutlama katmanı —
**Rider** — kullanır. `AddRider(...)` içinde `AddProducer`/`AddConsumer`
çağrıları yapılır, `UsingKafka(...)` içinde ise broker adresi (`Host`) ve
topic-consumer eşlemeleri (`TopicEndpoint`) tanımlanır.

## ⚠️ Kritik Nokta: Kafka Rider Tek Başına Yeterli Değil

MassTransit'in Kafka Rider'ı, **temel bus altyapısının (`IBus`) yanında**
çalışır, yerine geçmez. Sadece `x.AddRider(...)` tanımlayıp temel bus'ı hiç
yapılandırmazsanız, çalışma zamanında şu hatayı alırsınız:

```
Unable to resolve service for type 'MassTransit.IBus' while attempting to
activate 'MassTransit.DependencyInjection.ScopedBusContextProvider`1[MassTransit.IBus]'
```

**Çözüm:** `AddDistributedMessaging()` içinde, gerçek bir transport (RabbitMQ vb.)
gerekmeden, hafif bir **in-memory bus** ile temel `IBus`'ı da yapılandırıyoruz:

```csharp
services.AddMassTransit(x =>
{
    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
    x.AddRider(rider => { /* ... */ });
});
```

`ConfigureEndpoints(context)`, sadece **üst seviye** (`x.AddConsumer<T>()`
ile, Rider dışında) kaydedilmiş consumer'ları in-memory bus'a bağlar —
`rider.AddConsumer<T>()` ile Rider'a kaydedilenleri etkilemez. Bu satır sadece
MassTransit'in iç mekanizmasının (ConsumeContext, scoped filter'lar) ihtiyaç
duyduğu `IBus`'ın var olmasını sağlar.

## Kullanım

**Sadece Producer olan bir serviste** (örn. Order.Api):

```csharp
builder.Services.AddDistributedMessaging(builder.Configuration,
    configureRider: rider => rider.AddProducer<string, OrderCreatedEvent>(KafkaTopics.OrderCreated));
```

```csharp
app.MapPost("/orders", async (ITopicProducer<string, OrderCreatedEvent> producer) =>
{
    var evt = new OrderCreatedEvent(...);
    await producer.Produce(evt.PartitionKey, evt);   // 1. parametre = Kafka Message Key
    return Results.Accepted(...);
});
```

**Consumer olan bir serviste** (örn. Payment.Api) — retry/DLQ Polly ile
consumer'ın kendi içinde yapılır (bkz. "Dead Letter Queue" bölümü):

```csharp
builder.Services.AddDistributedMessaging(builder.Configuration,
    configureRider: rider =>
    {
        rider.AddConsumer<OrderCreatedConsumer>();
        rider.AddProducer<string, OrderCreatedDeadLetterMessage>(KafkaTopics.OrderCreatedDeadLetter);
    },
    configureTopics: (context, k) => k.TopicEndpoint<string, OrderCreatedEvent>(
        KafkaTopics.OrderCreated, "payment-service-group",
        e => e.ConfigureConsumer<OrderCreatedConsumer>(context)));
```

```csharp
public class OrderCreatedConsumer(
    ILogger<OrderCreatedConsumer> logger,
    ITopicProducer<string, OrderCreatedDeadLetterMessage> deadLetterProducer) : IConsumer<OrderCreatedEvent>
{
    public Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        logger.LogInformation("OrderId={OrderId}", context.Message.OrderId);
        return Task.CompletedTask;
    }
}
```

## İlk Somut Senaryo: `OrderCreatedEvent`

```
Order.Api (Producer)
  --Publish/Produce--> Kafka topic: "order-created" (Key = OrderId)
                              |
              +---------------+---------------+
              |                               |
     Payment.Api (Consumer)          Inventory.Api (Consumer)
     group: payment-service-group    group: inventory-service-group
```

- **Event, Command değil:** Order.Api, `OrderCreatedEvent`'i kime gittiğini
  bilmeden/umursamadan yayınlar (Publish semantiği — bkz. `IIntegrationEvent`).
  Payment.Api ve Inventory.Api birbirinden habersiz, bağımsız consumer'lardır.
- **Farklı consumer group'lar:** Kafka'da her tüketici grubu, aynı topic'in
  **kendi kopyasını** alır — Payment.Api'nin mesajı işlemesi, Inventory.Api'nin
  de işlemesini engellemez (ikisi de aynı mesajı görür).
- **Partition Key = OrderId:** `OrderCreatedEvent.PartitionKey` (ve
  `producer.Produce(evt.PartitionKey, evt)` çağrısındaki ilk parametre),
  aynı siparişe ait ileride eklenecek event'lerin (örn. `OrderCancelledEvent`)
  her zaman aynı Kafka partition'ına düşmesini — dolayısıyla sıralı
  işlenmesini — garanti eder.
- **Retry + DLQ:** Consumer'ın kendi içinde **Polly** (`ResiliencePipeline`)
  ile yapılır — `Consume()` içinde geçici bir hata oluşursa (örn. DB'ye
  erişilemedi), mesaj 3 kez, 5'er saniye arayla yeniden denenir; hepsi
  başarısız olursa `order-created-dlq` topic'ine yazılır (bkz. "Dead Letter
  Queue" bölümü).

## Doğrulama

```bash
# Order.Api'ye bir sipariş "oluşturma" isteği gönderin (dışarıdan payload ile).
# "orderId" OPSİYONELDİR — vermezseniz otomatik üretilir; Partition Key testini
# elle kontrol etmek isterseniz (aynı OrderId ile aynı partition'a düştüğünü
# görmek için) açıkça belirtebilirsiniz:
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"orderId": "11111111-1111-1111-1111-111111111111", "customerId": "musteri-123", "totalAmount": 349.90}'

# Cevap: {"orderId": "...", "published": true, "topic": "order-created"}
```

Ardından:
- **Kafka UI'da** (`http://localhost:8082`) `order-created` topic'ini açıp
  mesajın gerçekten yazıldığını görün.
- **Payment.Api ve Inventory.Api konsollarında** şu satırları arayın:
  `🟢 [Payment.Api] EVENT ALINDI — ...` ve `🟣 [Inventory.Api] EVENT ALINDI — ...`
  — **her ikisinin de** aynı OrderId için tetiklendiğini görmelisiniz
  (Publish/Subscribe kanıtı).

## ⚠️ Partition Key'i Gerçekten Test Etmek: Partition Sayısı

Kafka'da otomatik oluşturulan topic'ler **varsayılan olarak 1 partition**la
oluşur — tek partition varsa, Partition Key ne olursa olsun TÜM mesajlar
zorunlu olarak aynı (tek) partition'a gider; bu durumda key'in bir etkisini
göremezsiniz.

**`docker-compose.infra.yml`'e `KAFKA_NUM_PARTITIONS: "3"` eklendi** — ama bu
sadece **bundan sonra** otomatik oluşturulacak yeni topic'leri etkiler,
zaten var olan bir topic'in partition sayısını **geriye dönük değiştirmez**.
Eğer `order-created` topic'i daha önce (bu ayardan önce) zaten oluştuysa,
mevcut partition sayısını elle artırmanız gerekir:

```bash
docker exec neominal-kafka /opt/kafka/bin/kafka-topics.sh \
  --bootstrap-server localhost:9092 \
  --alter --topic order-created --partitions 3
```

**Doğrulama:**
```bash
docker exec neominal-kafka /opt/kafka/bin/kafka-topics.sh \
  --bootstrap-server localhost:9092 --describe --topic order-created
```
Çıktıda `PartitionCount: 3` görmelisiniz. Ardından farklı `/submit-order`
istekleri (farklı OrderId'ler) atıp Kafka UI'da hangi partition'a düştüklerini
gözlemleyerek Partition Key'in gerçekten çalıştığını doğrulayabilirsiniz.

> **Not:** Kafka'da partition sayısı sadece **artırılabilir**, asla
> azaltılamaz (Kafka'nın temel bir kısıtlamasıdır).

## Bilinen Sınırlamalar / Sonraki Adımlar

- ~~**Dead Letter Queue (DLQ)** henüz implemente edilmedi~~ ✅ Tamamlandı — bkz. ilgili bölüm.
- ~~Gerçek iş mantığı (ödeme talebi oluşturma, stok rezervasyonu) henüz yok~~
  ✅ Saga Pattern demosunda SİMÜLE edilmeye başlandı — bkz. aşağıdaki bölüm.
- ~~`Command` (Send()) örneği henüz yok~~ ✅ Tamamlandı — bkz. aşağıdaki bölüm.
  **Modül 3'ün tüm alt konuları tamamlandı.**
- ~~**Saga Pattern** henüz implemente edilmedi~~ ✅ Tamamlandı — bkz. aşağıdaki bölüm.

---

## Saga Pattern (Choreography)

**Ayrı, izole bir demo:** `POST /submit-order-saga` — mevcut `/submit-order`
ve `/submit-order-outbox`'a hiç dokunmadan, kendi topic'leri ve kendi event
zinciriyle çalışır. Merkezi bir orkestratör **yoktur** — her servis bir
öncekinin event'ini dinler, kendi işini yapar ve kendi event'ini yayınlar
("Choreography" — orkestrasyonun tersi).

### Akış

```
1) POST /submit-order-saga
   Order.Api: Order oluşturulur (Status: AwaitingInventory)
              -> "OrderSagaStarted" event PUBLISH edilir

2) Inventory.Api (dinler: OrderSagaStarted)
   Stok rezerve edilmeye çalışılır:
   ├─ BAŞARILI  -> "InventoryReserved" event PUBLISH edilir
   └─ BAŞARISIZ -> "InventoryReservationFailed" event PUBLISH edilir

3) Payment.Api (dinler: InventoryReserved)
   Ödeme alınmaya çalışılır:
   ├─ BAŞARILI  -> "PaymentCompleted" event PUBLISH edilir
   └─ BAŞARISIZ -> "PaymentFailed" event PUBLISH edilir

4a) Order.Api (dinler: PaymentCompleted)
    Order.Status = Confirmed  ✅ SAGA BAŞARIYLA TAMAMLANDI

4b) Order.Api (dinler: PaymentFailed)
    Order.Status = Cancelled
    -> "ReleaseInventoryCommand" SEND edilir (COMPENSATING ADIM)

5) Inventory.Api (dinler: ReleaseInventoryCommand)
   Az önce rezerve edilen stok GERİ BIRAKILIR (telafi tamamlandı)

2b) Order.Api (dinler: InventoryReservationFailed)
    Order.Status = Cancelled  ❌ SAGA BAŞARISIZ
    (Payment hiç tetiklenmedi, henüz bir şey rezerve edilmediği için
    compensation'a GEREK YOKTUR)
```

### Neden İki Farklı Başarısızlık Senaryosunda Compensation Farklı Davranıyor?

| Başarısızlık noktası | Compensation gerekir mi? | Neden |
|---|---|---|
| Stok rezervasyonu (adım 2) | ❌ Hayır | Henüz hiçbir şey rezerve/tahsis edilmedi — geri alınacak bir şey yok |
| Ödeme (adım 3) | ✅ Evet | Stok ZATEN rezerve edilmişti (adım 2 başarılıydı) — bu rezervasyonun geri bırakılması gerekir |

Bu, Saga Pattern'in temel mantığıdır: **her adımın kendi telafi işlemi
vardır, ve sadece GERÇEKTEN TAMAMLANMIŞ adımlar telafi edilir.**

### 3 Test Senaryosu

`customerId` alanındaki özel değerler senaryoyu tetikler (Modül 3'teki
`"FAIL"` konvansiyonunun devamı, farklı topic'lerde çakışmasın diye farklı
isimlerle):

| `customerId` | Sonuç | Durum geçişi |
|---|---|---|
| normal bir değer (örn. `"musteri-123"`) | ✅ Tam başarı | `Created → AwaitingInventory → Confirmed` |
| `"FAIL_INVENTORY"` | ❌ Stok yok (compensation YOK) | `Created → AwaitingInventory → Cancelled` |
| `"FAIL_PAYMENT"` | ❌ Ödeme reddi + **COMPENSATION** | `Created → AwaitingInventory → Cancelled` (+ Inventory.Api'de stok iade logu) |

### Kafka Topic'leri

`order-saga-started`, `inventory-reserved`, `inventory-reservation-failed`,
`payment-completed`, `payment-failed`, `release-inventory-command`

### Doğrulama

```bash
# Senaryo 1: Tam başarı
curl -X POST http://localhost:5001/submit-order-saga \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 250}'
# ~birkaç saniye sonra:
curl http://localhost:5001/orders/<orderId>   # Status: "Confirmed" olmalı

# Senaryo 2: Stok yok (compensation YOK)
curl -X POST http://localhost:5001/submit-order-saga \
  -H "Content-Type: application/json" \
  -d '{"customerId": "FAIL_INVENTORY", "totalAmount": 250}'
curl http://localhost:5001/orders/<orderId>   # Status: "Cancelled" olmalı

# Senaryo 3: Ödeme reddi + COMPENSATION
curl -X POST http://localhost:5001/submit-order-saga \
  -H "Content-Type: application/json" \
  -d '{"customerId": "FAIL_PAYMENT", "totalAmount": 250}'
curl http://localhost:5001/orders/<orderId>   # Status: "Cancelled" olmalı
```

Her senaryoda **Order.Api, Inventory.Api, Payment.Api konsollarını** takip
edin — her adımda 🟢/🔴/✅/↩️ işaretli, hangi servisin ne yaptığını
gösteren satırlar göreceksiniz.

### Kod Yapısı

| Servis | Yeni Dosyalar |
|---|---|
| Order.Api | `Consumers/PaymentCompletedConsumer.cs`, `PaymentFailedConsumer.cs` (+compensation tetikler), `InventoryReservationFailedConsumer.cs` |
| Order.Application | `Abstractions/ISagaEventPublisher.cs`, `Commands/StartOrderSagaCommand.cs` (+Validator) |
| Order.Infrastructure | `Messaging/KafkaSagaEventPublisher.cs` |
| Inventory.Api | `Consumers/OrderSagaStartedConsumer.cs` (rezervasyon simülasyonu), `ReleaseInventoryCommandConsumer.cs` (compensation alıcısı) |
| Payment.Api | `Consumers/InventoryReservedConsumer.cs` (ödeme simülasyonu) |

### Bilinen Sınırlamalar

- Stok/ödeme işlemleri **simüle edilmiştir** — gerçek bir stok tablosu veya
  ödeme entegrasyonu yoktur.
- Bu bir **Choreography** saga'dır (merkezi orkestratör yok). Karmaşık
  saga'larda genelde **Orchestration** (merkezi bir "Saga orchestrator"
  servisi) tercih edilir — bu proje eğitim amaçlı daha basit olan
  Choreography'yi seçti.
- Idempotency (aynı event'in 2 kez işlenmesi durumunda güvenlik) henüz
  eklenmedi — bu, Modül 5'in konusudur.

---

## Command (Send()) Örneği: `ProcessPaymentCommand`

Şu ana kadarki tüm senaryo **Event** (Publish) tabanlıydı. Şimdi **Command**
(Send) semantiğini somutlaştıran ikinci bir mesaj eklendi.

### Event ile Command Arasındaki Somut Fark

| | **Event** — `OrderCreatedEvent` | **Command** — `ProcessPaymentCommand` |
|---|---|---|
| Topic | `order-created` | `process-payment-command` |
| Anlamı | "Bir şey **oldu**" (olgu bildirimi) | "Şunu **yap**" (talimat) |
| Kim dinler? | Payment.Api **VE** Inventory.Api (bağımsız, N consumer) | **SADECE** Payment.Api (tek, bilinen alıcı) |
| Üretici ne bilir? | Kimin dinlediğini bilmez/umursamaz | Payment.Api'nin işlemesini **bekleyerek** gönderir |
| Consumer group | `payment-service-group`, `inventory-service-group` (ayrı ayrı) | `payment-service-commands-group` (tek) |

> **Not:** Kafka'da RabbitMQ'daki gibi gerçek bir "queue" (point-to-point)
> kavramı yoktur — bu yüzden Command/Event ayrımı bir API farkıyla değil
> (`Send()` diye ayrı bir Kafka metodu yok), bir **konvansiyonla** temsil
> edilir: Command topic'ini sadece bir servis dinler.

### Akış

```
POST /submit-order
  ├── 1) OrderCreatedEvent  -> "order-created" topic          -> Payment.Api + Inventory.Api (ikisi de bağımsız dinler)
  └── 2) ProcessPaymentCommand -> "process-payment-command" topic -> SADECE Payment.Api dinler
```

### Doğrulama

```bash
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 349.90}'

# Cevap:
# {
#   "orderId": "...",
#   "eventPublished": true, "eventTopic": "order-created",
#   "commandSent": true, "commandTopic": "process-payment-command"
# }
```

Konsollarda görmeniz gerekenler:
- **Payment.Api:** hem `🟢 EVENT ALINDI` (OrderCreatedEvent'ten) hem
  `🟡 COMMAND ALINDI` (ProcessPaymentCommand'dan) — **iki farklı** mesajı da alır.
- **Inventory.Api:** SADECE `🟣 EVENT ALINDI` — `COMMAND ALINDI` asla görülmez,
  çünkü Inventory.Api "process-payment-command" topic'ini hiç dinlemez.

Bu, Command/Event ayrımının en somut kanıtıdır: aynı `/submit-order` isteği
iki farklı mesaj tipi üretir, biri (Event) çoklu bağımsız tüketiciye, diğeri
(Command) tek, bilinen bir alıcıya gider.

---

## Dead Letter Queue (DLQ)

Kafka'da RabbitMQ'daki gibi **broker seviyesinde native bir DLQ yoktur** —
bu yüzden "tüm retry denemeleri tükendikten sonra başarısız olan mesaj" ayrı,
sıradan bir Kafka topic'ine (`order-created-dlq`) PRODUCE edilerek dead-letter
deseni elle uygulanır.

### ⚠️ Neden Polly (Elle Yazılmış Döngü veya MassTransit'in UseMessageRetry/Fault{T}'i Değil)?

İlk iki denemede sırasıyla `UseMessageRetry`+`IConsumer<Fault<T>>` (MassTransit'in
yerleşik mekanizması) ve ardından elle yazılmış bir `for` döngüsü denendi.
İkisi de sorunluydu:

- **`Fault<T>` yaklaşımı:** Mesajlar MassTransit'in **temel bus'ı** (in-memory,
  kalıcılığı yok) üzerinden yayınlanıyor; test sırasında güvenilir iletilmedi.
- **Elle yazılmış `for` döngüsü:** Çalışırdı ama **best practice değil** —
  jitter/backoff stratejisi yok, tekrar kullanılamaz, test edilmesi zor,
  kendi retry mantığımızı yeniden icat etmek anlamına gelir.

**Çözüm: Polly.** Bu proje zaten Modül 2'de (`BuildingBlocks.Resilience`) HTTP
çağrıları için Polly kullanıyor — burada da **aynı, test edilmiş, jitter/backoff
destekli** kütüphane kullanılıyor. DLQ üretimi ise (retry'lar tükenince)
hâlâ doğrudan burada, Kafka'ya produce ederek yapılıyor — bu kısım zaten
sorunsuzdu, sadece RETRY kısmı Polly'ye devredildi.

```csharp
private readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(5),
        BackoffType = DelayBackoffType.Constant,
        OnRetry = args => { logger.LogWarning(...); return ValueTask.CompletedTask; }
    })
    .Build();

public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
{
    try
    {
        await _retryPipeline.ExecuteAsync(static (msg, _) => { ProcessMessage(msg); return ValueTask.CompletedTask; },
            context.Message, context.CancellationToken);
    }
    catch (Exception ex)
    {
        // Polly TÜM denemeleri tükettiğinde son exception'ı yeniden fırlatır.
        await SendToDeadLetterAsync(context.Message, ex);
    }
}
```

### Akış

```
OrderCreatedConsumer.Consume() çağrılır
  -> Polly ResiliencePipeline.ExecuteAsync(...) — 3 deneme, 5sn sabit aralık
       (her retry'da OnRetry callback'i loglar)
  -> 3 deneme de başarısız oldu -> Polly son exception'ı fırlatır
  -> catch bloğunda yakalanır -> orijinal mesaj + hata sebebi
     "order-created-dlq" topic'ine PRODUCE edilir
```

### Test Etmek İçin: Bilinçli Hata Tetikleme

Her iki consumer'da da bir **test kancası** var: `CustomerId` alanı tam
olarak `"FAIL"` gönderilirse, consumer bilinçli olarak hata fırlatır:

```bash
curl -X POST http://localhost:5001/submit-order \
  -H "Content-Type: application/json" \
  -d '{"customerId": "FAIL", "totalAmount": 100}'
```

**Beklenen davranış (yaklaşık 15 saniye içinde tamamlanır: ilk deneme + 3
retry, aralarında 3 kez 5sn bekleme):**
1. Payment.Api ve Inventory.Api konsollarında sırasıyla:
   `OrderCreatedEvent işlenemedi (deneme 1/3)`, `(deneme 2/3)`, `(deneme 3/3)`
   uyarı satırlarını görürsünüz.
2. Son denemeden hemen sonra: `🔴 [Payment.Api] DEAD LETTER — ...` ve
   `🔴 [Inventory.Api] DEAD LETTER — ...` satırlarını görürsünüz.
3. **Kafka UI'da** (`http://localhost:8082`) `order-created-dlq` topic'ini
   açın — orijinal mesaj + `FailureReason` + `ConsumerName` alanlarıyla
   **2 ayrı DLQ mesajı** (biri Payment.Api'den, biri Inventory.Api'den)
   görmelisiniz.

### Normal (Hatasız) Senaryo

`CustomerId` "FAIL" değilse, ilk denemede başarılı olur — retry/DLQ hiç
devreye girmez.
