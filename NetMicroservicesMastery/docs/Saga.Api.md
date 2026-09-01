# Saga.Api — Saga Pattern (Orchestration)

**Modül:** Modül 4 — Finansal Uygulamalarda Veri Tutarlılığı
**Konu:** Saga Pattern'in ORCHESTRATION versiyonu (Choreography'nin karşılığı)

## Bu Servis Ne İşe Yarar?

`docs/BuildingBlocks.Messaging.md`'deki Saga (Choreography) örneğinde merkezi
bir "beyin" yoktu — her servis bir öncekinin event'ini dinleyip kendi işini
yapıyordu. Bu servis ise **tam tersi**: TÜM akış mantığı burada, tek bir
`OrderSagaStateMachine` sınıfında, açıkça tanımlıdır. Order.Api, Payment.Api,
Inventory.Api sadece "komut al, işi yap, sonucu bildir" yapar — akışın
kendisini bilmezler.

## Neden Ayrı Bir Servis (Order.Api İçinde Değil)?

- Bu bir eğitim projesi olsa da, gerçek dünyada orkestratör genelde birden
  fazla bounded context'i (Order/Payment/Inventory) koordine ettiği için,
  bunlardan birinin (Order.Api) içine gömülmesi "Order.Api'nin diğerleri
  üzerinde özel yetkisi var" asimetrisini yaratır.
- Ayrı bir servis olması, saga mantığının test edilmesini, izlenmesini ve
  (ileride) ölçeklenmesini kolaylaştırır.

## Neden RabbitMQ (Kafka Değil)?

MassTransit'in **Saga/State Machine** desteği, temel bus'ın (RabbitMQ, Azure
Service Bus tarzı) receive endpoint modeli için tasarlanmıştır. Kafka
Rider'da bu destek native değildir/güvenilir değildir — bu proje boyunca
Kafka Rider ile (Outbox, Fault&lt;T&gt;) yaşadığımız entegrasyon sorunlarının
aynısıyla karşılaşma riskini almamak için, bu ÖZEL senaryoda RabbitMQ (zaten
altyapıda hazır, `docker-compose.infra.yml`) tercih edildi. Choreography
örneği Kafka'da çalışmaya devam ediyor — ikisi tamamen izole.

## Mimari

```
Order.Api --(StartOrderSagaCommand, RabbitMQ Send)--> Saga.Api
                                                           |
                                          OrderSagaStateMachine (State Machine)
                                                           |
                    +--------------------------------------+--------------------------------------+
                    |                                                                               |
                    v                                                                               v
      Inventory.Api (ReserveInventoryCommand)                                      Payment.Api (ChargePaymentCommand)
      --(Succeeded/Rejected)--> Saga.Api                                           --(Charged/Failed)--> Saga.Api
                    ^                                                                               |
                    |                                                                               |
                    +----------- RevertInventoryReservationCommand (COMPENSATION) --------------------+
                                                           |
                                                           v
                                    Order.Api <--(OrderSagaCompletedEvent / OrderSagaFailedEvent)--
```

Saga.Api'nin **tek bir kuyruğu** vardır (`SagaQueues.OrderSagaOrchestrator`)
— hem başlatma komutu hem de tüm katılımcılardan gelen reply event'leri
buraya toplanır (bir saga'nın ilgili tüm mesajlarının TEK bir endpoint'te
toplanması, MassTransit saga'larının geleneksel tasarımıdır).

## Akış (Adım Adım)

```
1) Order.Api: POST /submit-order-saga-orchestrator
   -> Order oluşturulur (Status: AwaitingInventory)
   -> StartOrderSagaCommand, RabbitMQ ile Saga.Api'ye SEND edilir

2) Saga.Api (Initially): OrderSagaState oluşturulur (State: AwaitingInventory)
   -> ReserveInventoryCommand, Inventory.Api'ye SEND edilir

3) Inventory.Api: rezervasyonu simüle eder
   ├─ BAŞARILI  -> InventoryReservationSucceededEvent -> Saga.Api'ye SEND
   └─ BAŞARISIZ -> InventoryReservationRejectedEvent  -> Saga.Api'ye SEND

4a) Saga.Api (During AwaitingInventory, Succeeded):
    -> ChargePaymentCommand, Payment.Api'ye SEND edilir
    -> State: AwaitingPayment

4b) Saga.Api (During AwaitingInventory, Rejected):
    -> OrderSagaFailedEvent, Order.Api'ye SEND edilir (compensation YOK)
    -> State: Failed (Finalize)

5) Payment.Api: ödemeyi simüle eder
   ├─ BAŞARILI  -> PaymentChargedEvent      -> Saga.Api'ye SEND
   └─ BAŞARISIZ -> PaymentChargeFailedEvent -> Saga.Api'ye SEND

6a) Saga.Api (During AwaitingPayment, Charged):
    -> OrderSagaCompletedEvent, Order.Api'ye SEND edilir
    -> State: Completed (Finalize)  ✅ BAŞARI

6b) Saga.Api (During AwaitingPayment, Failed):
    -> RevertInventoryReservationCommand, Inventory.Api'ye SEND (COMPENSATION)
    -> OrderSagaFailedEvent, Order.Api'ye SEND edilir
    -> State: Failed (Finalize)  ❌ BAŞARISIZ

7) Order.Api: OrderSagaCompletedEvent/OrderSagaFailedEvent'i dinler,
   Order.Status günceller (Confirmed/Cancelled).
```

## `OrderSagaState` — Saga'nın Anlık Durumu

MassTransit'in `SagaStateMachineInstance` arayüzünü implemente eder.
`CorrelationId` = `OrderId`; `CurrentState` alanı State Machine tarafından
yönetilir (`InstanceState(x => x.CurrentState)`). PostgreSQL'in `xmin`
sistem kolonu, eşzamanlı güncellemeler için optimistic concurrency token'ı
olarak kullanılır (shadow property üzerinden — `entity.Property<uint>("xmin")...IsRowVersion()`).

### ⚠️ ÖNEMLİ: Saga Tamamlanınca Bu Satır SİLİNİR

MassTransit, bir saga `.Finalize()` ile "Final" bir state'e (Completed/Failed)
ulaştığında, `OrderSagaState` satırını repository'den **otomatik olarak
siler** — bu, kütüphanenin kendi housekeeping davranışıdır (tamamlanmış bir
saga'nın canlı durumunu tutmaya gerek olmadığını varsayar). Yani:

- **Devam eden** bir saga için `GET /debug/sagas/{orderId}` → `OrderSagaState`'ten okur.
- **Tamamlanmış/başarısız** bir saga için → `OrderSagaState` satırı ARTIK YOK;
  "son bilinen durum" `OrderSagaStateHistory`'deki (asla silinmez) SON
  kayıttan türetilir (bkz. aşağıdaki bölüm ve `Program.cs`'teki endpoint kodu).

## `OrderSagaStateHistory` — "Event Streaming" İzleme

`OrderSagaState` sadece **o anki** durumu tutar (üzerine yazılır, hatta
tamamlanınca SİLİNİR — yukarıya bakın). Bu tablo ise HER durum geçişini
ayrı, immutable bir satır olarak **append-only** biriktirir (`FromState`,
`ToState`, `TriggeredByEvent`, `OccurredOnUtc`, ve — `OrderSagaState`
silindikten sonra da kalıcı olsun diye denormalize edilmiş `CustomerId`/
`TotalAmount`) — "buraya nasıl geldik?" sorusunu cevaplar, denetim/hata
ayıklama için kullanılır.

> **Not:** Bu kayıt, `OrderSagaState` güncellemesiyle AYNI transaction'da
> DEĞİLDİR (basitlik için) — sadece izleme amaçlıdır.

## 3 Test Senaryosu

Choreography örneğiyle AYNI konvansiyon (`customerId` tetikleyicisi):

| `customerId` | Sonuç | Saga.Api State geçişi |
|---|---|---|
| normal bir değer | ✅ Tam başarı | `AwaitingInventory → AwaitingPayment → Completed` |
| `"FAIL_INVENTORY"` | ❌ Stok yok (compensation YOK) | `AwaitingInventory → Failed` |
| `"FAIL_PAYMENT"` | ❌ Ödeme reddi + **COMPENSATION** | `AwaitingInventory → AwaitingPayment → Failed` |

## Doğrulama

```bash
# Senaryo 1: Tam başarı
curl -X POST http://localhost:5001/submit-order-saga-orchestrator \
  -H "Content-Type: application/json" \
  -d '{"customerId": "musteri-123", "totalAmount": 250}'

# Saga.Api'de canlı durumu izleyin (birkaç saniye içinde State değişir):
curl http://localhost:5004/debug/sagas/<orderId>

# Order.Api'de nihai sonucu doğrulayın:
curl http://localhost:5001/orders/<orderId>   # Status: "Confirmed" olmalı
```

`GET /debug/sagas/{orderId}` (Saga.Api, port 5004) cevabı:
```json
{
  "orderId": "...",
  "currentState": "Completed",
  "customerId": "musteri-123",
  "totalAmount": 250,
  "history": [
    { "fromState": "Initial", "toState": "AwaitingInventory", "triggeredByEvent": "StartOrderSagaCommand", "occurredOnUtc": "..." },
    { "fromState": "AwaitingInventory", "toState": "AwaitingPayment", "triggeredByEvent": "InventoryReservationSucceededEvent", "occurredOnUtc": "..." },
    { "fromState": "AwaitingPayment", "toState": "Completed", "triggeredByEvent": "PaymentChargedEvent", "occurredOnUtc": "..." }
  ]
}
```

Her senaryoda **4 servisin konsollarını** (Order.Api, Saga.Api, Inventory.Api,
Payment.Api) takip edin — 🟢/🔴/✅/↩️ işaretli satırlarla hangi servisin
ne yaptığını görebilirsiniz.

## Veritabanı Şeması: EnsureCreated

Diğer servislerdeki gibi (bkz. `docs/Order.Infrastructure.md`), bu servis de
`EnsureCreated()` kullanır — Migrations değil. `saga_db` boşsa `Program.cs`
başlarken `OrderSagas` ve `OrderSagaHistory` tablolarını otomatik oluşturur.
Model değiştikçe (yeni kolon eklenmesi gibi), aynı `order_db` sorununda
olduğu gibi `saga_db`'yi sıfırlamanız gerekebilir:

```bash
docker exec -it neominal-postgres psql -U neominal -c "DROP DATABASE saga_db;"
docker exec -it neominal-postgres psql -U neominal -c "CREATE DATABASE saga_db;"
```

## Bilinen Sınırlamalar

- Stok/ödeme işlemleri **simüle edilmiştir**.
- `OrderSagaStateHistory` yazımı, saga state güncellemesiyle atomik değildir
  (sadece izleme amaçlı).
- Idempotency (aynı mesajın 2 kez işlenmesi) henüz eklenmedi — Modül 5'in konusu.
- Bu servis şu an tek instance için tasarlandı; RabbitMQ'nun kendi mesaj
  teslim garantileri (at-least-once) sayesinde mesaj kaybı olmaz, ama
  birden fazla Saga.Api instance'ı çalıştırma senaryosu test edilmedi.
