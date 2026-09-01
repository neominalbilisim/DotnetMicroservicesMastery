# Servisleri Hem Docker'da Hem Lokalde Çalıştırma

**Amaç:** Order/Payment/Inventory/ApiGateway/JobService'in hem Docker
container'ı içinden hem de doğrudan host makinede (`dotnet run` / Visual
Studio) — **altyapı her koşulda Docker'da kalırken** — sorunsuz çalışabilmesi.

## ⚠️ Önemli: Kestrel'in Tüm Arayüzlerden Dinlemesi (0.0.0.0)

`launchSettings.json`'daki `applicationUrl` **`http://0.0.0.0:{port}`** olarak
ayarlanmıştır — `http://localhost:{port}` **değil**. Bunun nedeni:

`http://localhost:5001` gibi bir URL, Kestrel'i **sadece loopback arayüzüne**
(127.0.0.1 / ::1) bağlar. Bu durumda uygulama sizin tarayıcınızdan
(`http://localhost:5001`) erişilebilir olsa da, Docker Desktop'ın
`host.docker.internal` köprüsü üzerinden (yani Prometheus container'ından)
gelen istekler **farklı bir sanal ağ arayüzünden** geldiği için reddedilir
(`connection refused`).

`0.0.0.0` kullanmak Kestrel'i **tüm IPv4 arayüzlerinden** dinlemeye zorlar —
bu, `localhost` erişimini de kapsar (hiçbir şey bozulmaz), ek olarak
`host.docker.internal` üzerinden gelen bağlantıları da kabul eder.

## Neden appsettings.Docker.json ve appsettings.Development.json Ayrı?

Container hostname'leri (`postgres`, `redis`, `kafka`, `seq`, `vault`,
`consul`, `keycloak`, `jaeger`) yalnızca **aynı Docker ağındaki** container'lar
için çözümlenir. Uygulama host makinede çalışırken bu isimler DNS'te
bulunamaz — bunun yerine `localhost` + `docker-compose.infra.yml`'in
**host'a yayınladığı** portlar kullanılmalıdır.

| Ayar | `appsettings.Docker.json` (container'da) | `appsettings.Development.json` (lokalde) |
|---|---|---|
| Postgres | `Host=postgres;Port=5432` | `Host=localhost;Port=15432` |
| Redis | `redis:6379` | `localhost:6379` |
| Kafka | `kafka:9092` | `localhost:19094` ⚠️ (bkz. not) |
| Vault | `http://vault:8200` | `http://localhost:8200` |
| Consul | `http://consul:8500` | `http://localhost:8500` |
| Keycloak | `http://keycloak:8080/realms/...` | `http://localhost:8180/realms/...` |
| Seq | `http://seq:5341` | `http://localhost:5341` |
| Jaeger (OTLP) | `http://jaeger:4317` | `http://localhost:4317` |
| ApiGateway → YARP hedefleri | `http://order-api:8080` vb. | `http://localhost:5001` vb. |

> ⚠️ **Kafka özel durumu:** Host'tan bağlanırken `localhost:9092` **DEĞİL**,
> `localhost:19094` kullanılmalıdır. Kafka'nın `PLAINTEXT` listener'ı
> kendini `kafka:9092` olarak duyurur (advertised listener) — bir host
> istemcisi ilk bağlantıdan sonra bu adrese yönlendirilir ve "kafka"
> hostname'i host makinede çözümlenemediği için bağlantı koparılır. Bu
> yüzden `docker-compose.infra.yml`'de ayrı bir `EXTERNAL` listener
> (`localhost:19094` olarak duyurulan) tanımlıdır — host istemcileri
> bunu kullanmalıdır.

## Hangi Ortam Ne Zaman Aktif Olur?

.NET, `ASPNETCORE_ENVIRONMENT` değişkenine göre `appsettings.{Environment}.json`
dosyasını `appsettings.json`'ın üzerine yükler:

| Nasıl çalıştırıldı | `ASPNETCORE_ENVIRONMENT` | Yüklenen dosya |
|---|---|---|
| `docker compose -f docker-compose.apps.yml up -d --build` | `Docker` (compose dosyasında tanımlı) | `appsettings.Docker.json` |
| `dotnet run` (proje dizininde) | `Development` (`Properties/launchSettings.json`'dan) | `appsettings.Development.json` |
| Visual Studio "▶ Başlat" | `Development` (launchSettings profili) | `appsettings.Development.json` |

> Swagger UI, hem `Development` hem `Docker` ortamında **açıktır**
> (`!app.Environment.IsProduction()` kontrolü kullanılır) — bu eğitim
> amaçlı bir proje olduğundan üretim ortamı henüz tanımlı değildir.

## Lokalde Çalıştırma Adımları

```bash
# 1) Sadece altyapı Docker'da ayakta olsun (uygulamalar OLMASIN)
docker compose -f docker-compose.infra.yml up -d

# 2) İstediğiniz servisi lokalde çalıştırın
cd src/Services/Order/Order.Api
dotnet run
# veya Visual Studio'da projeyi "Order.Api" launch profiliyle başlatın.
```

`launchSettings.json` her proje için önceden tanımlanmıştır:

| Proje | Local URL |
|---|---|
| Order.Api | http://localhost:5001 |
| Payment.Api | http://localhost:5002 |
| Inventory.Api | http://localhost:5003 |
| ApiGateway | http://localhost:8080 |
| JobService | http://localhost:5010 |

> **Not:** Docker container'ları da AYNI portları host'a yayınlıyor
> (5001, 5002...). Bu yüzden **aynı anda hem container hem lokal** modda
> aynı servisi çalıştırmayın — port çakışması olur. Test sırası: önce
> birini durdurup diğerini başlatın.

## Prometheus'un İki Modu Birden İzlemesi

`infra/prometheus/prometheus.yml`, her servis için **iki hedef** tanımlar:

```yaml
- job_name: 'order-api'
  static_configs:
    - targets: ['order-api:8080']            # Docker modu
      labels: { mode: 'docker' }
    - targets: ['host.docker.internal:5001'] # Lokal mod
      labels: { mode: 'local' }
```

`http://localhost:9090/targets` sayfasında, hangi modda çalıştırıyorsanız o
hedef **"UP"**, diğeri **"DOWN"** görünür — bu beklenen ve zararsız bir
durumdur; hata değildir. Grafana dashboard'undaki sorgular (`job` etiketine
göre) her iki modda da otomatik doğru veriyi gösterir.

`host.docker.internal` çözümlemesi, `docker-compose.infra.yml`'deki
`prometheus.extra_hosts: host.docker.internal:host-gateway` satırı
sayesinde çalışır (zaten mevcuttu, değişiklik gerekmedi).

## Sorun Giderme

| Belirti | Olası Neden |
|---|---|
| Lokalde `dotnet run` sonrası Postgres'e bağlanamıyor | `appsettings.Development.json`'da port `15432` mi kontrol edin (5432 değil) |
| Lokalde Kafka'ya publish/consume başarısız | `localhost:9092` değil `localhost:19094` kullanıldığından emin olun |
| Prometheus `/targets`'ta `host.docker.internal:{port}` için "connection refused" | `launchSettings.json`'daki `applicationUrl`'in `http://0.0.0.0:{port}` olduğundan emin olun (`http://localhost:{port}` DEĞİL) — yukarıdaki "Kestrel'in Tüm Arayüzlerden Dinlemesi" bölümüne bakın |
| Docker'da çalışırken loglar Seq'e gitmiyor ama lokalde gidiyor (veya tersi) | `ASPNETCORE_ENVIRONMENT` doğru mu (`docker compose ... config` ile kontrol edin); `[Serilog SelfLog]` satırları için container loglarına bakın |
| ApiGateway lokalde 502/bağlantı hatası veriyor | Order/Payment/Inventory Api'lerin de lokalde (aynı anda) `dotnet run` ile ayakta olduğundan emin olun — ApiGateway'in Development ayarları onları `localhost:500x`'te arar |

## İlgili Dokümanlar

- `docs/Altyapi-Entegrasyonu.md` — altyapı dosyanızla port/kimlik bilgisi uyumu
- `docs/Grafana-Metrik-Dogrulama.md` — metrik doğrulama (her iki mod için de geçerli)
