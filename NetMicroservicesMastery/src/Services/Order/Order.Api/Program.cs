using Order.Application;
using Order.Application.Abstractions;
using Order.Application.Commands;
using Order.Application.Queries;
using Order.Application.Requests;
using Order.Infrastructure.Messaging;
using Order.Infrastructure.Repositories;
using Order.Infrastructure.Configuration;
using Order.Api.Consumers;
using BuildingBlocks.Common.Exceptions;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure.Persistence;
using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Resilience;
using BuildingBlocks.Security;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Contracts.Events;
using BuildingBlocks.Messaging.Contracts.Commands;
using BuildingBlocks.Messaging.Contracts.Saga;
using MediatR;
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
builder.AddServiceObservability(serviceName: "Order.Api");

// =====================================================================
// Modül 1: Cross-Cutting Concerns — Global Exception Handling
// =====================================================================
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// =====================================================================
// Modül 4: CQRS (MediatR) — Order.Application katmanı kaydı
// (MediatR + FluentValidation + ValidationBehavior pipeline'ı içerir)
// =====================================================================
builder.Services.AddOrderApplication();

// =====================================================================
// Modül 2: Secret Management (Vault)
// Connection string'in şifre kısmı appsettings.json yerine Vault'tan
// okunur ve appsettings üzerine ŞEFFAFÇA uygulanır — bu satırdan SONRA
// GetConnectionString("OrderDb") çağrısı otomatik olarak güncel değeri döner.
// Vault'a ulaşılamazsa appsettings.json'daki (fallback) değer kullanılır.
// =====================================================================
builder.AddVaultSecrets();

// =====================================================================
// Modül 4/5: Persistence (EF Core + PostgreSQL, database-per-service)
// =====================================================================
builder.Services.AddDbContext<OrderDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")));
// IOrderRepository (Application) -> OrderRepository (Infrastructure, EF Core).
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// =====================================================================
// Modül 2: Service Discovery (Consul)
// MİMARİ KARAR: JWT doğrulama SADECE API Gateway'de yapılır; bu servis
// (ve diğer downstream servisler) kendi başına Keycloak/JWT doğrulaması
// YAPMAZ — sadece kendini Consul'a kaydeder.
// =====================================================================
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "order-service");

// =====================================================================
// Modül 5: Consul KV — Merkezi/Dinamik Konfigürasyon
// appsettings.json'daki değerlerin aksine, bu değer servis yeniden
// BAŞLATILMADAN Consul KV üzerinden değiştirilebilir (bkz.
// ConsulKvConfigurationWatcher — her 10sn'de bir Consul'dan okur).
// =====================================================================
builder.Services.AddSingleton<OrderDynamicConfiguration>();
builder.Services.AddSingleton<IOrderDynamicConfiguration>(sp => sp.GetRequiredService<OrderDynamicConfiguration>());
builder.Services.AddHostedService<ConsulKvConfigurationWatcher>();

// =====================================================================
// Modül 2: Resiliency Patterns (Polly) — DEMO
// Order.Api'nin Inventory.Api'yi çağırdığı (örn. stok kontrolü — asıl iş
// mantığı Modül 4'te Saga/Orchestration ile eklenecek) senaryoyu temsilen
// "InventoryClient" adında, Retry+CircuitBreaker+Fallback pipeline'ı
// uygulanmış bir HttpClient tanımlanır. Test etmek için: GET /test-resiliency
// =====================================================================
builder.Services.AddHttpClient("InventoryClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:InventoryApiBaseUrl"]
        ?? "http://inventory-api:8080");
}).AddStandardResiliency();

// =====================================================================
// Modül 3: MassTransit + Kafka — PRODUCER (kayıt gerekir, çünkü
// OutboxProcessorHostedService gerçek iletim sırasında bu producer'ları
// DI'dan çözer — bkz. Modül 4 aşağıdaki Outbox bölümü).
// =====================================================================
builder.Services.AddDistributedMessaging(builder.Configuration,
    // Modül 4 - Saga Orchestration: RabbitMQ, Kafka Rider'ın YANINDA devreye
    // girer (birbirini etkilemez) — Choreography örneği Kafka'da, Orchestration
    // örneği RabbitMQ'da çalışmaya devam eder.
    useRabbitMq: true,
    configureBus: x =>
    {
        x.AddConsumer<OrderSagaOrchestratorCompletedConsumer>();
        x.AddConsumer<OrderSagaOrchestratorFailedConsumer>();
    },
    configureRabbitMq: (context, cfg) =>
    {
        cfg.ReceiveEndpoint(SagaQueues.OrderSagaCompleted, e =>
            e.ConfigureConsumer<OrderSagaOrchestratorCompletedConsumer>(context));
        cfg.ReceiveEndpoint(SagaQueues.OrderSagaFailed, e =>
            e.ConfigureConsumer<OrderSagaOrchestratorFailedConsumer>(context));
    },
    configureRider: rider =>
    {
        rider.AddProducer<string, OrderCreatedEvent>(KafkaTopics.OrderCreated);
        rider.AddProducer<string, ProcessPaymentCommand>(KafkaTopics.ProcessPaymentCommand);

        // Modül 4 - Saga Pattern: Order.Api hem üretir (saga'yı başlatır +
        // compensation gönderir) hem tüketir (saga sonuç event'lerini dinler).
        rider.AddProducer<string, OrderSagaStartedEvent>(KafkaTopics.OrderSagaStarted);
        rider.AddProducer<string, ReleaseInventoryCommand>(KafkaTopics.ReleaseInventoryCommand);
        rider.AddConsumer<PaymentCompletedConsumer>();
        rider.AddConsumer<PaymentFailedConsumer>();
        rider.AddConsumer<InventoryReservationFailedConsumer>();
    },
    configureTopics: (context, k) =>
    {
        k.TopicEndpoint<string, PaymentCompletedEvent>(
            KafkaTopics.PaymentCompleted, "order-service-saga-group",
            e => e.ConfigureConsumer<PaymentCompletedConsumer>(context));

        k.TopicEndpoint<string, PaymentFailedEvent>(
            KafkaTopics.PaymentFailed, "order-service-saga-group",
            e => e.ConfigureConsumer<PaymentFailedConsumer>(context));

        k.TopicEndpoint<string, InventoryReservationFailedEvent>(
            KafkaTopics.InventoryReservationFailed, "order-service-saga-group",
            e => e.ConfigureConsumer<InventoryReservationFailedConsumer>(context));
    });

// =====================================================================
// Modül 4: Outbox Pattern (AYRI DEMO)
// "/submit-order" bunu KULLANMAZ — sadece "/submit-order-outbox" (dedike
// test endpoint'i) kullanır. IOrderEventPublisher (yukarıda kaydedildi)
// Kafka'ya DOĞRUDAN üretmeye devam eder; IOutboxOrderEventPublisher ise
// aynı OrderDbContext'in OutboxMessages tablosuna yazar. Gerçek Kafka'ya
// iletim, ayrı bir arka plan servisi (OutboxProcessorHostedService)
// tarafından periyodik olarak yapılır (bkz. docs/Order.Infrastructure.md).
// =====================================================================
builder.Services.AddScoped<IOrderEventPublisher, KafkaOrderEventPublisher>();
builder.Services.AddScoped<IOutboxOrderEventPublisher, OutboxOrderEventPublisher>();
// =====================================================================
// Modül 5: Distributed Lock (Redis/RedLock.net)
// OutboxProcessorHostedService, birden fazla Order.Api instance'ı
// çalıştığında aynı outbox satırının iki instance tarafından aynı anda
// işlenmesini bu kilit ile önler (bkz. docs/Order.Infrastructure.md).
// =====================================================================
builder.Services.AddSingleton<IDistributedLockService, DistributedLockService>();
builder.Services.AddHostedService<OutboxProcessorHostedService>();

// =====================================================================
// Modül 4: Saga Pattern (Choreography) — AYRI DEMO
// "/submit-order" ve "/submit-order-outbox"tan tamamen izole, kendi
// topic'leri ve kendi Command/Consumer'larıyla. Bkz. docs/BuildingBlocks.Messaging.md.
// =====================================================================
builder.Services.AddScoped<ISagaEventPublisher, KafkaSagaEventPublisher>();

// =====================================================================
// Modül 4: Saga Pattern (ORCHESTRATION) — AYRI DEMO (RabbitMQ + Saga.Api)
// "/submit-order", "/submit-order-outbox" ve "/submit-order-saga" (Choreography)
// hepsinden izole. Order.Api burada sadece isteği alıp RabbitMQ ile Saga.Api'ye
// iletir — orkestrasyon mantığının TAMAMI Saga.Api'dedir.
// =====================================================================
builder.Services.AddScoped<IOrderSagaOrchestratorClient, RabbitMqOrderSagaOrchestratorClient>();

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
        builder.Configuration.GetConnectionString("OrderDb")!,
        name: "postgresql",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "redis",
        tags: ["ready"]);

var app = builder.Build();

// =====================================================================
// Modül 4: Veritabanı Şeması Oluşturma
// Bu proje henüz EF Core Migrations kullanmıyor (basitlik için) — bunun
// yerine EnsureCreated() ile model'e göre şema (Orders, OutboxMessages
// tabloları) YOKSA oluşturulur. NOT: EnsureCreated() sadece VERİTABANI
// TAMAMEN BOŞSA çalışır — DB'de zaten bazı tablolar varsa (örn. daha önce
// Orders oluşmuş ama OutboxMessages sonradan eklenmiş) eksik tabloyu
// EKLEMEZ. Böyle bir "relation does not exist" hatası alırsanız, order_db
// veritabanını sıfırlayıp (DROP + yeniden CREATE) yeniden deneyin — bkz.
// docs/Order.Infrastructure.md.
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
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
    service = "Order.Api",
    status = "up",
    module = "Modül 4 — CQRS (MediatR) aktif"
}));

// =====================================================================
// Modül 2: Resiliency (Polly) — DEMO/TEST endpoint'i
// Inventory.Api'ye "InventoryClient" (Retry+CircuitBreaker+Fallback
// pipeline'lı) HttpClient üzerinden istek atar. Inventory.Api ayaktaysa
// onun /health/ready cevabını, ayakta değilse (birkaç retry sonrası)
// Fallback'in ürettiği 503 JSON'ı görürsünüz — ikisi de "başarılı" bir
// pipeline çalışmasıdır, uygulama asla ham bir exception'la çökmez.
// =====================================================================
app.MapGet("/test-resiliency", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("InventoryClient");
    var response = await client.GetAsync("/health/ready");
    var body = await response.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
});

// =====================================================================
// Modül 4: CQRS — COMMAND endpoint'i (SADE — Outbox YOK)
// Artık iş mantığının (agregat oluşturma, DB'ye kaydetme, Kafka'ya
// DOĞRUDAN yayınlama) TAMAMI CreateOrderCommandHandler içinde. Bu endpoint
// sadece HTTP <-> MediatR köprüsüdür ("ince controller" ilkesi).
// Validasyon (CreateOrderCommandValidator), handler çalışmadan ÖNCE
// ValidationBehavior tarafından otomatik uygulanır.
// Outbox Pattern'i (atomiklik garantisi) izole test etmek isterseniz:
// bkz. aşağıdaki POST /submit-order-outbox.
// =====================================================================
app.MapPost("/submit-order", async (SubmitOrderRequest request, IMediator mediator) =>
{
    var command = new CreateOrderCommand(request.CustomerId, request.TotalAmount, request.OrderId);
    var result = await mediator.Send(command);

    return Results.Accepted(value: new
    {
        orderId = result.OrderId,
        eventTopic = KafkaTopics.OrderCreated,
        commandTopic = KafkaTopics.ProcessPaymentCommand
    });
});

// =====================================================================
// Modül 4: Outbox Pattern — DEDİKE DEMO/TEST endpoint'i
// "/submit-order"dan BİLİNÇLİ olarak ayrı tutuldu — DB yazımı ile mesaj
// yayınlamanın ATOMİK hale getirilmesini izole bir şekilde göstermek/test
// etmek içindir. İş mantığı SubmitOrderWithOutboxCommandHandler'dadır.
// =====================================================================
app.MapPost("/submit-order-outbox", async (SubmitOrderRequest request, IMediator mediator) =>
{
    var command = new SubmitOrderWithOutboxCommand(request.CustomerId, request.TotalAmount, request.OrderId);
    var result = await mediator.Send(command);

    return Results.Accepted(value: new
    {
        orderId = result.OrderId,
        note = "Mesajlar önce OutboxMessages tablosuna yazıldı; gerçek Kafka'ya iletim birkaç saniye içinde OutboxProcessorHostedService tarafından yapılacak. Durumu GET /debug/outbox ile izleyin."
    });
});

// =====================================================================
// Modül 4: CQRS — QUERY endpoint'i
// =====================================================================
app.MapGet("/orders/{orderId:guid}", async (Guid orderId, IMediator mediator) =>
{
    var order = await mediator.Send(new GetOrderByIdQuery(orderId));
    return order is null ? Results.NotFound() : Results.Ok(order);
});

// =====================================================================
// Modül 4: Saga Pattern (Choreography) — DEDİKE DEMO/TEST endpoint'i
// Akış: Order.Api (başlatır) -> Inventory.Api (stok rezerve eder)
//     -> Payment.Api (öder) -> Order.Api (onaylar VEYA telafi tetikler)
// Test senaryoları (customerId ile tetiklenir):
//   normal değer      -> ✅ tam başarı (Confirmed)
//   "FAIL_INVENTORY"  -> ❌ stok yok, compensation YOK (Cancelled)
//   "FAIL_PAYMENT"    -> ❌ ödeme reddi + COMPENSATION (Cancelled + stok iade)
// Sonucu GET /orders/{orderId} ile izleyin (Status alanı).
// =====================================================================
app.MapPost("/submit-order-saga", async (SubmitOrderRequest request, IMediator mediator) =>
{
    var command = new StartOrderSagaCommand(request.CustomerId, request.TotalAmount, request.OrderId);
    var result = await mediator.Send(command);

    return Results.Accepted(value: new
    {
        orderId = result.OrderId,
        note = "Saga başlatıldı (Status: AwaitingInventory). Sonucu birkaç saniye içinde GET /orders/{orderId} ile izleyin."
    });
});

// =====================================================================
// Modül 4: Saga Pattern (ORCHESTRATION) — DEDİKE DEMO/TEST endpoint'i
// "/submit-order-saga" (Choreography) ile KARIŞTIRILMAMALIDIR. Bu, işi
// TAMAMEN ayrı bir servise (Saga.Api) devreden orkestrasyon versiyonudur.
// Test senaryoları (customerId ile) Choreography ile AYNI konvansiyonu kullanır.
// Saga'nın canlı durumunu Saga.Api'de izleyebilirsiniz: GET Saga.Api:5004/debug/sagas/{orderId}
// =====================================================================
app.MapPost("/submit-order-saga-orchestrator", async (SubmitOrderRequest request, IMediator mediator) =>
{
    var command = new StartOrderSagaOrchestratorCommand(request.CustomerId, request.TotalAmount, request.OrderId);
    var result = await mediator.Send(command);

    return Results.Accepted(value: new
    {
        orderId = result.OrderId,
        note = "Saga.Api'ye iletildi (Status: AwaitingInventory). Sonucu GET /orders/{orderId} veya Saga.Api'de GET /debug/sagas/{orderId} ile izleyin."
    });
});

// =====================================================================
// Modül 5: Consul KV — DEBUG/TEST endpoint'i
// Anlık olarak hangi MaxOrderAmount değerinin kullanıldığını gösterir —
// Consul UI'dan değeri değiştirip birkaç saniye sonra bu endpoint'i
// tekrar çağırarak canlı güncellemeyi doğrulayabilirsiniz.
// =====================================================================
app.MapGet("/debug/config", (IOrderDynamicConfiguration dynamicConfiguration) =>
    Results.Ok(new { maxOrderAmount = dynamicConfiguration.MaxOrderAmount }));

// =====================================================================
// Modül 4: Outbox Pattern — DEBUG/TEST endpoint'i
// OutboxMessages tablosunun o anki durumunu gösterir (hangi mesajlar
// bekliyor, hangileri iletildi). Sadece test/gözlem amaçlıdır.
// =====================================================================
app.MapGet("/debug/outbox", async (OrderDbContext dbContext) =>
{
    var messages = await dbContext.OutboxMessages
        .OrderByDescending(m => m.OccurredOnUtc)
        .Take(20)
        .Select(m => new
        {
            m.Id,
            m.Topic,
            m.PartitionKey,
            m.OccurredOnUtc,
            m.ProcessedOnUtc,
            Status = m.ProcessedOnUtc == null ? "Bekliyor" : "İletildi",
            m.Error
        })
        .ToListAsync();

    return Results.Ok(messages);
});

app.Run();
