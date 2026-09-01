using BuildingBlocks.Common.Exceptions;
using BuildingBlocks.Common.HealthChecks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts.Saga;
using BuildingBlocks.Observability;
using BuildingBlocks.Security;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Saga.Api.Persistence;
using Saga.Api.Sagas;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Modül 1: NET Core ve Kestrel — Self-Hosted Mimari
// =====================================================================
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    options.Limits.MaxConcurrentConnections = 100;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.AddServerHeader = false;
});

// =====================================================================
// Modül 1: Merkezi Loglama ve Gözlemlenebilirlik Zinciri (Serilog + OTel)
// =====================================================================
builder.AddServiceObservability(serviceName: "Saga.Api");

// =====================================================================
// Modül 1: Cross-Cutting Concerns — Global Exception Handling
// =====================================================================
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// =====================================================================
// Modül 4/5: Persistence (EF Core + PostgreSQL, database-per-service: saga_db)
// =====================================================================
builder.Services.AddDbContext<SagaDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("SagaDb")));

// =====================================================================
// Modül 2: Service Discovery (Consul)
// =====================================================================
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "saga-service");

// =====================================================================
// Modül 4: Saga Pattern (ORCHESTRATION) — MassTransit State Machine + RabbitMQ
// RabbitMQ, MassTransit'in Saga/State Machine desteğinin NATIVE ve
// kanıtlanmış çalıştığı transport'tur (bkz. docs/Saga.Api.md "Neden
// RabbitMQ" bölümü). Saga, EF Core repository ile kalıcı hale getirilir
// (OrderSagaState tablosu) — bu sayede Saga.Api yeniden başlasa bile
// devam eden saga'lar kaybolmaz.
// =====================================================================
builder.Services.AddRabbitMqMessaging(builder.Configuration,
    configureBus: x =>
    {
        x.AddSagaStateMachine<OrderSagaStateMachine, OrderSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ExistingDbContext<SagaDbContext>();
                r.UsePostgres();
            });
    },
    configureRabbitMq: (context, cfg) =>
    {
        // Saga'nın TEK dinlediği kuyruk — hem başlatma komutu hem tüm reply
        // event'leri buraya gelir (bkz. SagaQueues.OrderSagaOrchestrator açıklaması).
        cfg.ReceiveEndpoint(SagaQueues.OrderSagaOrchestrator, e =>
        {
            e.ConfigureSaga<OrderSagaState>(context);
        });
    });

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("SagaDb")!,
        name: "postgresql",
        tags: ["ready"]);

var app = builder.Build();

// =====================================================================
// Modül 4: Veritabanı Şeması Oluşturma (EnsureCreated — bkz. Order.Infrastructure.md
// "Migrations Değil" notu, aynı yaklaşım burada da geçerli)
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseExceptionHandler();
app.UseServiceObservability();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Saga.Api",
    status = "up",
    module = "Modül 4 — Saga Pattern (Orchestration) aktif"
}));

// =====================================================================
// Modül 4: Saga durumunu + geçmişini İZLEMEK için debug endpoint'i.
// =====================================================================
// =====================================================================
// Modül 4: Saga durumunu + geçmişini İZLEMEK için debug endpoint'i.
// ⚠️ ÖNEMLİ: MassTransit, saga Finalize() ile bir "Final" state'e (Completed/
// Failed) ulaştığında, OrderSagaState satırını REPOSITORY'DEN OTOMATİK
// OLARAK SİLER (kendi housekeeping davranışıdır — tamamlanmış bir saga'nın
// canlı durumunu tutmaya gerek olmadığını varsayar). Bu yüzden tamamlanmış
// bir saga için `dbContext.OrderSagas.FindAsync(orderId)` HER ZAMAN null
// döner — bu bir hata değildir. "Son bilinen durumu" bu durumda
// OrderSagaStateHistory'deki (event streaming — hiç silinmez) SON kayıttan
// türetiyoruz.
// =====================================================================
app.MapGet("/debug/sagas/{orderId:guid}", async (Guid orderId, SagaDbContext dbContext) =>
{
    var state = await dbContext.OrderSagas.FindAsync(orderId);
    var history = await dbContext.OrderSagaHistory
        .Where(h => h.OrderId == orderId)
        .OrderBy(h => h.OccurredOnUtc)
        .Select(h => new { h.FromState, h.ToState, h.TriggeredByEvent, h.OccurredOnUtc, h.CustomerId, h.TotalAmount })
        .ToListAsync();

    if (state is null && history.Count == 0)
    {
        return Results.NotFound(new { message = "Bu OrderId için bir saga bulunamadı." });
    }

    var lastHistoryEntry = history.LastOrDefault();
    // Saga hâlâ aktifse (state != null) oradan; tamamlanmış/silinmişse
    // (state == null) history'deki son kayıttan türetilir.
    var isFinalized = state is null;

    return Results.Ok(new
    {
        orderId,
        currentState = state?.CurrentState ?? lastHistoryEntry?.ToState ?? "Bilinmiyor",
        isFinalized,
        customerId = state?.CustomerId ?? lastHistoryEntry?.CustomerId,
        totalAmount = state?.TotalAmount ?? lastHistoryEntry?.TotalAmount,
        history
    });
});

app.Run();
