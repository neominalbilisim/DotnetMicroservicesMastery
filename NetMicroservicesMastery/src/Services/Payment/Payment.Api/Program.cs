using Payment.Application;
using BuildingBlocks.Common.Exceptions;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using Payment.Infrastructure.Persistence;
using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Resilience;
using BuildingBlocks.Security;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Contracts.Events;
using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Saga;
using BuildingBlocks.Messaging.Idempotency;
using Payment.Infrastructure.Idempotency;
using Payment.Api.Consumers;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Modül 1: NET Core ve Kestrel — Self-Hosted Mimari (IIS bağımlılığı yok)
// =====================================================================
builder.WebHost.ConfigureKestrel(options =>
{
    // DoS koruması: açık uçlu request body boyutunu sınırla (10 MB).
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    // Aynı anda kabul edilecek maksimum bağlantı sayısı.
    options.Limits.MaxConcurrentConnections = 100;
    // Boşta kalan (idle) bağlantıların ne kadar süre açık tutulacağı.
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    // İstemcinin header'ları göndermesi için tanınan azami süre (slow-loris koruması).
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    // "Server: Kestrel" response header'ını kapat — bilgi sızıntısını azaltır.
    options.AddServerHeader = false;
});

// =====================================================================
// Modül 1: Merkezi Loglama ve Gözlemlenebilirlik Zinciri (Serilog + OTel)
// =====================================================================
builder.AddServiceObservability(serviceName: "Payment.Api");

// =====================================================================
// Modül 1: Cross-Cutting Concerns — Global Exception Handling
// =====================================================================
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// =====================================================================
// Modül 4: CQRS (MediatR) — Payment.Application katmanı kaydı
// =====================================================================
builder.Services.AddPaymentApplication();

// =====================================================================
// Modül 2: Secret Management (Vault)
// Connection string'in şifre kısmı appsettings.json yerine Vault'tan
// okunur ve appsettings üzerine ŞEFFAFÇA uygulanır — bu satırdan SONRA
// GetConnectionString("PaymentDb") çağrısı otomatik olarak güncel değeri döner.
// Vault'a ulaşılamazsa appsettings.json'daki (fallback) değer kullanılır.
// =====================================================================
builder.AddVaultSecrets();

// =====================================================================
// Modül 4/5: Persistence (EF Core + PostgreSQL, database-per-service)
// =====================================================================
builder.Services.AddDbContext<PaymentDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

// =====================================================================
// Modül 5: Idempotent Consumer
// ChargePaymentCommandConsumer'ın (Modül 4 Saga Orchestration), aynı
// mesajı iki kez işleyip müşteriden İKİNCİ KEZ para çekmesini önler.
// =====================================================================
builder.Services.AddScoped<IIdempotencyStore, PaymentIdempotencyStore>();

// =====================================================================
// Modül 2: Service Discovery (Consul)
// MİMARİ KARAR: JWT doğrulama SADECE API Gateway'de yapılır; bu servis
// (ve diğer downstream servisler) kendi başına Keycloak/JWT doğrulaması
// YAPMAZ — sadece kendini Consul'a kaydeder.
// =====================================================================
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "payment-service");

// =====================================================================
// Modül 3: MassTransit + Kafka — CONSUMER + Dead Letter Queue
// Payment.Api, "order-created" topic'ini "payment-service-group" tüketici
// grubuyla dinler. Retry (Polly, 3 deneme, 5sn arayla) ve DLQ üretimi,
// OrderCreatedConsumer'ın KENDİ İÇİNDE yapılır (bkz. o dosyadaki gerekçe:
// MassTransit'in UseMessageRetry/Fault<T>'i, bu in-memory+Kafka Rider
// kombinasyonunda güvenilir çalışmadı). Tüm denemeler tükenirse orijinal
// mesaj + hata sebebi "order-created-dlq" topic'ine yazılır.
// =====================================================================
builder.Services.AddDistributedMessaging(builder.Configuration,
    // Modül 4 - Saga Orchestration: RabbitMQ, Kafka Rider'ın YANINDA devreye
    // girer — Choreography Kafka'da, Orchestration RabbitMQ'da çalışır.
    useRabbitMq: true,
    configureBus: x => x.AddConsumer<ChargePaymentCommandConsumer>(),
    configureRabbitMq: (context, cfg) => cfg.ReceiveEndpoint(SagaQueues.ChargePayment,
        e => e.ConfigureConsumer<ChargePaymentCommandConsumer>(context)),
    configureRider: rider =>
    {
        rider.AddConsumer<OrderCreatedConsumer>();
        // Modül 3 - Command consumer: SADECE Payment.Api bu topic'i dinler
        // (Inventory.Api dinlemez) — Event/Command ayrımının somut kanıtı.
        rider.AddConsumer<ProcessPaymentCommandConsumer>();
        // DLQ mesajlarını "order-created-dlq" topic'ine yazacak producer.
        rider.AddProducer<string, OrderCreatedDeadLetterMessage>(KafkaTopics.OrderCreatedDeadLetter);

        // Modül 4 - Saga Pattern: Inventory.Api'nin rezervasyon başarılı
        // event'ini dinler, ödemeyi işler ve sonucu (başarı/hata) yayınlar.
        rider.AddConsumer<InventoryReservedConsumer>();
        rider.AddProducer<string, PaymentCompletedEvent>(KafkaTopics.PaymentCompleted);
        rider.AddProducer<string, PaymentFailedEvent>(KafkaTopics.PaymentFailed);
    },
    configureTopics: (context, k) =>
    {
        k.TopicEndpoint<string, OrderCreatedEvent>(
            KafkaTopics.OrderCreated,
            "payment-service-group",
            e => e.ConfigureConsumer<OrderCreatedConsumer>(context));

        // Modül 3 - Command topic'i: ayrı, kendine özgü bir tüketici grubu
        // ("payment-service-commands-group") — Event topic'inden bağımsız.
        k.TopicEndpoint<string, ProcessPaymentCommand>(
            KafkaTopics.ProcessPaymentCommand,
            "payment-service-commands-group",
            e => e.ConfigureConsumer<ProcessPaymentCommandConsumer>(context));

        // Modül 4 - Saga Pattern: ayrı, kendine özgü bir tüketici grubu.
        k.TopicEndpoint<string, InventoryReservedEvent>(
            KafkaTopics.InventoryReserved,
            "payment-service-saga-group",
            e => e.ConfigureConsumer<InventoryReservedConsumer>(context));
    });

// =====================================================================
// Modül 5: Hangfire (bu API projesinde sadece Dashboard/enqueue; asıl
// worker JobService projesindedir) — TODO
// =====================================================================
// builder.Services.AddHangfire(cfg => cfg.UsePostgreSqlStorage(...));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    // "ready" etiketi: bu bağımlılıklardan biri çökerse servis trafik almaya hazır değildir.
    .AddNpgSql(
        builder.Configuration.GetConnectionString("PaymentDb")!,
        name: "postgresql",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "redis",
        tags: ["ready"]);

var app = builder.Build();

// =====================================================================
// Modül 5: Veritabanı Şeması Oluşturma (EnsureCreated — bkz.
// docs/Order.Infrastructure.md "Migrations Değil" notu, aynı yaklaşım
// burada da geçerli). ProcessedMessages tablosu (Idempotent Consumer) da
// dahil olmak üzere şema burada oluşturulur.
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseExceptionHandler();
app.UseServiceObservability();

// Hem lokal ("Development") hem container ("Docker") ortamında Swagger
// açık kalsın diye IsDevelopment() yerine IsProduction() negatif kontrolü kullanıldı.
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Liveness: process ayakta mı? (bağımlılıklar kontrol edilmez — orkestratör
// bunu "restart et mi?" kararı için kullanır.)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

// Readiness: Postgres/Redis gibi kritik bağımlılıklar da dahil tam kontrol
// (orkestratör bunu "trafik gönderilsin mi?" kararı için kullanır.)
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

// Genel amaçlı / manuel inceleme için tüm health check'lerin tam dökümü.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Payment.Api",
    status = "up",
    module = "Ön Hazırlık Dökümanı şablonuna göre iskelet — modüller doldurulacak"
}));

// TODO: Payment e özgü minimal API endpoint lerini (Endpoints/ klasörü) buraya map leyin.

// =====================================================================
// Modül 5: Idempotent Consumer — DEBUG/TEST endpoint'i
// AYNI MessageId ile ChargePaymentCommand'ı KENDİ kuyruğuna 2 kez gönderir.
// Beklenen: konsolda ilk mesaj için "🟢 ÖDEME ALINDI", ikincisi için
// "⏭️ Mesaj DAHA ÖNCE işlendi" satırını görmelisiniz — ödeme mantığı
// İKİNCİ SEFER HİÇ ÇALIŞMAZ.
// =====================================================================
app.MapPost("/debug/test-idempotency", async (IBus bus) =>
{
    var fixedMessageId = Guid.NewGuid();
    var command = new ChargePaymentCommand(Guid.NewGuid(), "test-idempotency-customer", 100m);
    var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{SagaQueues.ChargePayment}"));

    await endpoint.Send(command, ctx => ctx.MessageId = fixedMessageId);
    await Task.Delay(1000); // ilk mesajın işlenmesi için kısa bir bekleme
    await endpoint.Send(command, ctx => ctx.MessageId = fixedMessageId);

    return Results.Ok(new
    {
        messageId = fixedMessageId,
        note = "Aynı MessageId ile 2 kez gönderildi. Payment.Api konsolunu kontrol edin: ilki işlenmeli (🟢), ikincisi ATLANMALI (⏭️)."
    });
});

app.Run();
