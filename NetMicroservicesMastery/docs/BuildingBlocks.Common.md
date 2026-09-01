# BuildingBlocks.Common

**Modül:** Modül 1 — Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik
**Konu:** Cross-Cutting Concerns — Merkezi Hata Yönetimi (Global Exception Handling)

## Amaç

Tüm mikroservislerde (Order, Payment, Inventory, ApiGateway, JobService) tekrar
yazılmaması gereken, ortak "cross-cutting concern"leri barındıran class library.
Şu an için kapsamı: **global exception handling** ve **RFC 7807 Problem Details**
formatı. İleride Modül 4 (CQRS) çalışmasında MediatR pipeline behavior'ları da
bu projeye eklenecektir.

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `Exceptions/DomainException.cs` | İş kuralı ihlallerini temsil eden istisna (`400 Bad Request`'e eşlenir) |
| `Exceptions/DomainException.cs` (`NotFoundException`) | Kaynak bulunamadı istisnası (`404 Not Found`'a eşlenir) |
| `Exceptions/GlobalExceptionHandler.cs` | ASP.NET Core'un gerçek `IExceptionHandler`'ını implemente eden merkezi handler |
| `Models/ApiProblemDetails.cs` | RFC 7807 formatında, tüm servislerde aynı şekle sahip hata cevabı |
| `HealthChecks/HealthCheckResponseWriter.cs` | Health check sonuçlarını tutarlı JSON formatında yazan ortak writer (bkz. `docs/Order.Api.md` vb.) |

## Nasıl Çalışır — Global Exception Handling

```
İstek -> Controller/Endpoint -> [Exception fırlatılır]
      -> IExceptionHandler.TryHandleAsync (GlobalExceptionHandler)
      -> Exception türüne göre status code belirlenir
      -> ApiProblemDetails JSON olarak Response'a yazılır
```

**Eşleme tablosu:**

| Exception türü | HTTP Status | Örnek kullanım |
|---|---|---|
| `DomainException` | 400 Bad Request | `throw new DomainException("Sipariş tutarı sıfırdan büyük olmalı.")` |
| `NotFoundException` | 404 Not Found | `throw new NotFoundException("Order", orderId)` |
| Diğer tüm exception'lar | 500 Internal Server Error | Beklenmeyen/bug niteliğindeki hatalar — iç detaylar istemciye **sızdırılmaz** |

**Örnek cevap (`DomainException` fırlatıldığında):**

```json
{
  "title": "Geçersiz istek",
  "status": 400,
  "detail": "Sipariş tutarı sıfırdan büyük olmalı.",
  "traceId": "0HN7...",
  "type": "https://httpstatuses.io/400"
}
```

## Bir Serviste Nasıl Kullanılır (Program.cs)

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
...
app.UseExceptionHandler();
```

## Neden Class Library Olarak (Web SDK Değil)?

Bu proje `Microsoft.NET.Sdk` (class library) ile oluşturulmuştur, `Microsoft.NET.Sdk.Web`
ile değil — çünkü Application/Domain katmanlarından da referans alınabilmesi
gerekir ve gereksiz web-host bağımlılığı taşımamalıdır. Bu nedenle `HttpContext`,
`IExceptionHandler` gibi ASP.NET Core tiplerine erişebilmek için `.csproj`'a
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` eklenmiştir
(detaylı gerekçe: repo kökündeki `COMPILE_FIX_NOTES.md`).

## Loglama Davranışı

- `DomainException` / `NotFoundException` → `LogWarning` (beklenen, iş akışının parçası)
- Diğer tüm exception'lar → `LogError` (beklenmeyen, incelenmesi gereken hata)

Bu ayrım, Seq/Grafana'da "gerçek hata" ile "beklenen iş kuralı reddi"nin
birbirinden karışmadan filtrelenebilmesini sağlar.

## Bilinen Sınırlamalar / Sonraki Adımlar

- Validasyon hataları (FluentValidation) için henüz özel bir exception türü
  ve 422/400 eşlemesi yok — Modül 4 (CQRS) çalışmasında `ValidationException`
  ve MediatR `ValidationBehavior` eklenirken bu genişletilecektir.
