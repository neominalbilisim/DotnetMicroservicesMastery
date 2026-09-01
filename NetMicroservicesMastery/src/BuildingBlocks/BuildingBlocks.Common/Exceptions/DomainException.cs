namespace BuildingBlocks.Common.Exceptions;

/// <summary>
/// Bilinçli olarak fırlatılan, iş kuralı ihlallerini temsil eden istisna türü.
/// Global exception handler bu türü "400 Bad Request" olarak eşler.
/// (bkz. Ön Hazırlık Dökümanı - Modül 1: Cross-Cutting Concerns)
/// </summary>
public class DomainException(string message) : Exception(message);

/// <summary>Aranan kaynağın bulunamadığı durumlar için (404 Not Found).</summary>
public class NotFoundException(string entityName, object key)
    : Exception($"'{entityName}' ({key}) bulunamadı.");
