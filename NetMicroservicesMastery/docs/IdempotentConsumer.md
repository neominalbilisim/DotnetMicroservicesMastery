# Idempotent Consumer

**Modül:** Modül 5 — Ölçeklenebilirlik ve Sağlamlık
**Konu:** Aynı mesajın (retry/redelivery nedeniyle) birden fazla kez gelmesi durumunda dahi yan etkilerin (side effect) SADECE BİR KEZ çalışmasını garanti etmek.

## Neden Gerekli?

Bir mesaj broker'ı (Kafka/RabbitMQ), belirli senaryolarda (consumer crash sonrası, ağ kesintisi, retry mekanizmaları) **aynı mesajı** ikinci kez teslim edebilir. Consumer'ın yan etkisi (`"ödeme al"`, `"stok azalt"`) mesaj her geldiğinde ÇALIŞIRSA, bu gerçek bir soruna yol açar:

- **Ödeme:** Aynı sipariş için müşteriden **iki kez** para çekilir.
- **Stok:** Aynı sipariş için stok **iki kez** düşülür (gerçekte tek sipariş var).

Idempotent Consumer deseni, "bu mesajı daha önce gördüm mü?" kontrolünü ekleyerek bunu önler.

## Nerede Implemente Edildi?

Bilinçli olarak **iki, en kritik** consumer'a uygulandı (Saga Orchestration akışında):

| Consumer | Servis | Neden Kritik |
|---|---|---|
| `ChargePaymentCommandConsumer` | Payment.Api | İki kez çalışırsa **müşteriden iki kez para çekilir** |
| `ReserveInventoryCommandConsumer` | Inventory.Api | İki kez çalışırsa **stok iki kez düşülür** |

Aynı desen, `IIdempotencyStore` soyutlaması üzerinden herhangi bir başka consumer'a da (Choreography'deki `OrderCreatedConsumer` dahil) kolayca eklenebilir.

## Mimari

```
BuildingBlocks.Messaging/Idempotency/IIdempotencyStore.cs   (paylaşılan arayüz)
        |
        +--> Payment.Infrastructure/Idempotency/PaymentIdempotencyStore.cs      (PaymentDbContext ile)
        +--> Inventory.Infrastructure/Idempotency/InventoryIdempotencyStore.cs  (InventoryDbContext ile)
```

```csharp
public interface IIdempotencyStore
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, CancellationToken ct = default);
    Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
}
```

Her servis kendi veritabanına (`ProcessedMessages` tablosu — `MessageId` birincil anahtar) yazan kendi implementasyonunu sağlar.

## Consumer İçindeki Kullanım

```csharp
public async Task Consume(ConsumeContext<ChargePaymentCommand> context)
{
    var messageId = context.MessageId;

    if (messageId is not null && await idempotencyStore.HasBeenProcessedAsync(messageId.Value, context.CancellationToken))
    {
        // Yan etki (ödeme alma) HİÇ ÇALIŞTIRILMADAN atlanır.
        return;
    }

    // ... asıl iş mantığı (ödeme alma / stok rezerve etme) ...

    if (messageId is not null)
    {
        await idempotencyStore.MarkAsProcessedAsync(messageId.Value, nameof(ChargePaymentCommandConsumer), context.CancellationToken);
    }
}
```

## Neden `MessageId` (Kendi `EventId`/`CommandId` Alanlarımız Değil)?

Mesaj sözleşmelerimizin bir kısmında (`OrderCreatedEvent.EventId`, `ProcessPaymentCommand`'ta yok) kendi ID alanlarımız var, ama **tutarlı bir şekilde her mesaj tipinde yok**. MassTransit'in **her mesaja otomatik atadığı** `ConsumeContext.MessageId` (transport seviyesinde, taşıma katmanının kendisi tarafından garanti edilen bir GUID) evrensel olarak kullanılabilir olduğu için tercih edildi — hangi mesaj tipini korumak istersek isteyelim, aynı mekanizma çalışır.

## Doğrulama

Her iki serviste de bir **kendi kendini test eden** debug endpoint'i var — aynı mesajı, **elle aynı `MessageId`'yi belirterek**, 2 kez kendi kuyruğuna gönderir:

```bash
# Payment.Api
curl -X POST http://localhost:5002/debug/test-idempotency

# Inventory.Api
curl -X POST http://localhost:5003/debug/test-idempotency
```

**Beklenen konsol çıktısı** (Payment.Api örneği):
```
🟢 [Payment.Api/SagaOrchestrator] ÖDEME ALINDI (simüle) — OrderId=...
⏭️ [Payment.Api/Idempotency] Mesaj DAHA ÖNCE işlendi, ödeme TEKRAR ALINMAYACAK — MessageId=..., OrderId=...
```

İkinci mesaj için **ödeme mantığı hiç çalışmaz** — sadece "atlandı" logu görürsünüz.

**Veritabanında doğrulamak isterseniz:**
```sql
SELECT * FROM "ProcessedMessages";
-- Sadece 1 satır olmalı (2 mesaj gönderilmesine rağmen).
```

## Veritabanı Şeması

Diğer servislerdeki gibi `EnsureCreated()` kullanılır. Payment.Api ve
Inventory.Api'de bu, **ilk kez** gerçek bir `EnsureCreated()` çağrısı
eklenmesine vesile oldu (daha önce bu iki serviste hiç yoktu — `Payments`/
`Inventorys` tabloları da bu değişiklikle birlikte ilk kez oluşacak).

> Eğer `payment_db`/`inventory_db` üzerinde daha önce bu tablolar farklı bir
> şekilde (veya hiç) oluşmuşsa, `order_db`/`saga_db` ile yaşadığımız aynı
> sorunu yaşayabilirsiniz — gerekirse veritabanlarını sıfırlayın:
> ```bash
> docker exec -it neominal-postgres psql -U neominal -c "DROP DATABASE payment_db; CREATE DATABASE payment_db;"
> docker exec -it neominal-postgres psql -U neominal -c "DROP DATABASE inventory_db; CREATE DATABASE inventory_db;"
> ```

## Bilinen Sınırlamalar

- `HasBeenProcessedAsync` kontrolü ile asıl iş mantığı + `MarkAsProcessedAsync`
  arasında (teorik olarak) küçük bir race condition penceresi vardır — aynı
  mesaj GERÇEKTEN aynı anda iki farklı consumer thread'i tarafından
  işlenirse (çok nadir), ikisi de "işlenmemiş" görüp aynı anda işleyebilir.
  `MessageId` üzerindeki birincil anahtar kısıtlaması, en azından **kaydın
  kendisinin** iki kez yazılmasını veritabanı seviyesinde engeller.
- Bu iki consumer dışındaki consumer'lara (örn. Choreography'deki
  `OrderCreatedConsumer`) henüz eklenmedi — aynı `IIdempotencyStore` deseni
  kolayca oraya da taşınabilir.
