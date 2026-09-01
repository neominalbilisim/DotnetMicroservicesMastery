using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using BuildingBlocks.Common.Models;
using System.Net.Mime;
using System.Text.Json;

namespace BuildingBlocks.Common.Exceptions;

/// <summary>
/// Modül 1 - "Cross-Cutting Concerns: Merkezi Hata Yönetimi".
/// Tüm mikroservislerde ortak kullanılan global exception handler.
/// ASP.NET Core'un gerçek Microsoft.AspNetCore.Diagnostics.IExceptionHandler
/// arayüzünü implemente eder; bu sayede app.UseExceptionHandler() middleware'i
/// tarafından DI üzerinden otomatik olarak bulunur ve çağrılır.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = MapException(exception);

        // DomainException/NotFoundException bilinçli fırlatılan, beklenen hatalardır;
        // bunlar Warning, geri kalan her şey (beklenmeyen/bug niteliğindeki hatalar) Error seviyesinde loglanır.
        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Beklenmeyen hata: {Message}", exception.Message);
        }
        else
        {
            logger.LogWarning(exception, "İş kuralı/istemci hatası: {Message}", exception.Message);
        }

        var problem = new ApiProblemDetails(
            Title: title,
            Status: statusCode,
            // 500 hatalarında iç detayları (stack trace, iç mesaj) istemciye SIZDIRMIYORUZ.
            Detail: statusCode == StatusCodes.Status500InternalServerError
                ? "Sunucu tarafında beklenmeyen bir hata oluştu."
                : exception.Message,
            TraceId: httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = MediaTypeNames.Application.ProblemJson;

        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(problem),
            cancellationToken);

        // true = "bu exception'ı ben ele aldım, pipeline'da başka handler aranmasın".
        return true;
    }

    private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Kaynak bulunamadı"),
        DomainException => (StatusCodes.Status400BadRequest, "Geçersiz istek"),
        _ => (StatusCodes.Status500InternalServerError, "Sunucu hatası")
    };
}
