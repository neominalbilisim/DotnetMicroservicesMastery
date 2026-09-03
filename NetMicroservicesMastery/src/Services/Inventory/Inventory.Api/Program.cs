using Inventory.Application;
using BuildingBlocks.Common.Exceptions;
using BuildingBlocks.Observability;
using BuildingBlocks.Messaging.Extensions;
using Microsoft.EntityFrameworkCore;
using Inventory.Infrastructure.Persistence;
using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Resilience;
using BuildingBlocks.Security;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Contracts.Events;
using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Saga;
using BuildingBlocks.Messaging.Idempotency;
using Inventory.Infrastructure.Idempotency;
using Inventory.Api.Consumers;
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
builder.AddServiceObservability(serviceName: "Inventory.Api");

// =====================================================================
// Modül 1: Cross-Cutting Concerns — Global Exception Handling
// =====================================================================
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// =====================================================================
// Modül 4: CQRS (MediatR) — Inventory.Application katmanı kaydı
// =====================================================================
builder.Services.AddInventoryApplication();

// =====================================================================
// Modül 2: Secret Management (Vault)
// Connection string'in şifre kısmı appsettings.json yerine Vault'tan
// okunur ve appsettings üzerine ŞEFFAFÇA uygulanır — bu satırdan SONRA
// GetConnectionString("InventoryDb") çağrısı otomatik olarak güncel değeri döner.
// Vault'a ulaşılamazsa appsettings.json'daki (fallback) değer kullanılır.
// =====================================================================
builder.AddVaultSecrets();

// =====================================================================
// Modül 4/5: Persistence (EF Core + PostgreSQL, database-per-service)
// =====================================================================
builder.Services.AddDbContext<InventoryDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")));

// =====================================================================
// Modül 5: Idempotent Consumer
// ReserveInventoryCommandConsumer'ın (Modül 4 Saga Orchestration), aynı
// mesajı iki kez işleyip stoğu İKİNCİ KEZ rezerve etmesini önler.
// =====================================================================
builder.Services.AddScoped<IIdempotencyStore, InventoryIdempotencyStore>();

// =====================================================================
// Modül 2: Service Discovery (Consul)
// MİMARİ KARAR: JWT doğrulama SADECE API Gateway'de yapılır; bu servis
// (ve diğer downstream servisler) kendi başına Keycloak/JWT doğrulaması
// YAPMAZ — sadece kendini Consul'a kaydeder.
// =====================================================================
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "inventory-service");

// =====================================================================
// Modül 3: MassTransit + Kafka — CONSUMER + Dead Letter Queue
// Inventory.Api, AYNI "order-created" topic'ini FARKLI bir tüketici grubuyla
// ("inventory-service-group") dinler — bu sayede Kafka her iki gruba da
// event'in KENDİ KOPYASINI verir (tüketici grupları birbirinden bağımsızdır;
// Payment.Api'nin bu event'i işlemesi, Inventory.Api'nin de işlemesini
// engellemez — Publish/Subscribe'ın temel özelliği budur). Retry (Polly)
// ve DLQ üretimi, OrderCreatedConsumer'ın kendi içinde yapılır (bkz. o dosya).
// =====================================================================
builder.Services.AddDistributedMessaging(builder.Configuration,
    // Modül 4 - Saga Orchestration: RabbitMQ, Kafka Rider'ın YANINDA devreye
    // girer — Choreography Kafka'da, Orchestration RabbitMQ'da çalışır.
    useRabbitMq: true,
    configureBus: x =>
    {
        x.AddConsumer<ReserveInventoryCommandConsumer>();
        x.AddConsumer<RevertInventoryReservationCommandConsumer>();
    },
    configureRabbitMq: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(SagaQueues.ReserveInventory, e =>
            e.ConfigureConsumer<ReserveInventoryCommandConsumer>(context));
        cfg.ReceiveEndpoint(SagaQueues.RevertInventoryReservation, e =>
            e.ConfigureConsumer<RevertInventoryReservationCommandConsumer>(context));
    },
    configureRider: rider =>
    {
        rider.AddConsumer<OrderCreatedConsumer>();
        // DLQ mesajlarını "order-created-dlq" topic'ine yazacak producer.
        rider.AddProducer<string, OrderCreatedDeadLetterMessage>(KafkaTopics.OrderCreatedDeadLetter);

        // Modül 4 - Saga Pattern: Order.Api'nin başlattığı saga'yı dinler
        // (stok rezervasyonu), sonucu yayınlar; ödeme başarısız olursa
        // Order.Api'den gelecek COMPENSATING command'ı da dinler.
        rider.AddConsumer<OrderSagaStartedConsumer>();
        rider.AddConsumer<ReleaseInventoryCommandConsumer>();
        rider.AddProducer<string, InventoryReservedEvent>(KafkaTopics.InventoryReserved);
        rider.AddProducer<string, InventoryReservationFailedEvent>(KafkaTopics.InventoryReservationFailed);
    },
    configureTopics: (context, k) =>
    {
        k.TopicEndpoint<string, OrderCreatedEvent>(
            KafkaTopics.OrderCreated,
            "inventory-service-group",
            e => e.ConfigureConsumer<OrderCreatedConsumer>(context));

        // Modül 4 - Saga Pattern: ayrı, kendine özgü tüketici grupları.
        k.TopicEndpoint<string, OrderSagaStartedEvent>(
            KafkaTopics.OrderSagaStarted,
            "inventory-service-saga-group",
            e => e.ConfigureConsumer<OrderSagaStartedConsumer>(context));

        k.TopicEndpoint<string, ReleaseInventoryCommand>(
            KafkaTopics.ReleaseInventoryCommand,
            "inventory-service-compensation-group",
            e => e.ConfigureConsumer<ReleaseInventoryCommandConsumer>(context));
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
        builder.Configuration.GetConnectionString("InventoryDb")!,
        name: "postgresql",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "redis",
        tags: ["ready"]);

var app = builder.Build();

// =====================================================================
// Modül 5: Veritabanı Şeması Oluşturma (EnsureCreated) — ProcessedMessages
// tablosu (Idempotent Consumer) dahil.
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    dbContext.Database.EnsureCreated();
}

// =====================================================================
// Modül 2: Load Balancing DEMO — Bu servisin birden fazla instance'ı
// (bkz. docs/Inventory.Api.md "İkinci Instance ile Load Balancing Testi")
// çalıştırıldığında, Gateway üzerinden gelen isteklerin HANGİ instance
// tarafından cevaplandığını görebilmek için TÜM cevaplara bu header eklenir.
// (Gerçek bir üretim ortamında böyle bir header genelde gerekmez; sadece
// bu demo/eğitim senaryosunda load balancing'i gözle görülür kılmak içindir.)
// =====================================================================
var instancePort = builder.Configuration["Consul:ServicePort"] ?? "unknown";
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Instance-Port"] = instancePort;
    await next();
});

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
    service = "Inventory.Api",
    status = "up",
    module = "Ön Hazırlık Dökümanı şablonuna göre iskelet — modüller doldurulacak"
}));

// TODO: Inventory e özgü minimal API endpoint lerini (Endpoints/ klasörü) buraya map leyin.

// =====================================================================
// Modül 5: Idempotent Consumer — DEBUG/TEST endpoint'i
// AYNI MessageId ile ReserveInventoryCommand'ı KENDİ kuyruğuna 2 kez gönderir.
// Beklenen: konsolda ilk mesaj için "🟢 STOK REZERVE EDİLDİ", ikincisi için
// "⏭️ Mesaj DAHA ÖNCE işlendi" satırını görmelisiniz.
// =====================================================================
app.MapPost("/debug/test-idempotency", async (IBus bus) =>
{
    var fixedMessageId = Guid.NewGuid();
    var command = new ReserveInventoryCommand(Guid.NewGuid(), "test-idempotency-customer", 100m);
    var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{SagaQueues.ReserveInventory}"));

    await endpoint.Send(command, ctx => ctx.MessageId = fixedMessageId);
    await Task.Delay(1000);
    await endpoint.Send(command, ctx => ctx.MessageId = fixedMessageId);

    return Results.Ok(new
    {
        messageId = fixedMessageId,
        note = "Aynı MessageId ile 2 kez gönderildi. Inventory.Api konsolunu kontrol edin: ilki işlenmeli (🟢), ikincisi ATLANMALI (⏭️)."
    });
});

// =====================================================================
// Kafka Topic Initialization
// Uygulama startup'ında, tüm topic'leri Kafka'da otomatik oluştur.
// Topic'ler zaten varsa (idempotent) skip edilir.
// =====================================================================
await app.InitializeKafkaTopicsAsync(
    KafkaTopics.OrderSagaStarted,
    KafkaTopics.InventoryReserved,
    KafkaTopics.InventoryReservationFailed,
    KafkaTopics.ReleaseInventoryCommand);

app.Run();
