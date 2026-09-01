# Order.Api

**Modül:** Modül 1 — Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik
**Konu:** Kestrel Self-Hosted Yapılandırması + Health Check Zinciri

## Servis Bilgisi

| | |
|---|---|
| Katman | `*.Api` (Kestrel self-hosted giriş noktası) |
| Host port | `5001` (docker-compose) |
| Veritabanı | PostgreSQL — `order_db` |
| Ortak katmanlar | `BuildingBlocks.Common`, `BuildingBlocks.Observability` (bkz. ilgili docs dosyaları) |

## Kestrel Yapılandırması

`Program.cs` içinde, IIS'siz self-hosted çalışan Kestrel için üretim bilinciyle
belirlenmiş limitler tanımlıdır:

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;   // 10 MB — DoS koruması
    options.Limits.MaxConcurrentConnections = 100;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30); // slow-loris koruması
    options.AddServerHeader = false;                          // "Server: Kestrel" header'ı kapalı
});
```

| Ayar | Değer | Neden |
|---|---|---|
| `MaxRequestBodySize` | 10 MB | Açık uçlu (sınırsız) body boyutu DoS riski taşır |
| `MaxConcurrentConnections` | 100 | Tek bir instance'ın aşırı bağlantı ile boğulmasını engeller |
| `KeepAliveTimeout` | 2 dk | Boşta bağlantıları makul bir sürede serbest bırakır |
| `RequestHeadersTimeout` | 30 sn | "Slow-loris" tipi saldırılara karşı koruma |
| `AddServerHeader` | `false` | Sunucu/versiyon bilgisi sızıntısını azaltır |

> **Not:** Bu değerler eğitim/demo amaçlı makul varsayılanlardır; gerçek üretim
> ortamında trafik profiline göre yeniden ayarlanmalıdır.

## Health Check Zinciri

`AspNetCore.HealthChecks.NpgSql` ve `AspNetCore.HealthChecks.Redis` paketleriyle
gerçek bağımlılık kontrolleri yapılır; sonuçlar `BuildingBlocks.Common`'daki ortak
`HealthCheckResponseWriter` ile tutarlı bir JSON formatında döndürülür.

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"])
    .AddRedis(redisConnectionString, name: "redis", tags: ["ready"]);
```

### Endpoint'ler

| Endpoint | Amaç | Kontrol Edilen |
|---|---|---|
| `GET /health/live` | **Liveness** — process ayakta mı? | Hiçbiri (`Predicate = _ => false`) — orkestratörün "restart et mi?" kararı için |
| `GET /health/ready` | **Readiness** — trafik almaya hazır mı? | `"ready"` etiketli tüm kontroller (Postgres + Redis) — orkestratörün "trafik gönder mi?" kararı için |
| `GET /health` | Genel/manuel inceleme | Tüm health check'lerin tam dökümü |

**Örnek `/health` cevabı:**

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.4,
  "checks": [
    { "name": "postgresql", "status": "Healthy", "durationMs": 8.1, "tags": ["ready"] },
    { "name": "redis", "status": "Healthy", "durationMs": 3.9, "tags": ["ready"] }
  ]
}
```

> **Neden liveness/readiness ayrımı?** Bir orkestratör (Kubernetes, Docker Swarm vb.)
> liveness başarısız olursa container'ı **yeniden başlatır**; readiness başarısız
> olursa sadece **trafiği kesip** container'ı ayakta bırakır (örn. veritabanı geçici
> olarak erişilemezken servisin gereksiz yere restart döngüsüne girmesini önler).

## Hızlı Doğrulama (docker-compose ayaktayken)

```bash
curl http://localhost:5001/health/live
curl http://localhost:5001/health/ready
curl http://localhost:5001/health
curl http://localhost:5001/metrics   # bkz. BuildingBlocks.Observability.md
```

## İlgili Dokümanlar

- `docs/BuildingBlocks.Common.md` — Global Exception Handling, HealthCheckResponseWriter
- `docs/BuildingBlocks.Observability.md` — Serilog/OpenTelemetry/Prometheus zinciri

## Bilinen Sınırlamalar / Sonraki Adımlar

- Kafka için henüz bir health check eklenmedi (Modül 3 — MassTransit entegrasyonu
  sırasında `AddKafka(...)` eklenecektir).
- Vault/Consul/Keycloak entegrasyonları henüz aktif değil (Modül 2).

## Modül 2 Güncellemesi: Vault + Consul

- **Secret Management (Vault):** `ConnectionStrings:OrderDb` artık appsettings.json yerine HashiCorp Vault'tan okunuyor (bkz. `docs/BuildingBlocks.Resilience.md`). Vault'a ulaşılamazsa appsettings.json'daki değere düşülür.
- **Service Discovery (Consul):** Bu servis artık ayağa kalktığında kendini Consul'a kaydediyor, kapanırken kaydını siliyor (bkz. `docs/BuildingBlocks.Security.md`). Health check olarak mevcut `/health/ready` endpoint'i kullanılıyor.
- **JWT doğrulama YOK:** Mimari karar gereği bu servis Keycloak/JWT doğrulaması yapmaz — bu sorumluluk tamamen ApiGateway'e aittir (bkz. `docs/ApiGateway.md`).

## Modül 2 Güncellemesi: Polly Demo Endpoint'i

`GET /test-resiliency` — Inventory.Api'ye Retry+CircuitBreaker+Fallback
pipeline'ı (`InventoryClient`) üzerinden istek atar. Detaylar için bkz.
`docs/BuildingBlocks.Resilience.md`.

## Modül 3 Güncellemesi: MassTransit + Kafka Producer

`POST /submit-order` — "order-created" topic'ine bir `OrderCreatedEvent` publish EDER, ayrıca "process-payment-command" topic'ine bir `ProcessPaymentCommand` SEND
eder (Partition Key = OrderId). Detaylar için bkz. `docs/BuildingBlocks.Messaging.md`.

## Modül 4 Güncellemesi: CQRS (MediatR)

`POST /submit-order` ve yeni `GET /orders/{orderId}` endpoint'leri artık
"ince" — iş mantığının tamamı MediatR handler'larına taşındı
(`CreateOrderCommand`, `GetOrderByIdQuery`). Girdi validasyonu
(`CreateOrderCommandValidator`) handler çalışmadan önce otomatik uygulanır.
Detaylar için bkz. `docs/Order.Application.md` ve `docs/Order.Infrastructure.md`.

## Modül 4 Güncellemesi: Outbox Pattern (Ayrı Endpoint)

`POST /submit-order-outbox` — `/submit-order`'dan BİLİNÇLİ olarak ayrı,
dedike bir Outbox Pattern demo endpoint'i (karışıklığı önlemek için).
`/submit-order` değişmedi, hâlâ doğrudan Kafka'ya yayınlıyor. `GET /debug/outbox`
ile outbox tablosunun durumu izlenebilir. Detaylar için bkz.
`docs/Order.Infrastructure.md`.

## Modül 4 Güncellemesi: Saga Pattern (Choreography)

`POST /submit-order-saga` — `/submit-order` ve `/submit-order-outbox`'tan
tamamen izole, dedike bir Saga Pattern demo endpoint'i. `Consumers/`
klasöründe saga sonuçlarını dinleyen 3 consumer var (`PaymentCompletedConsumer`,
`PaymentFailedConsumer` — compensation tetikler, `InventoryReservationFailedConsumer`).
Akış diyagramı için bkz. ana `README.md` ve `docs/BuildingBlocks.Messaging.md`.

## Modül 4 Güncellemesi: Saga Pattern (Orchestration)

`POST /submit-order-saga-orchestrator` — Choreography'deki `/submit-order-saga`
ile KARIŞTIRILMAMALIDIR. Bu, işi tamamen ayrı bir servise (**Saga.Api**)
devreden orkestrasyon versiyonudur; Order.Api sadece isteği alıp RabbitMQ ile
iletir, akış mantığının tamamı Saga.Api'dedir. Detaylar için bkz. `docs/Saga.Api.md`.
