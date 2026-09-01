# Payment.Api

**Modül:** Modül 1 — Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik
**Konu:** Kestrel Self-Hosted Yapılandırması + Health Check Zinciri

## Servis Bilgisi

| | |
|---|---|
| Katman | `*.Api` (Kestrel self-hosted giriş noktası) |
| Host port | `5002` (docker-compose) |
| Veritabanı | PostgreSQL — `payment_db` |
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
curl http://localhost:5002/health/live
curl http://localhost:5002/health/ready
curl http://localhost:5002/health
curl http://localhost:5002/metrics   # bkz. BuildingBlocks.Observability.md
```

## İlgili Dokümanlar

- `docs/BuildingBlocks.Common.md` — Global Exception Handling, HealthCheckResponseWriter
- `docs/BuildingBlocks.Observability.md` — Serilog/OpenTelemetry/Prometheus zinciri

## Bilinen Sınırlamalar / Sonraki Adımlar

- Kafka için henüz bir health check eklenmedi (Modül 3 — MassTransit entegrasyonu
  sırasında `AddKafka(...)` eklenecektir).
- Vault/Consul/Keycloak entegrasyonları henüz aktif değil (Modül 2).

## Modül 2 Güncellemesi: Vault + Consul

- **Secret Management (Vault):** `ConnectionStrings:PaymentDb` artık appsettings.json yerine HashiCorp Vault'tan okunuyor (bkz. `docs/BuildingBlocks.Resilience.md`). Vault'a ulaşılamazsa appsettings.json'daki değere düşülür.
- **Service Discovery (Consul):** Bu servis artık ayağa kalktığında kendini Consul'a kaydediyor, kapanırken kaydını siliyor (bkz. `docs/BuildingBlocks.Security.md`). Health check olarak mevcut `/health/ready` endpoint'i kullanılıyor.
- **JWT doğrulama YOK:** Mimari karar gereği bu servis Keycloak/JWT doğrulaması yapmaz — bu sorumluluk tamamen ApiGateway'e aittir (bkz. `docs/ApiGateway.md`).

## Modül 3 Güncellemesi: MassTransit + Kafka Consumer

`Consumers/OrderCreatedConsumer.cs` — "order-created" topic'ini
`payment-service-group` tüketici grubuyla dinler, alınan event'i loglar.
Detaylar için bkz. `docs/BuildingBlocks.Messaging.md`.

## Modül 3 Güncellemesi: Command Consumer

`Consumers/ProcessPaymentCommandConsumer.cs` — "process-payment-command"
topic'ini `payment-service-commands-group` ile dinler. Bu bir **Command**
consumer'ıdır (Event consumer'ından farklı) — bu topic'i SADECE Payment.Api
dinler, Inventory.Api dinlemez. Detaylar için bkz. `docs/BuildingBlocks.Messaging.md`.

## Modül 4 Güncellemesi: Saga Pattern Katılımcısı

`Consumers/InventoryReservedConsumer.cs` — Saga'nın ödeme adımını simüle
eder (`CustomerId: "FAIL_PAYMENT"` ile ödeme reddi test edilebilir).
Detaylar için bkz. `docs/BuildingBlocks.Messaging.md`.

## Modül 4 Güncellemesi: Saga Pattern (Orchestration) Katılımcısı

`Consumers/ChargePaymentCommandConsumer.cs` — Saga.Api'den (RabbitMQ) gelen
ödeme talebini işler, sonucu doğrudan Saga.Api'ye geri gönderir
(`CustomerId: "FAIL_PAYMENT"` ile test edilebilir). Detaylar için bkz. `docs/Saga.Api.md`.
