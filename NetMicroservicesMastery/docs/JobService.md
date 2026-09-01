# JobService

**Modül:** Modül 2 (Consul kaydı) + Modül 5 (Hangfire)

## Modül 2: Consul Kaydı + Health Check

Diğer tüm servisler gibi JobService de ayağa kalktığında kendini Consul'a
kaydeder (`Consul:ServiceName: "job-service"`). Bu kayıt **envanter/gözlem**
amaçlıdır — JobService bir arka plan worker'ı olduğundan (HTTP trafiği almaz),
hiçbir servis onu Consul üzerinden "keşfetmez"; sadece Consul UI'da
ekosistemin tamamının (gateway + 3 API + job service) tek yerden görünür
olmasını sağlar.

```json
{
  "Consul": {
    "Address": "http://consul:8500",
    "ServiceName": "job-service",
    "ServiceAddress": "job-service",
    "ServicePort": 8080
  }
}
```

Health check zinciri (`/health/live`, `/health/ready`, `/health`), Order/Payment/
Inventory'deki ortak `HealthCheckResponseWriter` ile aynı JSON formatını
kullanır (bkz. `docs/BuildingBlocks.Common.md`), ancak şu an için gerçek bir
bağımlılık kontrolü (Postgres/Redis) eklenmedi — bu, Modül 5 çalışmasında
Hangfire ile birlikte eklenecektir.

## Doğrulama

```bash
curl http://localhost:5010/health/ready
```

`http://localhost:8500` (Consul UI) → Services listesinde `job-service`'in
yeşil (healthy) göründüğünü doğrulayın.

## Bilinen Sınırlamalar / Sonraki Adımlar (Modül 5)

- ~~Hangfire henüz kurulmadı~~ ✅ Tamamlandı — bkz. aşağıdaki bölüm.
- ~~Distributed Lock (Redis/Redlock) henüz implemente edilmedi~~ ✅ Tamamlandı — bkz. aşağıdaki bölüm.
- ~~Gerçek bir recurring job örneği henüz çalışır durumda değil~~ ✅ Tamamlandı — `ExampleReportJob`.

---

## Modül 5: Hangfire

### 3 Job Tipi

| Tip | Nasıl Tetiklenir | Örnek Kullanım |
|---|---|---|
| **Fire-and-forget** | Elle, `IBackgroundJobClient.Enqueue()` | "Hemen arka planda yap, sonucunu bekleme" |
| **Delayed** | Elle, `IBackgroundJobClient.Schedule()` | "N süre sonra çalıştır" |
| **Recurring** | Otomatik, `IRecurringJobManager.AddOrUpdate()` + Cron ifadesi | "Her gün/saat/dakika tekrar et" |

Üçü de **aynı iş sınıfını** (`IReportJob` / `ExampleReportJob`) çalıştırır.

### Kurulum

```csharp
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));

builder.Services.AddHangfireServer(); // Bu process'in KENDİSİ de bir worker olsun diye.
```

`AddHangfire` sadece storage'a **yazmayı** sağlar; job'ların GERÇEKTEN
**çalıştırılması** için `AddHangfireServer()` de gereklidir. Hangfire,
`hangfire_db`'deki kendi tablolarını **otomatik olarak** oluşturur — elle
bir `EnsureCreated()` adımına gerek yoktur.

### Dashboard

```
http://localhost:5010/hangfire
```

Tüm job'ları (bekleyen, çalışan, tamamlanan, başarısız), recurring job
zamanlamalarını ve geçmiş çalıştırmaları görsel olarak izleyebilirsiniz.

> ⚠️ **Güvenlik notu:** Bu eğitim projesinde Dashboard'a **herkesin**
> erişebilmesi için `Authorization = []` verildi (varsayılan davranış
> sadece localhost'tan erişime izin verir, Docker'da sorun çıkarabilir).
> **Üretimde mutlaka** bir `IDashboardAuthorizationFilter` eklenmelidir.

### Distributed Lock Entegrasyonu

`ExampleReportJob`, Order.Api'nin `OutboxProcessorHostedService`'inin
kullandığı **AYNI** `IDistributedLockService` implementasyonunu kullanır —
artık paylaşılan bir BuildingBlock (`BuildingBlocks.Resilience`). Bu,
JobService birden fazla instance ile çalıştırıldığında, aynı recurring
job'un iki instance tarafından aynı anda çalıştırılmasını önler:

```csharp
public async Task RunAsync(CancellationToken ct = default)
{
    using var @lock = await distributedLockService.TryAcquireAsync(
        "job-service:example-report-job", TimeSpan.FromMinutes(2), ct);

    if (@lock is null)
    {
        return; // Başka bir instance zaten çalıştırıyor.
    }

    // ... iş mantığı ...
}
```

> **Not:** Hangfire'ın kendi storage'ı da recurring job'ların birden fazla
> worker tarafından aynı anda alınmasını büyük ölçüde engelleyen bir
> mekanizmaya zaten sahiptir. Buradaki kullanım, Modül 5'in "Distributed
> Lock" konusunu Hangfire bağlamında da somut göstermek içindir.

**`IDistributedLockService`/`DistributedLockService` artık `BuildingBlocks.Resilience`'ta**
(daha önce sadece `Order.Infrastructure`'daydı) — hem Order.Api hem
JobService aynı kodu kullanıyor, kopyalamıyor.

### Doğrulama

```bash
# Fire-and-forget — hemen kuyruğa alınır
curl -X POST http://localhost:5010/jobs/fire-and-forget

# Delayed — 30 saniye sonra çalışır
curl -X POST http://localhost:5010/jobs/delayed
```

Her iki durumda da JobService konsolunda birkaç saniye içinde:
```
🔒 [JobService] ExampleReportJob BAŞLADI — ...
✅ [JobService] ExampleReportJob TAMAMLANDI — ...
```

**Recurring job**'u görmek için hiçbir şey yapmanıza gerek yok —
`Cron.Minutely()` ile her dakika otomatik çalışır (Dashboard → Recurring
Jobs → "Trigger now" ile beklemeden de tetikleyebilirsiniz).

### Bilinen Sınırlamalar

- `ExampleReportJob` gerçek bir iş yapmaz (2 saniye bekler, log basar).
- Hangfire Dashboard, üretimde mutlaka kimlik doğrulama arkasına alınmalıdır.
