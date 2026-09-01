using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace BuildingBlocks.Common.HealthChecks;

/// <summary>
/// Modül 1 - "Health Check". Her bağımlılığın (Postgres, Redis vb.) adını,
/// durumunu ve süresini tutarlı bir JSON formatında döndüren, tüm servislerde
/// ortak kullanılan health check response writer.
/// </summary>
public static class HealthCheckResponseWriter
{
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                tags = e.Value.Tags
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
