# Grafana ile Metrik Doğrulama

**Modül:** Modül 1 — Merkezi Loglama ve Gözlemlenebilirlik Zinciri
**Amaç:** `docker compose up` ile ayağa kalkan servislerin ürettiği metriklerin
Grafana üzerinden gerçekten görünür olduğunu doğrulamak.

## 0. Ön Koşul

```bash
cp .env.example .env
docker compose -f docker-compose.infra.yml up -d
docker compose -f docker-compose.apps.yml up -d --build
docker compose -f docker-compose.infra.yml -f docker-compose.apps.yml ps   # tüm servislerin ayakta olduğunu kontrol edin
```

## 1. Hazır Dashboard Otomatik Yüklenir

`infra/grafana/provisioning/dashboards/net-microservices-overview.json`, Grafana
açılışında **otomatik olarak** yüklenir (provider tanımı:
`infra/grafana/provisioning/dashboards/dashboard-provider.yml`). Elle dashboard
oluşturmanıza gerek yoktur.

1. Tarayıcıda **http://localhost:3000** adresine gidin.
2. `admin` / `.env` dosyanızdaki `GRAFANA_ADMIN_PASSWORD` değeri ile giriş yapın.
3. Sol menüden **Dashboards** → **".NET Microservices Mastery - Genel Bakış"**
   dashboard'unu açın.

Dashboard'da şu paneller bulunur:

| Panel | Ne gösterir |
|---|---|
| Servis Durumu (up) | Her servisin Prometheus tarafından "ayakta" görülüp görülmediği |
| İstek Oranı (RPS) | Saniyedeki HTTP istek sayısı, servise göre |
| İstek Süresi p95 | İsteklerin %95'inin tamamlandığı süre (saniye) |
| Hata Oranı (5xx) | Sunucu hatası dönen isteklerin oranı |
| .NET GC Heap Boyutu | Her servisin bellek kullanımı |

Üstteki **`job`** dropdown'ından tek bir servisi (örn. sadece `order-api`)
seçip filtreleyebilirsiniz.

## 2. Trafik Üretin (Metriklerin Görünmesi İçin)

Histogram/counter tipi metrikler (istek oranı, p95 süre) yalnızca **gerçek
trafik olduğunda** veri gösterir. Health check endpoint'lerine birkaç istek
göndererek trafik üretin:

```bash
for i in $(seq 1 30); do
  curl -s http://localhost:5001/health > /dev/null   # Order.Api
  curl -s http://localhost:5002/health > /dev/null   # Payment.Api
  curl -s http://localhost:5003/health > /dev/null   # Inventory.Api
  sleep 1
done
```

Bu isteği attıktan ~30-60 saniye sonra Grafana dashboard'unu yenileyin
(sağ üstte otomatik 10 saniyede bir yenilenir); "İstek Oranı" ve "p95" panelleri
dolmaya başlamalıdır.

## 3. Ham Metrikleri Doğrudan Görme (Sorun Giderme)

Bir panel "No data" gösteriyorsa, önce servisin `/metrics` endpoint'ini
doğrudan kontrol edin:

```bash
curl http://localhost:5001/metrics | head -50
```

Bu çıktıda `http_server_request_duration_seconds_count` gibi satırlar
görüyorsanız veri üretiliyor demektir — sorun muhtemelen Grafana/Prometheus
tarafındadır (bkz. Sorun Giderme tablosu).

Ayrıca Grafana'nın **Explore** ekranı (sol menü → pusula ikonu), hangi metrik
isimlerinin gerçekten mevcut olduğunu keşfetmek için en hızlı yoldur:
Datasource olarak **Prometheus**'u seçip metrik adını yazmaya başladığınızda
otomatik tamamlama, o an Prometheus'a scrape edilmiş tüm metrik isimlerini
listeler.

## 4. Prometheus Üzerinde Doğrudan Kontrol

Prometheus'un servisleri gerçekten scrape edip etmediğini görmek için:

**http://localhost:9090/targets** → tüm target'ların (`order-api`, `payment-api`,
`inventory-api`, `api-gateway`) durumu **"UP"** olmalıdır.

## Sorun Giderme

| Belirti | Olası Neden | Çözüm |
|---|---|---|
| Grafana'da dashboard görünmüyor | Provisioning dosyaları container'a mount edilmemiş | `docker-compose.infra.yml`'de `grafana` servisinin `volumes` altında `./infra/grafana/provisioning:/etc/grafana/provisioning` olduğunu kontrol edin; `docker compose -f docker-compose.infra.yml restart grafana` |
| Prometheus `/targets` sayfasında bir servis "DOWN" | Servis henüz ayağa kalkmadı veya crash oldu | `docker compose -f docker-compose.infra.yml -f docker-compose.apps.yml logs <servis-adı>` ile logları inceleyin |
| Panel "No data" ama `/metrics` endpoint'inde veri var | Metrik/label ismi OpenTelemetry paket sürümüne göre farklılaşmış olabilir | Grafana **Explore** ile gerçek metrik adını bulup panel sorgusunu (`.json` dashboard dosyasında) buna göre güncelleyin |
| `up{job=...}` sorgusu boş dönüyor | `job` dropdown'ında "All" seçili değil veya yanlış servis seçili | Dropdown'dan "All" seçin |
| `/health` çalışıyor ama `/metrics` boş | `AddPrometheusExporter()` veya `MapPrometheusScrapingEndpoint()` eksik/hatalı | `docs/BuildingBlocks.Observability.md` içindeki kurulumu kontrol edin |

## İlgili Dokümanlar

- `docs/BuildingBlocks.Observability.md` — metriklerin nasıl üretildiği (OpenTelemetry kurulumu)
- `docs/Order.Api.md`, `Payment.Api.md`, `Inventory.Api.md` — health check endpoint'leri
