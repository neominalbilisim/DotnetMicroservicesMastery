namespace BuildingBlocks.Common.Models;

/// <summary>
/// RFC 7807 Problem Details formatına uygun, tüm servislerde tutarlı hata cevabı.
/// </summary>
public record ApiProblemDetails(string Title, int Status, string? Detail, string? TraceId)
{
    public string Type { get; init; } =
        $"https://httpstatuses.io/{Status}";
}
