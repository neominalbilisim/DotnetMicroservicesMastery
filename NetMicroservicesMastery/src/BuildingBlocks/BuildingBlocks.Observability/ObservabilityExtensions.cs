using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace BuildingBlocks.Observability;

/// <summary>
/// Modül 1 - "Merkezi Loglama ve Gözlemlenebilirlik Zinciri".
/// Serilog (structured logging) + OpenTelemetry (metrics/tracing) + Prometheus exporter
/// zincirini tek satırda tüm servislere kazandıran ortak extension noktası.
///
///   .NET Servisi (Serilog+OTel) -> OTel Collector/Prometheus/Jaeger -> Grafana
///
/// Kullanım (her *.Api/Program.cs içinde):
///   builder.AddServiceObservability(serviceName: "Order.Api");
///   ...
///   app.UseServiceObservability();
/// </summary>
public static class ObservabilityExtensions
{
    public static IHostApplicationBuilder AddServiceObservability(
        this IHostApplicationBuilder builder, string serviceName)
    {
        // Serilog'un bir sink'e (örn. Seq) yazarken karşılaştığı hataları
        // normalde SESSİZCE yutar. Bu satır, o hataları container loglarına
        // (docker compose logs <servis>) yazdırarak bağlantı sorunlarının
        // görünür olmasını sağlar — sadece geliştirme/tanı amaçlıdır.
        Serilog.Debugging.SelfLog.Enable(msg =>
            Console.Error.WriteLine($"[Serilog SelfLog] {msg}"));

        var configuration = builder.Configuration;
        var seqServerUrl = configuration["Seq:ServerUrl"] ?? "http://localhost:5341";
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

        // -----------------------------------------------------------------
        // 1) Serilog — Structured Logging
        //    Loglar JSON alanlarla (ServiceName, Environment, TraceId vb.)
        //    üretilir; Console'a (docker logs) ve Seq'e (arama/filtreleme
        //    UI'ı) aynı anda yazılır. appsettings.json'daki isteğe bağlı
        //    "Serilog" bölümü de (varsa) okunur ve override edebilir.
        // -----------------------------------------------------------------
        builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("ServiceName", serviceName)
            .WriteTo.Console()
            .WriteTo.Seq(seqServerUrl));

        // -----------------------------------------------------------------
        // 2) OpenTelemetry — Metrics + Tracing
        //    Resource attribute'ları (service.name) sayesinde Grafana/Jaeger
        //    tarafında hangi servisten geldiği ayırt edilebilir.
        // -----------------------------------------------------------------
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Prometheus'un scrape edeceği /metrics endpoint'ini üretir
                // (bkz. UseServiceObservability -> MapPrometheusScrapingEndpoint).
                .AddPrometheusExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Modül 5 - MassTransit'in kendi yerleşik OpenTelemetry
                // ActivitySource'unu ("MassTransit") dinlemeye başlar. Bu
                // sayede Kafka (Rider) VE RabbitMQ üzerinden giden/gelen
                // TÜM Send/Publish/Consume işlemleri otomatik olarak trace
                // edilir — MassTransit, trace context'i mesaj header'larına
                // kendisi enjekte edip okur (tıpkı HTTP'deki traceparent
                // gibi), bu yüzden ekstra bir "context propagation"
                // middleware'i yazmaya GEREK YOKTUR.
                .AddSource("MassTransit")
                // Trace'ler OTLP üzerinden Jaeger'a (veya bir OTel Collector'a) gönderilir.
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));

        return builder;
    }

    public static WebApplication UseServiceObservability(this WebApplication app)
    {
        // Her HTTP isteğini (method, path, status code, süre) tek satır
        // structured log olarak yazar — Modül 1'in "tek bir isteğin onlarca
        // servisten geçmesi" sorununu Seq/Grafana'da izlenebilir kılar.
        app.UseSerilogRequestLogging();

        // Prometheus'un scrape edeceği /metrics endpoint'i.
        app.MapPrometheusScrapingEndpoint();

        return app;
    }
}
