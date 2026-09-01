# BuildingBlocks.Resilience

**Modül:** Modül 2 — Güvenlik, Dayanıklılık ve Konfigürasyon
**Konu:** Secret Management (HashiCorp Vault)

## Amaç

Connection string gibi hassas verilerin `appsettings.json` içine düz metin
olarak yazılması yerine, HashiCorp Vault'tan runtime'da güvenli şekilde
okunmasını sağlayan ortak extension noktası.

```
.NET Servisi -> Vault'a Token ile kimlik doğrular -> secret/order-service okunur
             -> appsettings üzerine ŞEFFAFÇA uygulanır (aynı key adları)
```

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `VaultExtensions.cs` | `AddVaultSecrets()` — Vault'tan secret okuyup `IConfiguration`'a enjekte eder |

## Nasıl Çalışır

Vault'taki secret'ların anahtar isimleri, **doğrudan `IConfiguration` path
formatında** tutulur (örn. `ConnectionStrings:OrderDb`). Bu sayede Vault'tan
okunan değer, `AddInMemoryCollection` ile aynı isimli appsettings.json
değerinin üzerine yazılır — kod tarafında (`Program.cs`, `DbContext` vb.)
**hiçbir değişiklik gerekmez**; `builder.Configuration.GetConnectionString("OrderDb")`
çağrısı otomatik olarak Vault'tan gelen güncel değeri döner.

## Bir Serviste Nasıl Kullanılır (Program.cs)

```csharp
// AddDbContext'ten ÖNCE çağrılmalı (config'i o satırdan önce güncellemiş olur)
builder.AddVaultSecrets();

builder.Services.AddDbContext<OrderDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")));
```

## appsettings.json Anahtarları

```json
{
  "Vault": {
    "Address": "http://vault:8200",
    "SecretPath": "secret/data/order-service"
  }
}
```

`Vault:Token` **appsettings.json'da tutulmaz** — bilinçli bir tasarım kararı:
gerçek bir secret (Vault'un kendi kimlik doğrulama anahtarı) appsettings
dosyasına (git'e commit edilen bir dosya) yazılmamalıdır. Bunun yerine ortam
değişkeni olarak enjekte edilir:

| Ortam | Nereden geliyor |
|---|---|
| Docker | `docker-compose.apps.yml` → `Vault__Token: root` |
| Lokal (`dotnet run`) | `Properties/launchSettings.json` → `environmentVariables.Vault__Token` |

> Bu, dev/demo ortamı için pragmatik bir yaklaşımdır (gerçek token yine de
> bir dosyaya yazılı) — gerçek üretimde bu değer CI/CD secret store'undan
> (örn. GitHub Actions secrets, Azure Key Vault) enjekte edilmelidir.

## ⚠️ Vault Dev Sunucusu IN-MEMORY Çalışır

`docker-compose.infra.yml`'deki Vault, **dev mode** (`VAULT_DEV_ROOT_TOKEN_ID`)
ile çalışır — bu mod verileri sadece bellekte tutar. Container her yeniden
başlatıldığında (`docker compose restart vault` veya yeniden oluşturma) **tüm
secret'lar kaybolur**. Bu durumda secret'ları yeniden yüklemeniz gerekir:

```bash
# Linux/Mac/WSL
bash infra/vault/seed-secrets.sh

# Windows cmd.exe
infra\vault\seed-secrets.cmd
```

Bu script'ler `docker exec` ile doğrudan Vault container'ının kendi `vault`
CLI'ını kullanarak Order/Payment/Inventory servislerinin connection string'ini
yükler.

## Hata Toleransı

Vault'a ulaşılamazsa (henüz seed edilmemişse, container ayakta değilse vb.)
uygulama **başlamayı reddetmez** — `appsettings.json`'daki (fallback) değerle
devam eder ve konsola bir uyarı yazar:

```
[Vault] Secret okunamadı ('secret/data/order-service'): ... appsettings.json'daki (fallback) değerlerle devam ediliyor.
```

Bu tasarım, yerel geliştirmede Vault'suz da (sadece appsettings.json
değerleriyle) çalışabilmeyi sağlar.

## Doğrulama

```bash
# Vault'a secret'ları yükleyin
bash infra/vault/seed-secrets.sh   # veya .cmd

# Order.Api'yi başlatın ve konsol/container logunda şunu arayın:
# [Vault] 'order-service' altından 1 secret okundu ve appsettings üzerine uygulandı.
```

## Bilinen Sınırlamalar / Sonraki Adımlar

- ~~Polly (Retry/Circuit Breaker/Fallback) henüz bu projede implemente
  edilmedi~~ ✅ Tamamlandı — bkz. aşağıdaki bölüm.
- AppRole gibi daha üretim-uyumlu bir Vault kimlik doğrulama yöntemi yerine
  basitlik için Token auth kullanılmıştır (dev/eğitim ortamı için yeterlidir).

---

## Polly: Retry + Circuit Breaker + Fallback

`ResilienceExtensions.cs` — `AddStandardResiliency()`, tek bir `IHttpClientBuilder`
extension'ı içinde 4 katmanlı bir pipeline uygular (dıştan içe):

```
İstek -> [Fallback] -> [Retry: 3 deneme] -> [Circuit Breaker] -> [Timeout: 5sn] -> Gerçek HTTP çağrısı
```

| Katman | Ayar | Neden |
|---|---|---|
| **Retry** | 3 deneme, exponential backoff + jitter | Sabit aralıklı retry, zaten yoğun bir servise senkronize dalgalar halinde ek yük bindirebilir ("retry storm") — jitter bunu dağıtır |
| **Circuit Breaker** | 30sn'de ≥5 istekten %50 hata → 15sn devre açık | Sürekli hata veren bir servise istek göndermeyi keserek hem onun toparlanmasına hem de çağıran servisin gereksiz beklemesine engel olur |
| **Timeout** | Deneme başına 5sn | Yanıt vermeyen bir servisin çağıranı sonsuza kadar bloklamasını engeller |
| **Fallback** (en dışta) | Yukarıdakilerin TÜMÜ tükenirse | Ham exception yerine kontrollü `503` + JSON body döner — çağıran kod asla çökmez |

### Kullanım

```csharp
builder.Services.AddHttpClient("InventoryClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:InventoryApiBaseUrl"]);
}).AddStandardResiliency();
```

### Demo/Test (Order.Api → Inventory.Api)

Order.Api'de, Inventory.Api'yi çağıran bir demo endpoint eklendi:

```bash
curl http://localhost:5001/test-resiliency
```

| Inventory.Api durumu | Beklenen cevap |
|---|---|
| Ayakta | Inventory.Api'nin `/health/ready` cevabı (200, `{"status":"Healthy",...}`) |
| Durdurulmuş/erişilemez | ~birkaç saniye (3 retry + timeout) sonra Fallback'in ürettiği `503` + `{"title":"Bağımlı servise şu an ulaşılamıyor (fallback cevabı)","status":503}` |

**Circuit Breaker'ı tetiklemek için:** Inventory.Api'yi durdurup `/test-resiliency`'i
art arda 5+ kez çağırın — birkaç başarısız denemeden sonra Circuit Breaker
devreye girer ve sonraki istekler Retry'yi hiç denemeden (anında) Fallback'e
düşer — loglarda/yanıt süresinde bu farkı görebilirsiniz.

