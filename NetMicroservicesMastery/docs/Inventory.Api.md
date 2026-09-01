# Inventory.Api

**Modül:** Modül 1 — Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik
**Konu:** Kestrel Self-Hosted Yapılandırması + Health Check Zinciri

## Servis Bilgisi

| | |
|---|---|
| Katman | `*.Api` (Kestrel self-hosted giriş noktası) |
| Host port | `5003` (docker-compose) |
| Veritabanı | PostgreSQL — `inventory_db` |
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
curl http://localhost:5003/health/live
curl http://localhost:5003/health/ready
curl http://localhost:5003/health
curl http://localhost:5003/metrics   # bkz. BuildingBlocks.Observability.md
```

## İlgili Dokümanlar

- `docs/BuildingBlocks.Common.md` — Global Exception Handling, HealthCheckResponseWriter
- `docs/BuildingBlocks.Observability.md` — Serilog/OpenTelemetry/Prometheus zinciri

## Bilinen Sınırlamalar / Sonraki Adımlar

- Kafka için henüz bir health check eklenmedi (Modül 3 — MassTransit entegrasyonu
  sırasında `AddKafka(...)` eklenecektir).
- Vault/Consul/Keycloak entegrasyonları henüz aktif değil (Modül 2).

## Modül 2 Güncellemesi: Vault + Consul

- **Secret Management (Vault):** `ConnectionStrings:InventoryDb` artık appsettings.json yerine HashiCorp Vault'tan okunuyor (bkz. `docs/BuildingBlocks.Resilience.md`). Vault'a ulaşılamazsa appsettings.json'daki değere düşülür.
- **Service Discovery (Consul):** Bu servis artık ayağa kalktığında kendini Consul'a kaydediyor, kapanırken kaydını siliyor (bkz. `docs/BuildingBlocks.Security.md`). Health check olarak mevcut `/health/ready` endpoint'i kullanılıyor.
- **JWT doğrulama YOK:** Mimari karar gereği bu servis Keycloak/JWT doğrulaması yapmaz — bu sorumluluk tamamen ApiGateway'e aittir (bkz. `docs/ApiGateway.md`).

## Modül 2: İkinci Instance ile Load Balancing Testi

`Properties/launchSettings.json`'da ikinci bir launch profili tanımlıdır —
bu, aynı servisin (`inventory-service`) FARKLI bir portta ikinci bir
instance'ını ayağa kaldırmak ve YARP'ın Consul üzerinden ikisi arasında
**RoundRobin load balancing** yaptığını canlı olarak görmek içindir.

### Nasıl Çalıştırılır

**Visual Studio kullanıyorsanız:** Üstteki "Başlat" dropdown'ından
`Inventory.Api (Instance 2 - Load Balancing testi)` profilini seçip
çalıştırmanız yeterlidir — hiçbir ek adım gerekmez.

**cmd.exe (Komut İstemi) kullanıyorsanız:** `dotnet run --launch-profile "..."`
komutu, profil adındaki boşluk/parantez nedeniyle cmd.exe'de tırnaklama
sorunlarına yol açabilir. Bunun yerine ortam değişkenlerini doğrudan `set`
ile verip `--no-launch-profile` ile varsayılan profilin (port 5003) araya
girmesini engelleyin:

```bat
REM --- Terminal 1 (birinci instance, normal şekilde) ---
cd src\Services\Inventory\Inventory.Api
dotnet run

REM --- Terminal 2 (YENİ bir Komut İstemi penceresi açın) ---
cd src\Services\Inventory\Inventory.Api

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://0.0.0.0:5013
set Vault__Token=root
set Consul__ServicePort=5013

dotnet run --no-launch-profile --no-build
```

> `set` komutları sadece o Komut İstemi penceresi için geçerlidir — bu yüzden
> Terminal 2'yi **ayrı, yeni** bir pencerede açmanız gerekir; aksi halde
> Terminal 1'in ortam değişkenlerini de etkilersiniz. `--no-build` şart —
> aksi halde "dosya kullanımda" (MSB3021) hatası alırsınız (bkz. aşağıdaki bölüm).

**Linux/Mac/WSL (bash) kullanıyorsanız**, `--launch-profile` tırnaklama
sorunu yaşamaz:

```bash
# Terminal 1
cd src/Services/Inventory/Inventory.Api && dotnet run

# Terminal 2 (Terminal 1'in "Now listening on..." demesini bekleyin)
cd src/Services/Inventory/Inventory.Api
dotnet run --launch-profile "Inventory.Api (Instance 2 - Load Balancing testi)" --no-build
```

### ⚠️ "Dosya kullanımda" (MSB3021) Hatası

İki instance **aynı proje klasöründen** aynı derleme çıktısını
(`bin/Debug/net10.0/`) paylaştığı için, ikinci `dotnet run` komutu ayrıca
derleme/kopyalama yapmaya çalışırsa, birinci instance'ın kilitli tuttuğu
`.pdb`/`.dll` dosyalarıyla çakışır:

```
error MSB3021: Unable to copy file "...Inventory.Infrastructure.pdb" ...
because it is being used by another process.
```

**Çözüm:** İkinci instance'ı **`--no-build`** ile çalıştırın (yukarıdaki
komutlarda zaten var) — bu, zaten Terminal 1 tarafından derlenmiş çıktıyı
yeniden derlemeden kullanır. Terminal 1'in build'i tamamlanmış (uygulama
"Now listening on..." demiş) olduktan **sonra** Terminal 2'yi başlatın.

Kod üzerinde değişiklik yapıp yeniden test etmek isterseniz: önce **her iki**
instance'ı durdurun, `dotnet build` ile bir kez derleyin, sonra ikisini de
`--no-build` ile tekrar başlatın.

### Doğrulama

**Instance'ı görsel olarak ayırt etmek için:** `Program.cs`'e, tüm cevaplara
`X-Instance-Port` header'ı ekleyen küçük bir middleware eklendi — bu sayede
Gateway üzerinden gelen bir cevabın **hangi instance'tan** geldiğini
görebilirsiniz (`curl -i` ile header'ları görüntüleyin).

1. **Consul UI'da** (`http://localhost:8500` → Services → `inventory-service`)
   artık **2 ayrı instance** listelendiğini görün (`host.docker.internal:5003`
   ve `host.docker.internal:5013`), ikisi de yeşil (healthy).

2. **ApiGateway loglarında** (`docker compose -f docker-compose.apps.yml logs api-gateway`
   veya lokal terminal) ~10 saniye içinde şu satırı görün:
   ```
   [Consul->YARP] 'inventory-service' için 2 sağlıklı instance bulundu: http://host.docker.internal:5003, http://host.docker.internal:5013
   ```

3. **Gateway üzerinden art arda istek atıp `X-Instance-Port` header'ını izleyin:**
   ```bash
   curl -i http://localhost:8080/api/inventory/test -H "Authorization: Bearer <access_token>"
   ```
   Bu komutu 4-6 kez art arda çalıştırın — RoundRobin çalışıyorsa `X-Instance-Port`
   değeri sırayla `5003`, `5013`, `5003`, `5013`... şeklinde değişmelidir.
   (`404 Not Found` gövdesi normaldir — henüz gerçek `/api/inventory` endpoint'i
   yok; önemli olan header'daki port değişimidir.)

4. **Bir instance'ı durdurun** (Ctrl+C ile terminal 2'yi kapatın) — ~10-70
   saniye içinde Consul onu "critical" olarak işaretler, `ConsulYarpSyncHostedService`
   onu YARP hedeflerinden çıkarır ve tüm trafik otomatik olarak kalan tek
   instance'a yönlenir (istemci tarafında hiçbir hata/kesinti olmadan).

## Modül 3 Güncellemesi: MassTransit + Kafka Consumer

`Consumers/OrderCreatedConsumer.cs` — aynı "order-created" topic'ini
**bağımsız** bir tüketici grubuyla (`inventory-service-group`) dinler —
Payment.Api'nin aynı event'i aynı anda işlemesini engellemez (Publish/Subscribe
modeli). Detaylar için bkz. `docs/BuildingBlocks.Messaging.md`.

## Modül 4 Güncellemesi: Saga Pattern Katılımcısı

`Consumers/OrderSagaStartedConsumer.cs` (stok rezervasyonu simülasyonu,
`CustomerId: "FAIL_INVENTORY"` ile test edilebilir) ve
`Consumers/ReleaseInventoryCommandConsumer.cs` (compensation alıcısı).
Detaylar için bkz. `docs/BuildingBlocks.Messaging.md`.

## Modül 4 Güncellemesi: Saga Pattern (Orchestration) Katılımcısı

`Consumers/ReserveInventoryCommandConsumer.cs` (rezervasyon, `CustomerId:
"FAIL_INVENTORY"` ile test edilebilir) ve `RevertInventoryReservationCommandConsumer.cs`
(compensation alıcısı) — ikisi de Saga.Api'den (RabbitMQ) gelen komutları işler.
Detaylar için bkz. `docs/Saga.Api.md`.
