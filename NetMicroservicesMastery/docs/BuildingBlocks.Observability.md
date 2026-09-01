# BuildingBlocks.Observability

**Modül:** Modül 1 — Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik
**Konu:** Merkezi Loglama ve Gözlemlenebilirlik Zinciri

## Amaç

Serilog (structured logging) + OpenTelemetry (metrics/tracing) + Prometheus
exporter zincirini, tüm mikroservislere (Order, Payment, Inventory, ApiGateway,
JobService) **tek satırla** kazandıran ortak extension noktası.

```
.NET Servisi (Serilog + OpenTelemetry)
     │
     ├── Loglar ──────────► Seq (structured log arama/filtreleme UI'ı)
     ├── Metrikler ───────► Prometheus (/metrics scrape) ──► Grafana (dashboard)
     └── Trace'ler ───────► OTLP ──► Jaeger (distributed tracing UI'ı)
```

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `ObservabilityExtensions.cs` | `AddServiceObservability()` ve `UseServiceObservability()` extension'ları |

## Bir Serviste Nasıl Kullanılır (Program.cs)

```csharp
builder.AddServiceObservability(serviceName: "Order.Api");
...
app.UseServiceObservability();
```

## `AddServiceObservability` Ne Yapar?

1. **Serilog** — `builder.Services.AddSerilog(...)`:
   - `Console` (docker logs) + `Seq` (`appsettings.json` → `Seq:ServerUrl`) sink'lerine yazar
   - `ServiceName`, `EnvironmentName` alanlarıyla enrich eder
   - `Microsoft.AspNetCore` / `Microsoft.EntityFrameworkCore` loglarını `Warning` seviyesine çeker (gürültü azaltma)
   - `appsettings.json`'daki isteğe bağlı `"Serilog"` bölümünü de okuyup override edebilir

2. **OpenTelemetry — Metrics**:
   - `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`, `AddRuntimeInstrumentation()`
   - `AddPrometheusExporter()` → `/metrics` endpoint'i üretir (bkz. `UseServiceObservability`)

3. **OpenTelemetry — Tracing**:
   - `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`
   - `AddOtlpExporter()` → `appsettings.json` → `OpenTelemetry:OtlpEndpoint` (Jaeger'a gönderir)

4. **Resource** — `AddService(serviceName, serviceVersion)`: Grafana/Jaeger'da hangi
   servisten geldiğini ayırt etmek için `service.name` attribute'unu ekler.

## `UseServiceObservability` Ne Yapar?

- `app.UseSerilogRequestLogging()` → her HTTP isteğini (method, path, status code,
  süre) tek satır structured log olarak yazar.
- `app.MapPrometheusScrapingEndpoint()` → `/metrics` endpoint'ini map'ler.

## appsettings.json Anahtarları

```json
{
  "Seq": { "ServerUrl": "http://seq:5341" },
  "OpenTelemetry": { "OtlpEndpoint": "http://jaeger:4317" }
}
```

> **Not:** `OtlpEndpoint` doğrudan Jaeger container'ına işaret eder
> (`docker-compose.yml`'de ayrı bir `otel-collector` servisi yoktur; Jaeger,
> OTLP protokolünü doğrudan kabul eder).

## Doğrulama (docker-compose ayaktayken)

| Bileşen | URL |
|---|---|
| Seq (loglar) | http://localhost:8081 |
| Prometheus (raw metrikler) | http://localhost:9090 |
| Grafana (dashboard) | http://localhost:3000 (admin / admin) |
| Jaeger (trace'ler) | http://localhost:16686 |
| Servisin kendi `/metrics` endpoint'i | http://localhost:5001/metrics (Order.Api) |

## Gerekli NuGet Paketleri (özet)

`Serilog.AspNetCore`, `Serilog.Settings.Configuration`, `Serilog.Sinks.Seq`,
`Serilog.Enrichers.Environment`, `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Instrumentation.AspNetCore/Http/Runtime`,
`OpenTelemetry.Exporter.Prometheus.AspNetCore`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`.

> `Serilog.Settings.Configuration` özellikle önemlidir: `ReadFrom.Configuration()`
> metodu `Serilog.AspNetCore` paketine dahil değildir, ayrı eklenmesi gerekir
> (bkz. repo kökündeki `COMPILE_FIX_NOTES.md`).

## Bilinen Sınırlamalar / Sonraki Adımlar

- Grafana'da henüz otomatik provision edilmiş bir dashboard yok (sadece
  Prometheus datasource'u hazır) — bir sonraki adımda `infra/grafana/provisioning/dashboards/`
  altına hazır bir dashboard JSON'u eklenebilir.
- Loglara `TraceId`/`SpanId` enrichment'ı henüz eklenmedi — Serilog logları ile
  OpenTelemetry trace'lerinin Jaeger/Seq üzerinde çapraz ilişkilendirilmesi
  (log-trace correlation) sonraki bir iyileştirme adımıdır.
