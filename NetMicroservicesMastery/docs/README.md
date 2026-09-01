# Dokümantasyon İndeksi

Bu klasör, repoya eklenen her özelliğin **hangi projede**, **hangi modül
kapsamında** ve **nasıl** uygulandığını belgeler. Her proje için ayrı bir
`.md` dosyası tutulur; bir proje güncellendiğinde ilgili dosya da güncellenir.

## Modül 1 — Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik

| Proje | Doküman | Kapsam |
|---|---|---|
| `BuildingBlocks.Common` | [BuildingBlocks.Common.md](./BuildingBlocks.Common.md) | Global Exception Handling, RFC 7807 Problem Details, ortak Health Check response writer |
| `BuildingBlocks.Observability` | [BuildingBlocks.Observability.md](./BuildingBlocks.Observability.md) | Serilog (Seq) + OpenTelemetry (Prometheus/Jaeger) zinciri |
| `Order.Api` | [Order.Api.md](./Order.Api.md) | Kestrel yapılandırması, Health Check zinciri (liveness/readiness) |
| `Payment.Api` | [Payment.Api.md](./Payment.Api.md) | Kestrel yapılandırması, Health Check zinciri (liveness/readiness) |
| `Inventory.Api` | [Inventory.Api.md](./Inventory.Api.md) | Kestrel yapılandırması, Health Check zinciri (liveness/readiness) |
| — | [../COMPILE_FIX_NOTES.md](../COMPILE_FIX_NOTES.md) | Derleme hatalarının kök nedeni ve düzeltmesi |
| — | [Altyapi-Entegrasyonu.md](./Altyapi-Entegrasyonu.md) | Kullanıcının kendi `docker-compose.infra.yml` dosyasıyla uyumlama (network adı, kimlik bilgileri, init script) |
| — | [Yerel-Ortamda-Calistirma.md](./Yerel-Ortamda-Calistirma.md) | Servislerin hem Docker container'ında hem lokalde (`dotnet run`) çalıştırılması — appsettings.Docker.json / appsettings.Development.json ayrımı |
| — | [Grafana-Metrik-Dogrulama.md](./Grafana-Metrik-Dogrulama.md) | Servisler ayağa kalktığında metriklerin Grafana'da nasıl görüntüleneceği/test edileceği |

⬜ Multi-stage Dockerfile'ların gerçek build/test doğrulaması — henüz bu ortamda
(Docker daemon/NuGet erişimi olmadığından) yapılamadı; kendi ortamınızda
`docker compose up -d --build` ile doğrulanmalı.

## Modül 2 — Güvenlik, Dayanıklılık ve Konfigürasyon

| Proje | Doküman | Kapsam |
|---|---|---|
| `BuildingBlocks.Resilience` | [BuildingBlocks.Resilience.md](./BuildingBlocks.Resilience.md) | Secret Management (HashiCorp Vault) |
| `BuildingBlocks.Security` | [BuildingBlocks.Security.md](./BuildingBlocks.Security.md) | Service Discovery (Consul, tüm servisler) + Keycloak (JWT, sadece Gateway) |
| `ApiGateway` | [ApiGateway.md](./ApiGateway.md) | YARP + Consul dinamik servis keşfi (RoundRobin load balancing), Keycloak merkezi kimlik doğrulama, Rate Limiting |
| `Order.Api` / `Payment.Api` / `Inventory.Api` | (mevcut dosyalara ek bölüm) | Vault + Consul entegrasyonu; `Inventory.Api.md`'de ayrıca 2. instance ile load balancing testi |
| `JobService` | [JobService.md](./JobService.md) | Consul kaydı + health check |
| — | `infra/vault/seed-secrets.sh` \| `.cmd` | Vault dev sunucusu (in-memory) her restart'ta sıfırlandığında secret'ları yeniden yükleme |

✅ Polly (Retry/Circuit Breaker/Fallback) tamamlandı — `BuildingBlocks.Resilience.md`'de detaylı.

## Modül 3 — Event-Driven Mimari ve MassTransit Temelleri

| Proje | Doküman | Kapsam |
|---|---|---|
| `BuildingBlocks.Messaging` | [BuildingBlocks.Messaging.md](./BuildingBlocks.Messaging.md) | MassTransit + Kafka Rider temel kurulumu, `OrderCreatedEvent` (ilk somut Event), Producer/Consumer, Partition Key, temel Retry |
| `Order.Api` / `Payment.Api` / `Inventory.Api` | (mevcut dosyalara ek bölüm) | Producer (Order.Api) + 2 bağımsız Consumer (Payment.Api, Inventory.Api) |

✅ Dead Letter Queue tamamlandı (`Fault<T>` tabanlı, `order-created-dlq` topic'i). ✅ Command (Send()) örneği tamamlandı (`ProcessPaymentCommand`, sadece Payment.Api dinler). **Modül 3 tamamlandı.**

## Modül 4 — Finansal Uygulamalarda Veri Tutarlılığı

| Proje | Doküman | Kapsam |
|---|---|---|
| `Order.Application` | [Order.Application.md](./Order.Application.md) | CQRS (MediatR): `CreateOrderCommand`, `GetOrderByIdQuery`, `ValidationBehavior`, Repository/EventPublisher soyutlamaları |
| `Order.Infrastructure` | [Order.Infrastructure.md](./Order.Infrastructure.md) | `IOrderRepository`/`IOrderEventPublisher`'ın somut (EF Core/Kafka) implementasyonları |
| `Order.Api` | (mevcut dosyaya ek bölüm) | `/submit-order` ve `/orders/{id}` artık MediatR üzerinden çalışıyor |
| `Saga.Api` | [Saga.Api.md](./Saga.Api.md) | Saga Pattern (Orchestration) — ayrı servis, MassTransit State Machine + RabbitMQ, `OrderSagaState`/`OrderSagaStateHistory` |

✅ Outbox Pattern tamamlandı (bkz. `Order.Infrastructure.md`) — DB yazımı ile Kafka'ya yayınlama artık atomik. ✅ Saga Pattern **Choreography** tamamlandı — `POST /submit-order-saga` (bkz. `BuildingBlocks.Messaging.md`). ✅ Saga Pattern **Orchestration** tamamlandı — `POST /submit-order-saga-orchestrator`, ayrı bir servis (Saga.Api) + MassTransit State Machine + RabbitMQ (bkz. `Saga.Api.md`). **Modül 4 tamamen tamamlandı.**

## Modül 5 — Ölçeklenebilirlik ve Sağlamlık

| Konu | Doküman | Kapsam |
|---|---|---|
| Consul KV | [Order.Infrastructure.md](./Order.Infrastructure.md) | Order.Api'de `MaxOrderAmount` — servis yeniden başlatılmadan canlı güncellenen konfigürasyon |
| Idempotent Consumer | [IdempotentConsumer.md](./IdempotentConsumer.md) | `ChargePaymentCommandConsumer` (Payment.Api) ve `ReserveInventoryCommandConsumer` (Inventory.Api) — aynı mesaj 2 kez gelirse yan etki tekrarlanmaz |
| Distributed Lock | [Order.Infrastructure.md](./Order.Infrastructure.md) | `OutboxProcessorHostedService` — Redis/RedLock.net ile çoklu-instance koruması (paylaşılan `BuildingBlocks.Resilience`) |
| Hangfire | [JobService.md](./JobService.md) | Fire-and-forget/delayed/recurring job'lar + Dashboard + Distributed Lock entegrasyonu |

✅ **Modül 5 tamamen tamamlandı — 5 modülün tamamı bitti.**
