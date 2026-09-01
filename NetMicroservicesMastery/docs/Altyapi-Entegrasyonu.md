# Altyapı Entegrasyonu — Kendi `docker-compose.infra.yml` Dosyanızla Uyumlama

**Bağlam:** Repoya daha önce kurulmuş ve çalışır durumdaki kendi altyapı
compose dosyanız (`docker-compose.infra.yml`) entegre edildi. Bu doküman,
neyin neden değiştirildiğini ve iki compose dosyasının birlikte nasıl
çalıştırılacağını açıklar.

## Neden İki Ayrı Compose Dosyası?

- **`docker-compose.infra.yml`** — sizin yönettiğiniz, kalıcı altyapı
  (Postgres, Redis, Kafka, Vault, Consul, Keycloak, Seq, Prometheus, Grafana,
  Jaeger + RabbitMQ, RedisInsight, Kafka UI, Nginx).
- **`docker-compose.apps.yml`** — sadece bu projenin 5 .NET servisi (Order,
  Payment, Inventory, ApiGateway, JobService). Altyapıdan bağımsız olarak
  build edilir/yeniden başlatılır; altyapıyı etkilemez.

İki dosya, aynı Docker ağını (`neominal-net`) paylaşır; bu sayede
`order-api` container'ı, `postgres` container'ına doğrudan hostname ile
(`Host=postgres`) erişebilir.

## Yapılan Değişiklikler

### 1. `docker-compose.infra.yml` (sizin dosyanız, repoya alınıp uyarlandı)

| Değişiklik | Neden |
|---|---|
| `networks.neominal-net.name: neominal-net` eklendi | Docker Compose, proje adını (`neominal-microservices-infra`) önek yaparak ağ adını değiştirir; bu satır olmadan `docker-compose.apps.yml`'deki `external: true` referansı ağı bulamaz |
| `postgres.volumes`'a `init-multiple-dbs.sh` mount edildi | Modül 4 "database-per-service" ilkesini korumak için `order_db`, `payment_db`, `inventory_db`, `hangfire_db` veritabanlarının `neominal_demo`'ya ek olarak oluşturulması gerekiyordu |
| `prometheus.volumes` yolu `./infra/prometheus/prometheus.yml` olarak güncellendi | Dosya artık bu repodaki `infra/` klasöründe yaşıyor |
| `grafana.volumes`'a `./infra/grafana/provisioning` eklendi | Modül 1'de hazırlanan Prometheus datasource + hazır dashboard'un otomatik yüklenmesi için |
| `keycloak.volumes`'a `realm-export.json` + `--import-realm` eklendi | Modül 2 çalışmasında kullanılacak realm/client tanımlarının hazır gelmesi için (istemiyorsanız kaldırılabilir) |
| Nginx servisine uyarı yorumu eklendi | Bu proje API Gateway olarak YARP kullanıyor; Nginx servisi şu an **kullanılmıyor** ve `nginx.conf` repoda yok — olduğu gibi çalıştırılırsa mount hatası verir |

**Değiştirilmeyenler:** container adları (`neominal-*`), portlar, kimlik
bilgileri (Postgres `neominal`/`neominal_pass`, Vault token `root`, Keycloak
`admin`/`admin`), RabbitMQ/RedisInsight/Kafka UI servisleri — hepsi olduğu
gibi korundu.

### 2. `docker-compose.yml` → `docker-compose.apps.yml` (yeniden adlandırıldı ve daraltıldı)

Önceki tek dosyalı yaklaşımda bu repo **kendi** altyapısını (farklı container
adları, farklı kimlik bilgileriyle) da içeriyordu. Artık:

- Sadece 5 .NET servisi kaldı.
- `depends_on` + healthcheck koşulları kaldırıldı (Compose, farklı bir
  projede tanımlı servislere `depends_on` ile bağlanamaz) — **altyapının
  ayakta olduğundan emin olduktan sonra** `docker-compose.apps.yml`'i
  başlatmanız gerekir.
- Ağ artık `external: true` ile sizin `neominal-net` ağınıza katılıyor.

### 3. `appsettings.json` / `appsettings.Development.json` (Order/Payment/Inventory Api + JobService)

Postgres bağlantı dizelerindeki kimlik bilgisi güncellendi:

```diff
- Host=postgres;Port=5432;Database=order_db;Username=postgres;Password=postgres
+ Host=postgres;Port=5432;Database=order_db;Username=neominal;Password=neominal_pass
```

**Değişmeyenler** (zaten sizin infra dosyanızla birebir örtüşüyordu):
`Redis:ConnectionString` (`redis:6379`), `Kafka:BootstrapServers`
(`kafka:9092`), `Vault:Address` (`http://vault:8200`), `Consul:Address`
(`http://consul:8500`), `Keycloak:Authority` (`http://keycloak:8080/...` —
container içi port her zaman 8080'dir, host'a yayınlanan `8180` farklıdır),
`Seq:ServerUrl` (`http://seq:5341`), `OpenTelemetry:OtlpEndpoint`
(`http://jaeger:4317`).

### 4. `infra/postgres/init-multiple-dbs.sh`

`keycloak_db` listeden çıkarıldı — sizin Keycloak kurulumunuz (v21.1.1,
`start-dev`) harici bir Postgres'e değil, gömülü/dev-mode veritabanına
bağlanıyor.

## Çalıştırma Sırası

```bash
cp .env.example .env

# İlk kurulumsa ya da yeni veritabanlarının (order_db vb.) oluşması
# gerekiyorsa (postgres-data volume'ü zaten doluysa init script ÇALIŞMAZ):
docker compose -f docker-compose.infra.yml down -v   # sadece gerekiyorsa!

docker compose -f docker-compose.infra.yml up -d
# Postgres/Kafka/Keycloak'ın tam ayağa kalkması birkaç dakika sürebilir.
docker compose -f docker-compose.infra.yml ps

docker compose -f docker-compose.apps.yml up -d --build
```

## Bilinen Riskler / Kontrol Etmeniz Gerekenler

| Konu | Durum |
|---|---|
| `neominal-net` ağ adı eşleşmesi | `name: neominal-net` eklendi; yine de ilk çalıştırmada `docker network ls \| grep neominal-net` ile tam adı doğrulayın |
| Postgres init script'inin tetiklenmesi | Sadece **boş bir volume** üzerinde ilk başlatmada çalışır. Daha önce bu infra'yı ayağa kaldırdıysanız, yeni veritabanlarının oluşması için `docker compose -f docker-compose.infra.yml down -v` (⚠️ mevcut verileri siler) gerekir |
| Nginx servisi | `nginx.conf` repoda yok — bu servisi kullanmıyorsanız `docker-compose.infra.yml`'den kaldırmanız veya kendi config'inizi eklemeniz gerekir |
| Keycloak realm import | v21.1.1 + `--import-realm` bayrağı eklendi; farklı bir Keycloak sürümü/akışı kullanıyorsanız bu davranışı doğrulayın |

## İlgili Dokümanlar

- `docs/Grafana-Metrik-Dogrulama.md` — iki dosyalı çalıştırma sonrası metrik doğrulama
- `docs/BuildingBlocks.Observability.md` — Seq/Prometheus/Jaeger adresleri
