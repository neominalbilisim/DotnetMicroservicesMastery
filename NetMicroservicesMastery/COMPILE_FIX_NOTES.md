# Derleme Hataları — Gerçek Kök Neden ve Düzeltme

> Bu not, `DERLEME_HATALARI_RAPORU.md`'deki teşhisi düzeltir. O raporun önerdiği çözüm
> (özel bir `IExceptionHandler` arayüzü tanımlamak) derlemeyi geçirir ama **çalışma
> zamanında global hata yönetimini sessizce devre dışı bırakır** — bu yüzden
> uygulanmadı, yerine aşağıdaki kök-neden çözümü uygulandı.

## Gerçek Kök Neden

`IExceptionHandler`, .NET 8+ ile birlikte gelen **gerçek bir ASP.NET Core arayüzüdür**
(`Microsoft.AspNetCore.Diagnostics` namespace'i) ve kodda zaten doğru kullanılıyordu.

Asıl sorun: `BuildingBlocks.Common`, `BuildingBlocks.Observability`, `BuildingBlocks.Resilience`
ve `BuildingBlocks.Security` projeleri **class library** (`Microsoft.NET.Sdk`) olarak
oluşturuldu — `Microsoft.NET.Sdk.Web` değil. Class library projeleri, ASP.NET Core'un
paylaşımlı çalışma zamanına (shared framework: `HttpContext`, `IExceptionHandler`,
`WebApplication`, `IHostApplicationBuilder` vb.) **otomatik erişime sahip değildir.**

`Microsoft.AspNetCore.Http.Abstractions` (v2.2.0) gibi eski NuGet paketleri .NET Core
3.0 öncesi modelden kalmadır; .NET 10 ile uyumlu değildir ve `IExceptionHandler`'ı
(2023'te .NET 8 ile geldi) hiçbir şekilde içermez — derleme hatalarının asıl sebebi budur.

## Doğru Çözüm

Her ilgili class library'ye tek satırlık gerçek framework referansı eklendi:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

Bu satır; `HttpContext`, `IExceptionHandler`, `WebApplication`, `IHostApplicationBuilder`,
`IConfiguration`, `IServiceCollection`, `ILogger` gibi tüm ASP.NET Core/Extensions
tiplerini, herhangi bir NuGet paketi eklemeden, doğrudan gerçek framework derlemelerinden
kazandırır.

**Neden bu, rapordaki "kendi IExceptionHandler'ını tanımla" çözümünden daha iyi:**
`app.UseExceptionHandler()` middleware'i, DI container'da özellikle
`Microsoft.AspNetCore.Diagnostics.IExceptionHandler` tipini arar. Aynı isimli ama
farklı (kendi tanımladığımız) bir arayüz kullanılsaydı, derleme başarılı olur ama
middleware bu handler'ı **hiçbir zaman bulamaz** — global hata yönetimi çalışma
zamanında sessizce devre dışı kalırdı.

## Değiştirilen Dosyalar

| Dosya | Değişiklik |
|---|---|
| `BuildingBlocks.Common.csproj` | `Microsoft.AspNetCore.Http.Abstractions` (2.2.0) kaldırıldı → `FrameworkReference` eklendi |
| `BuildingBlocks.Observability.csproj` | `FrameworkReference` eklendi |
| `BuildingBlocks.Resilience.csproj` | `FrameworkReference` eklendi |
| `BuildingBlocks.Security.csproj` | `FrameworkReference` eklendi |
| `Order/Payment/Inventory.Application.csproj` | `Microsoft.Extensions.DependencyInjection.Abstractions` eklendi (Application katmanını ASP.NET Core'a bağımlı kılmamak için `FrameworkReference` yerine hafif bir paket tercih edildi) |
| `GlobalExceptionHandler.cs` | Modül 1'in TODO'su tamamlandı: `DomainException`→400, `NotFoundException`→404, diğerleri→500; `ApiProblemDetails` JSON olarak yazılıyor; 500 hatalarında iç detaylar istemciye sızdırılmıyor |
| `ApiProblemDetails.cs` | `Type` alanı (RFC 7807) eklendi |

Repodaki **19 projenin tamamı** bu değişiklikten sonra statik olarak (namespace/paket
eşleşmesi) tekrar tarandı; eksik referans kalmadı.
