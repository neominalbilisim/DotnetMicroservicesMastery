using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Observability;
using BuildingBlocks.Resilience;
using BuildingBlocks.Security;
using Hangfire;
using Hangfire.PostgreSql;
using JobService.Jobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TimeZoneConverter;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Modül 1: Gözlemlenebilirlik — Serilog + OpenTelemetry (Prometheus/Jaeger)
// =====================================================================
builder.AddServiceObservability(serviceName: "JobService");

// =====================================================================
// Modül 2: Service Discovery (Consul)
// "Tüm servisler" ilkesi gereği JobService de kendini Consul'a kaydeder
// (Consul UI'da tüm ekosistemin tek bir yerden görünür olması için) —
// ancak JobService bir arka plan worker'ı olduğundan, ona HTTP üzerinden
// istek yönlendiren (YARP gibi) bir bileşen yoktur; kayıt salt gözlem/
// envanter amaçlıdır.
// =====================================================================
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "job-service");

// =====================================================================
// Modül 5: Hangfire — PostgreSQL destekli job scheduling
// Fire-and-forget, delayed (gecikmeli), recurring (zamanlanmış/tekrarlayan)
// ve continuation (zincirleme) job'ları destekler. Storage PostgreSQL
// olduğu için, JobService yeniden başlasa (veya birden fazla instance
// çalışsa) bile bekleyen/zamanlanmış job'lar KAYBOLMAZ — veritabanında kalıcıdır.
// =====================================================================
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 3, DelaysInSeconds = new[] { 5, 10, 15 } });

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180) 
    // Hangfire 1.8.0 ile gelen yeni veri modelini kullanır (önceki sürümlerdeki veri modeliyle uyumluluk sağlar)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(builder.Configuration.GetConnectionString("HangfireDb")))
    );



// Hangfire Server: bu process'in KENDİSİNİN de bir worker olarak job
// kuyruğunu dinleyip işlemesini sağlar (storage'a sadece job YAZMAK
// için AddHangfire yeterlidir; job'ları GERÇEKTEN ÇALIŞTIRMAK için
// AddHangfireServer gereklidir).
builder.Services.AddHangfireServer(options =>
{
    // Kuyruk isimleri, JobService'in işlediği job türlerine göre ELLE ayarlandı, sıraya göre çalışacak (öncelik: critical > default > low).
    options.Queues = new[] { "critical","default","low" };
    // Worker sayısı, CPU çekirdek sayısının 5 katı olarak ayarlandı — bu
    options.WorkerCount = Environment.ProcessorCount * 5;
});

// =====================================================================
// Modül 5: Multi-Instance Senaryoları (Concurrency) — Distributed Lock
// BuildingBlocks.Resilience'taki PAYLAŞILAN implementasyon — Order.Api'nin
// OutboxProcessorHostedService'inin kullandığı İLE AYNI kod.
// =====================================================================
builder.Services.AddSingleton<IDistributedLockService, DistributedLockService>();

// Hangfire, job sınıflarını DI container'dan çözer (resolve eder) —
// bu yüzden IReportJob burada kaydedilmelidir.
builder.Services.AddScoped<IReportJob, ExampleReportJob>();

builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseServiceObservability();

// =====================================================================
// Modül 5: Hangfire Dashboard
// ⚠️ Bu eğitim/demo projesinde Dashboard'a HERKESİN erişebilmesi için
// Authorization filtresi BİLİNÇLİ OLARAK boş bırakıldı (varsayılan
// davranış sadece localhost'tan erişime izin verir — Docker'da container
// dışından erişimi engelleyebilir). ÜRETİMDE MUTLAKA bir
// IDashboardAuthorizationFilter (örn. sadece admin rolü) eklenmelidir.
// =====================================================================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = []
});

// =====================================================================
// Modül 5: Recurring Job Kaydı
// Uygulama her başladığında AddOrUpdate çağrılır — Hangfire bunu
// "idempotent" şekilde ele alır (aynı isimde zaten varsa günceller,
// yoksa oluşturur; tekrar tekrar kayıt oluşturmaz).
// NOT: Demo/test amaçlı Cron.Minutely() (her dakika) kullanıldı — gerçek
// bir üretim senaryosunda muhtemelen Cron.Daily() gibi bir şey olması daha uygun olur.
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<IReportJob>(
        "example-recurring-report",
        job => job.RunAsync(CancellationToken.None),
        Cron.Minutely(), // Demo amaçlı her dakika çalışacak şekilde ayarlandı
                         // "0 8 * * *", // Her gün 08:00 (Türkiye saatine göre)
        new RecurringJobOptions
        {
            // Hangfire, job'ları UTC saat diliminde çalıştırır;
            // bu yüzden Türkiye saat dilimine göre ayarlamak için TimeZoneInfo kullanılır.
            // Linux ve Windows ortamlarında TimeZoneInfo farklılıkları olabileceği için TZConvert kütüphanesi kullanılır.
            TimeZone = TZConvert.GetTimeZoneInfo("Turkey Standard Time")
        });
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapGet("/", () => Results.Ok(new
{
    service = "JobService",
    status = "up",
    module = "Modül 5 — Hangfire aktif",
    dashboard = "/hangfire"
}));

// =====================================================================
// Modül 5: Fire-and-Forget ve Delayed Job — DEMO/TEST endpoint'leri
// Recurring job zaten otomatik (her dakika) çalışır; bu ikisi ELLE
// tetiklenir. IBackgroundJobClient/IRecurringJobManager, Hangfire'ın
// statik BackgroundJob/RecurringJob facade'leri yerine DI-dostu API'sidir.
// =====================================================================
app.MapPost("/jobs/fire-and-forget", (IBackgroundJobClient jobClient) =>
{
    // Fire-and-forget: HEMEN (ilk müsait worker'a) kuyruğa alınır, çağıran
    // taraf sonucu beklemez — job kimliği (jobId) ile Dashboard'dan takip edilebilir.
    
    var jobId = jobClient.Enqueue<IReportJob>("critical",job => job.RunAsync(CancellationToken.None));
    return Results.Ok(new { jobId, type = "fire-and-forget", note = "Hemen kuyruğa alındı. Dashboard: /hangfire" });
});

app.MapPost("/jobs/delayed", (IBackgroundJobClient jobClient) =>
{
    // Delayed: belirtilen süre kadar beklendikten SONRA kuyruğa alınır.
    var delay = TimeSpan.FromSeconds(30);
    var jobId = jobClient.Schedule<IReportJob>("low", job => job.RunAsync(CancellationToken.None), delay);
    return Results.Ok(new { jobId, type = "delayed", delay = delay.ToString(), note = "30 saniye sonra çalışacak. Dashboard: /hangfire" });
});

app.Run();
