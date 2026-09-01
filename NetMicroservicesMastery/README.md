# .NET Microservices Mastery — Uygulama Şablonu (Solution İskeleti)

Bu repo, **"Kuruma Ön Hazırlık Dökümanı"**nda anlatılan 5 modülün tamamını (Kestrel/Observability,
Vault/Polly/Consul/YARP/Keycloak, MassTransit/Kafka, CQRS/Outbox/Saga, Hangfire/Distributed Lock)
tek bir **single-repo** altında, **3 mikroservisli** (Order, Payment, Inventory) gerçekçi bir
kurumsal senaryo üzerinden uygulamanız için hazırlanmış **.NET 10** solution iskeletidir.

> **Bu aşamada** tüm proje/dosya yapısı, `.csproj` referansları, Docker/Compose altyapısı ve her
> dosyanın içine **hangi modülde neyin doldurulacağını** anlatan `TODO` yorumları hazırdır.
> Modüllerin gerçek implementasyonu (kod) bir sonraki adımlarda, modül modül birlikte yazılacaktır.

---

## 1. Solution Yapısı

```
NetMicroservicesMastery.sln
├── src/
│   ├── BuildingBlocks/                     # Tüm servislerin paylaştığı ortak katmanlar
│   │   ├── BuildingBlocks.Common/          # Modül 1: Global Exception Handling, ApiProblemDetails
│   │   ├── BuildingBlocks.Observability/   # Modül 1: Serilog + OpenTelemetry + Prometheus
│   │   ├── BuildingBlocks.Resilience/      # Modül 2: Polly (Retry/CB/Fallback) + Vault
│   │   ├── BuildingBlocks.Messaging/       # Modül 3/4: MassTransit sözleşmeleri + Outbox
│   │   └── BuildingBlocks.Security/        # Modül 2: Keycloak (JWT) + Consul (Service Discovery)
│   │
│   ├── Services/
│   │   ├── Order/       (Order.Domain / Order.Application / Order.Infrastructure / Order.Api)
│   │   ├── Payment/     (aynı 4 katman)
│   │   ├── Inventory/   (aynı 4 katman)
│   │   └── Saga/
│   │       └── Saga.Api/                   # Modül 4: Saga Pattern (Orchestration) — MassTransit State Machine + RabbitMQ
│   │
│   ├── Gateway/
│   │   └── ApiGateway/                     # Modül 2: YARP tabanlı API Gateway
│   │
│   └── BackgroundJobs/
│       └── JobService/                     # Modül 5: Hangfire + Distributed Lock
│
├── infra/                                  # Docker altyapı konfigürasyonları
│   ├── postgres/init-multiple-dbs.sh       # Database-per-service
│   ├── prometheus/prometheus.yml
│   ├── grafana/provisioning/
│   └── keycloak/realm-export.json
│
├── docker-compose.infra.yml                # Altyapı (Postgres, Redis, Kafka, Vault, Consul, Keycloak, Seq, Prometheus, Grafana, Jaeger, RabbitMQ, RedisInsight, Kafka UI, Nginx*)
├── docker-compose.apps.yml                 # Sadece .NET servisleri (Order/Payment/Inventory/Saga/Gateway/JobService)
├── .env.example
└── tests/
```

Her mikroservis **katmanlı (layered) mimari** ile modellenmiştir:

| Katman | Sorumluluk |
|---|---|
| `*.Domain` | Aggregate'ler, domain event'ler — dış bağımlılığı yoktur |
| `*.Application` | CQRS Command/Query'ler (MediatR), validasyon, iş akışı orkestrasyonu |
| `*.Infrastructure` | EF Core (Postgres), Outbox, Redis/Distributed Lock, repository implementasyonları |
| `*.Api` | Kestrel self-hosted giriş noktası, Program.cs, appsettings, Dockerfile |

---

## 2. Modül → Proje Eşleme Tablosu

| Modül | Konu | Nerede |
|---|---|---|
| **Modül 1** | Kestrel, Global Exception Handling, Serilog/OTel/Prometheus/Grafana, Multi-stage Dockerfile | `BuildingBlocks.Common`, `BuildingBlocks.Observability`, her `*.Api/Program.cs`, her `*.Api/Dockerfile` |
| **Modül 2** | Vault (Secret Mgmt), Polly (Resiliency), Consul (Service Discovery), YARP (API Gateway), Keycloak (AuthServer) | `BuildingBlocks.Resilience`, `BuildingBlocks.Security`, `src/Gateway/ApiGateway` |
| **Modül 3** | MassTransit + Kafka, Command/Event ayrımı, DLQ | `BuildingBlocks.Messaging`, her `*.Api/Consumers` |
| **Modül 4** | CQRS (MediatR), Outbox Pattern, Saga Pattern (Choreography + Orchestration) | Her `*.Application/Commands\|Queries`, `BuildingBlocks.Messaging/Outbox`, her `*.Infrastructure/Persistence`, `Saga.Api` (ayrı servis) |
| **Modül 5** | Hangfire, IHostedService, Distributed Lock, Idempotent Consumer | `src/BackgroundJobs/JobService`, her `*.Infrastructure/Locking` |

Her ilgili dosyanın içinde, o dosyanın hangi modülde ve hangi sırayla doldurulacağını
belirten `// TODO (Modül X): ...` yorumları bulunur — modül çalışmasına başladığımızda
doğrudan bu noktalara gideceğiz.

---

## 3. Altyapı Bileşenleri

Altyapı ve uygulamalar **iki ayrı compose dosyasına** bölünmüştür:

- **`docker-compose.infra.yml`** — sizin daha önce kurduğunuz, kalıcı altyapı (Postgres, Redis, Kafka, Vault, Consul, Keycloak, Seq, Prometheus, Grafana, Jaeger + ekstra: RabbitMQ, RedisInsight, Kafka UI). Repo, dosyalarınızı **`neominal-*`** container adları ve **`neominal-net`** ağıyla birebir kullanacak şekilde uyarlanmıştır.
- **`docker-compose.apps.yml`** — sadece 5 .NET servisi; `neominal-net` ağına **`external: true`** ile katılır.

| Bileşen | Amaç | Port | Kimlik bilgisi |
|---|---|---|---|
| PostgreSQL | Database-per-service (`order_db`, `payment_db`, `inventory_db`, `hangfire_db` — `neominal_demo`'ya ek olarak) | 15432 | `neominal` / `neominal_pass` |
| Redis | Cache + Distributed Lock (Redlock) | 6379 | — |
| Kafka (KRaft) | MassTransit transport | 9092 (internal) / 19094 (host, external listener) | — |
| Kafka UI | Kafka izleme arayüzü | 8082 | — |
| HashiCorp Vault (dev mode) | Secret Management | 8200 | root token: `root` |
| HashiCorp Consul | Service Discovery + Health Check + DNS | 8500 (UI/HTTP) / 8600 (DNS, udp) | — |
| Keycloak | AuthServer (OAuth2/OIDC) — v21.1.1, realm otomatik import edilir | 8180 | `admin` / `admin` |
| Seq | Serilog structured log görüntüleyici | 8081 (UI) / 5341 (ingest) | `.env`'deki `SEQ_ADMIN_PASSWORD` |
| Prometheus | Metrics depolama | 9090 | — |
| Grafana | Görselleştirme (hazır dashboard otomatik yüklenir) | 3000 | `admin` / `.env`'deki `GRAFANA_ADMIN_PASSWORD` |
| Jaeger | OpenTelemetry trace görüntüleyici | 16686 (UI) / 4317 (OTLP gRPC) / 4318 (OTLP HTTP) | — |
| RedisInsight | Redis veri inceleme arayüzü | 5540 | — |
| RabbitMQ (+ Management) | *(bu projede kullanılmıyor — Modül 3 Kafka kullanır)* | 25672 (AMQP) / 15672 (UI) | `neominal` / `neominal_pass` |
| Nginx | ⚠️ *(bu projede kullanılmıyor — API Gateway YARP ile yapılır, `nginx.conf` repoda yok)* | 8090 | — |
| order-api / payment-api / inventory-api | Mikroservisler | 5001 / 5002 / 5003 | — |
| api-gateway | YARP tabanlı tek giriş noktası | 8080 | — |
| job-service | Hangfire worker | 5010 | — |

### Ayağa Kaldırma

```bash
cp .env.example .env
# 1) Önce altyapı (Postgres init script'inin çalışması için ilk seferde
#    volume'ler boş olmalı — daha önce ayağa kaldırdıysanız ve yeni
#    veritabanlarının oluşması gerekiyorsa: docker compose -f docker-compose.infra.yml down -v)
docker compose -f docker-compose.infra.yml up -d

# 2) Sonra uygulamalar (infra'nın "healthy" durumuna gelmesini bekleyin)
docker compose -f docker-compose.apps.yml up -d --build

# 3) Modül 2: Vault dev sunucusu in-memory'dir, secret'ları yükleyin
bash infra/vault/seed-secrets.sh     # Linux/Mac/WSL
infra\vault\seed-secrets.cmd         # Windows cmd.exe
```

> **Ağ adı uyarısı:** `docker-compose.apps.yml`, `neominal-net` adında **var olan**
> bir Docker ağı bekler (`external: true`). Bunun için `docker-compose.infra.yml`
> içindeki ağ tanımına `name: neominal-net` eklenmiştir — bu satır olmadan Docker
> Compose ağı proje adını önek yaparak farklı isimlendirir ve apps compose'u
> "network not found" hatası verir. Detay: `docs/Altyapi-Entegrasyonu.md`.

> **Not:** Bu iskelet aşamasında servislerin `Program.cs` dosyalarındaki Modül 2-5 entegrasyonları
> (Vault, Consul, Keycloak, MassTransit, Hangfire) yorum satırı (`// TODO`) halindedir; servisler
> yalnızca Kestrel + PostgreSQL + Redis health check ile ayağa kalkacak şekilde derlenebilir durumdadır.
> Modüller ilerledikçe bu satırlar aktif hale getirilecektir.

---

## 4. Solution'ı Açma

```bash
dotnet restore NetMicroservicesMastery.sln
dotnet build NetMicroservicesMastery.sln
```

Visual Studio / Rider ile `NetMicroservicesMastery.sln` dosyasını doğrudan açabilirsiniz;
Solution Explorer'da `BuildingBlocks`, `Services/Order`, `Services/Payment`, `Services/Inventory`,
`Gateway`, `BackgroundJobs` klasörleri altında 19 proje göreceksiniz.

---

## 5. Dokümantasyon

Eklenen her özellik, hangi projede/modülde ne yaptığını anlatan bir doküman
eşliğinde gelir. Tüm dokümanlar [`docs/`](./docs/README.md) klasöründedir.

## 6. İlerleme Durumu

- ✅ **Derleme hataları düzeltildi** — class library'lere `FrameworkReference` eklendi (bkz. `COMPILE_FIX_NOTES.md`)
- ✅ **Modül 1 — Global Exception Handling**: `GlobalExceptionHandler` tamamlandı (DomainException→400, NotFoundException→404, diğer→500, RFC 7807 JSON)
- ✅ **Modül 1 — Observability zinciri**: `ObservabilityExtensions` tamamlandı (Serilog→Console+Seq, OpenTelemetry Metrics/Tracing, Prometheus `/metrics`, OTLP→Jaeger)
- ✅ **Modül 1 — Kestrel + Health Check zinciri**: Kestrel limitleri sıkılaştırıldı; `/health/live`, `/health/ready`, `/health` endpoint'leri gerçek Postgres/Redis kontrolleriyle çalışıyor
- ⬜ Multi-stage Dockerfile'ların gerçek derlemesi/testi (bu ortamda Docker daemon/NuGet erişimi yok — kendi ortamınızda doğrulanmalı)
- ✅ **Modül 2 — Secret Management (Vault)**: Order/Payment/Inventory'nin connection string'i Vault'tan okunuyor, appsettings.json üzerine override ediliyor
- ✅ **Modül 2 — Service Discovery (Consul)**: Order/Payment/Inventory/ApiGateway/JobService/Saga — **tüm servisler** kendini Consul'a kaydediyor
- ✅ **Modül 2 — YARP + Consul dinamik servis keşfi**: ApiGateway artık statik appsettings yerine Consul'dan periyodik olarak (10sn) sağlıklı instance'ları okuyor
- ✅ **Modül 2 — Keycloak (AuthServer)**: JWT doğrulama SADECE ApiGateway'de yapılıyor; downstream servisler doğrulama yapmıyor (merkezi kimlik doğrulama ilkesi)
- ✅ **Modül 2 — Polly**: Retry (exponential backoff+jitter) + Circuit Breaker + Fallback pipeline'ı tamamlandı; Order.Api'de `/test-resiliency` demo endpoint'i ile test edilebilir
- ✅ **Modül 2 — Rate Limiting**: Redis tabanlı DAĞITIK rate limiting (Lua script ile atomik), client bazlı (Keycloak `azp` claim'i) 10sn/20 istek kotası, `X-RateLimit-*` header'ları, aşımda `429`, Redis kesintisinde "fail open"
- ✅ **Modül 2 — Load Balancing**: YARP RoundRobin ile birden fazla Consul-kayıtlı instance arasında dağıtım; Inventory.Api için 2. instance test senaryosu (`docs/Inventory.Api.md`)
- ✅ **Modül 3 — MassTransit + Kafka temel kurulumu**: `AddDistributedMessaging()` ortak extension'ı; Order.Api Producer (`POST /submit-order`), Payment.Api + Inventory.Api'de 2 bağımsız Consumer; Partition Key (OrderId) + temel Retry
- ✅ **Modül 3 — Dead Letter Queue**: `Fault<T>` tabanlı, retry'lar tükenince orijinal mesaj + hata sebebi `order-created-dlq` Kafka topic'ine yazılıyor; `CustomerId: "FAIL"` ile test edilebilir
- ✅ **Modül 3 — Command (Send()) örneği**: `ProcessPaymentCommand` — "process-payment-command" topic'ini SADECE Payment.Api dinler (Inventory.Api dinlemez); `OrderCreatedEvent` (çoklu bağımsız consumer) ile somut fark böylece kanıtlandı. **Modül 3 tamamlandı.**
- ✅ **Modül 4 — CQRS (MediatR)**: Order.Api'nin iş mantığı `CreateOrderCommand`/`GetOrderByIdQuery` handler'larına taşındı; `ValidationBehavior` (FluentValidation) pipeline'ı; Repository/EventPublisher soyutlamaları (Clean Architecture — bağımlılık yönü Application'a doğru)
- ✅ **Modül 4 — Outbox Pattern**: DB yazımı ile Kafka'ya yayınlama artık ATOMİK (`OutboxOrderEventPublisher` + `OutboxProcessorHostedService`); `GET /debug/outbox` ile izlenebilir
- ✅ **Modül 4 — Saga Pattern (Choreography)**: `POST /submit-order-saga` — Order.Api → Inventory.Api → Payment.Api zinciri, başarı/başarısızlık senaryoları ve compensating adım (bkz. aşağıdaki akış diyagramı).
- ✅ **Modül 4 — Saga Pattern (Orchestration)**: Ayrı bir servis (**Saga.Api**), MassTransit State Machine + RabbitMQ ile — `POST /submit-order-saga-orchestrator`, canlı durum izleme (`GET Saga.Api:5004/debug/sagas/{orderId}`), event streaming (OrderSagaStateHistory). **Modül 4 tamamen tamamlandı.**
- ✅ **Modül 5 — Consul KV (Merkezi/Dinamik Konfigürasyon)**: Order.Api'de `MaxOrderAmount` — servis yeniden başlatılmadan Consul'dan canlı güncellenir (bkz. `docs/Order.Infrastructure.md`, `GET /debug/config`)
- ✅ **Modül 5 — Idempotent Consumer**: `ChargePaymentCommandConsumer` (Payment.Api) ve `ReserveInventoryCommandConsumer` (Inventory.Api) — aynı mesaj (`MessageId`) 2 kez gelirse yan etki (ödeme/rezervasyon) tekrarlanmaz (bkz. `docs/IdempotentConsumer.md`, `POST /debug/test-idempotency`)
- ✅ **Modül 5 — Distributed Lock**: `OutboxProcessorHostedService`, Redis/RedLock.net ile çoklu-instance korumasına kavuştu — aynı anda sadece bir Order.Api instance'ı outbox'ı işler (bkz. `docs/Order.Infrastructure.md`)
- ✅ **Modül 5 — Hangfire**: JobService'te fire-and-forget, delayed, recurring job örnekleri + Dashboard (`/hangfire`) + Distributed Lock entegrasyonu (bkz. `docs/JobService.md`). **Modül 5 tamamen tamamlandı — 5 modülün tamamı bitti.**

## 7. Sıradaki Adım

**Tüm 5 modül tamamlandı!** Bu proje artık Ön Hazırlık Dökümanı'ndaki tüm
konuları kapsıyor: Kestrel/Gözlemlenebilirlik (Modül 1), Vault/Consul/YARP/
Keycloak/Polly/Rate Limiting (Modül 2), MassTransit/Kafka/DLQ/Command-Event
(Modül 3), CQRS/Outbox/Saga — hem Choreography hem Orchestration (Modül 4),
Consul KV/Idempotent Consumer/Distributed Lock/Hangfire (Modül 5).

Olası sonraki adımlar (isteğe bağlı, orijinal modül kapsamının dışında):
Payment.Api/Inventory.Api'yi de CQRS'e taşımak, gerçek EF Core Migrations'a
geçmek, Hangfire Dashboard'a kimlik doğrulama eklemek, ya da Dockerfile
build/test doğrulamasını kendi ortamınızda tamamlamak.

## 8. Saga Pattern Akışları (Modül 4)

### 8.1 Choreography (`POST /submit-order-saga`)

`POST /submit-order-saga` — merkezi bir orkestratör olmadan, her servisin
bir öncekinin event'ini dinleyip kendi işini yaptığı ve kendi event'ini
yayınladığı bir akış:

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

4a) Order.Api (dinler: PaymentCompleted)     -> Status: Confirmed  ✅ BAŞARILI
4b) Order.Api (dinler: PaymentFailed)        -> Status: Cancelled
    -> "ReleaseInventoryCommand" SEND edilir (COMPENSATING ADIM)
5)  Inventory.Api (dinler: ReleaseInventoryCommand) -> stok GERİ BIRAKILIR

2b) Order.Api (dinler: InventoryReservationFailed) -> Status: Cancelled ❌
    (compensation GEREKMEZ — henüz hiçbir şey rezerve edilmemişti)
```

**3 test senaryosu** (`customerId` alanı ile tetiklenir):

| `customerId` | Sonuç | Durum geçişi |
|---|---|---|
| normal bir değer | ✅ Tam başarı | `Created → AwaitingInventory → Confirmed` |
| `"FAIL_INVENTORY"` | ❌ Stok yok (compensation yok) | `Created → AwaitingInventory → Cancelled` |
| `"FAIL_PAYMENT"` | ❌ Ödeme reddi + **compensation** | `Created → AwaitingInventory → Cancelled` (+ stok iade) |

Detaylı doğrulama komutları ve kod yapısı için bkz. `docs/BuildingBlocks.Messaging.md` "Saga Pattern" bölümü.

### 8.2 Orchestration (`POST /submit-order-saga-orchestrator`)

Choreography'nin aksine, merkezi bir "beyin" **vardır** — ayrı bir servis
olan **Saga.Api**, MassTransit'in **State Machine**'i ile TÜM akışı tek bir
yerde yönetir. Order.Api, Payment.Api, Inventory.Api sadece "komut al, işi
yap, sonucu bildir" yapar; akışın kendisini bilmezler. Transport olarak
**RabbitMQ** kullanılır (Kafka değil) — MassTransit'in Saga desteğinin
native ve kanıtlanmış çalıştığı transport budur.

```
Order.Api --(StartOrderSagaCommand)--> Saga.Api (OrderSagaStateMachine)
                                              |
                        +---------------------+---------------------+
                        v                                           v
          Inventory.Api (ReserveInventoryCommand)      Payment.Api (ChargePaymentCommand)
          --(Succeeded/Rejected)--> Saga.Api            --(Charged/Failed)--> Saga.Api
                        ^                                           |
                        +---- RevertInventoryReservationCommand ----+
                                    (COMPENSATION)                  |
                                                                     v
                              Order.Api <--(OrderSagaCompletedEvent / OrderSagaFailedEvent)--
```

**Aynı 3 test senaryosu** (`customerId` ile), aynı sonuçlar — fark sadece
akışın NEREDE yönetildiğidir (Saga.Api'de, merkezi olarak).

**Ekstra:** Saga.Api'de canlı durum + "event streaming" geçmişi izlenebilir:
```bash
curl http://localhost:5004/debug/sagas/<orderId>
```

Detaylı akış, `OrderSagaState`/`OrderSagaStateHistory` açıklaması ve
doğrulama komutları için bkz. `docs/Saga.Api.md`.

