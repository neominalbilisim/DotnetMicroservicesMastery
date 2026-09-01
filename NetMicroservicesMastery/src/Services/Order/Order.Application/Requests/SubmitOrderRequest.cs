using System;

namespace Order.Application.Requests;

/// <summary>
/// Modül 3 - POST /submit-order endpoint'inin request body sözleşmesi.
/// Dışarıdan (Postman/curl) sipariş verisi almak için kullanılır.
///
/// KURAL: Bundan sonra tüm request tipindeki record'lar, ilgili servisin
/// Application katmanında "Requests/" klasörü altında tanımlanır — Api
/// katmanının Program.cs dosyasına inline olarak GÖMÜLMEZ. Bu, hem
/// Program.cs'i sade tutar hem de top-level statement/type declaration
/// sıralama hatalarını (bkz. COMPILE_FIX_NOTES.md benzeri notlar) önler.
///
/// OrderId OPSİYONELDİR — verilmezse otomatik üretilir; Partition Key
/// testini elle kontrol etmek isterseniz (örn. aynı OrderId ile birden
/// fazla istek atıp hepsinin aynı partition'a düştüğünü görmek için)
/// açıkça belirtebilirsiniz.
/// </summary>
public record SubmitOrderRequest(Guid? OrderId, string CustomerId, decimal TotalAmount);
