# ApiGateway

**Modül:** Modül 2 — Güvenlik, Dayanıklılık ve Konfigürasyon
**Konu:** API Gateway (YARP) + Consul Dinamik Servis Keşfi + Keycloak (AuthServer)

## Bu Projede Ne Var

| Bileşen | Sorumluluk |
|---|---|
| `Program.cs` | Tüm parçaları (Keycloak, Consul, YARP) birleştiren giriş noktası |
| `Services/YarpRouteMap.cs` | Route (path) ↔ Consul servis adı eşlemesinin **tek merkezi kaynağı** |
| `Services/ConsulYarpSyncHostedService.cs` | Consul'u periyodik sorgulayıp YARP hedeflerini günceller |

## Neden Statik `ReverseProxy` appsettings Bölümü Artık Yok

Önceki (Modül 1) sürümünde `appsettings.json`'da sabit bir `ReverseProxy`
bölümü vardı (`http://order-api:8080` gibi). Bu, iki sorun doğurur:

1. Yeni bir instance ayağa kalktığında appsettings'i elle güncellemek gerekir.
2. Bir instance çökse bile YARP onu hâlâ "sağlıklı" sanıp trafik göndermeye devam eder.

Artık bu bölüm appsettings'ten **tamamen kaldırıldı**; hedefler tamamen
Consul'dan dinamik olarak besleniyor (bkz. aşağıdaki akış).

## Dinamik Servis Keşfi Akışı

```
İstemci -> YARP (ApiGateway) -> Route eşleşir (örn. /api/orders/*)
                              -> Cluster'ın güncel hedefleri (ConsulYarpSyncHostedService
                                 tarafından 10sn'de bir Consul'dan güncellenmiş) kullanılır
                              -> Sağlıklı bir instance'a proxy'lenir
```

`YarpRouteMap.cs`'deki eşleme:

| Route (path) | Cluster | Consul Servis Adı |
|---|---|---|
| `/api/orders/{**catch-all}` | order-cluster | order-service |
| `/api/payments/{**catch-all}` | payment-cluster | payment-service |
| `/api/inventory/{**catch-all}` | inventory-cluster | inventory-service |

`ConsulYarpSyncHostedService`, her 10 saniyede bir `Consul.Health.Service(name,
passingOnly: true)` çağrısı yapar — bu, sadece health check'i **geçen**
(sağlıklı) instance'ları döner. Sonuç, YARP'ın `InMemoryConfigProvider`'ına
(`Update()` metoduyla) yazılır.

**Geçici Consul kesintisine karşı koruma:** Bir sorguda hiç sağlıklı instance
bulunamazsa (örn. Consul'a geçici ulaşılamıyorsa), bir önceki başarılı
sorgudaki hedefler **korunur** — YARP aniden tüm hedeflerini kaybetmez.

**İlk açılış:** Uygulama ayağa kalktığında cluster/hedef listesi **boştur**
(henüz ilk Consul senkronizasyonu tamamlanmamıştır). Bu birkaç saniyelik
pencerede proxy istekleri `503 (no available destinations)` döner — bu
normaldir.

## Keycloak: Merkezi Kimlik Doğrulama

Bu proje, ekosistemdeki **tek** JWT doğrulama noktasıdır (bkz.
`docs/BuildingBlocks.Security.md`). `app.MapReverseProxy().RequireAuthorization()`
sayesinde, proxy edilen her istek geçerli bir Bearer token gerektirir;
`/health/*` ve `/` bundan muaftır.

## Consul: Gateway Kendini de Kaydeder

Diğer tüm servisler gibi ApiGateway da kendini Consul'a kaydeder
(`Consul:ServiceName: "api-gateway"`) — bu, envanter/gözlem amaçlıdır (Consul
UI'da tüm ekosistemi tek yerden görebilmek için); hiçbir servis Gateway'i
Consul üzerinden keşfetmez (Gateway zaten sabit bir giriş noktasıdır).

## Health Check

`/health/live`, `/health/ready`, `/health` — Order/Payment/Inventory'deki ile
aynı desende (bkz. `docs/Order.Api.md`), ancak Gateway'in kendi veritabanı
bağımlılığı olmadığından şu an için sadece temel bir kontrol (her zaman
"Healthy") döner.

## Rate Limiting — Redis Tabanlı (Dağıtık)

`RedisRateLimitingMiddleware.cs` — ASP.NET Core'un yerleşik (in-memory)
`Microsoft.AspNetCore.RateLimiting` middleware'i yerine, sayacı **Redis'te**
tutan custom bir middleware kullanılır.

### Neden Redis (In-Memory Değil)?

Birden fazla ApiGateway instance'ı çalıştırıldığında (Modül 5'teki
multi-instance senaryosu gibi), in-memory bir sayaç **her instance'ta ayrı**
tutulur — "10sn'de 20 istek" limiti gerçekte "instance başına 10sn'de 20
istek"e dönüşür (3 instance varsa fiilen 60 istek/10sn olur). Redis'teki
**paylaşımlı** sayaç ile, kaç ApiGateway instance'ı olursa olsun limit
ekosistem genelinde gerçekten geçerli olur.

### Algoritma: Fixed Window Counter (Lua Script ile Atomik)

```lua
local current = redis.call('INCR', KEYS[1])
if tonumber(current) == 1 then
    redis.call('EXPIRE', KEYS[1], ARGV[1])
end
local ttl = redis.call('TTL', KEYS[1])
return { current, ttl }
```

`INCR` + `EXPIRE`'ın **tek bir Lua script içinde** çalıştırılması, iki
ApiGateway instance'ının aynı anda aynı sayaca yazması durumunda oluşabilecek
race condition'ı (ikisinin de "current == 1" görüp expire'ı iki kez set
etmesi gibi) önler — Redis, bir Lua script'i her zaman atomik (bölünmez)
çalıştırır.

| Ayar | Değer | Config anahtarı |
|---|---|---|
| Pencere | 10 saniye | `RateLimiting:WindowSeconds` |
| Kota | 10sn'de 20 istek | `RateLimiting:PermitLimit` |
| Partition key | JWT'nin `azp` claim'i — yoksa IP | — |
| Redis key formatı | `ratelimit:gateway:{partitionKey}` | — |

### Response Header'ları (Her İstekte)

| Header | Anlamı |
|---|---|
| `X-RateLimit-Limit` | Pencere başına izin verilen toplam istek |
| `X-RateLimit-Remaining` | Bu pencerede kalan hak |
| `X-RateLimit-Reset` | Pencerenin sıfırlanmasına kaç saniye kaldığı |
| `Retry-After` | (sadece 429'da) Ne kadar sonra tekrar denenmeli |

### "Fail Open" Davranışı

Redis'e ulaşılamazsa (örn. Redis container çökmüşse), middleware isteği
**engellemez** — sadece bir uyarı loglar ve isteğin geçmesine izin verir.
Gerekçe: bir "rate limiting" mekanizmasının kendisi, arkasındaki bağımlılık
çöktüğünde TÜM trafiği durdurursa, sorunu çözmek yerine daha da büyütmüş
olur. (Bu "fail open" tercihi, güvenlik açısından kritik senaryolarda —
örn. yetkilendirme — "fail closed" olması gerekirdi; ama rate limiting için
kabul edilebilir bir trade-off'tur.)

### Doğrulama

```bash
# Aynı token ile art arda 25 istek atın; ilk 20'si normal (404, çünkü
# endpoint yok), sonraki 5'i 429 döner. Her cevapta X-RateLimit-Remaining
# header'ının azaldığını gözlemleyin.
for i in $(seq 1 25); do
  curl -s -D - -o /dev/null http://localhost:8080/api/orders/123 \
    -H "Authorization: Bearer <access_token>" | grep -E "HTTP|X-RateLimit"
done
```

**Redis'te sayacı doğrudan görmek isterseniz:**
```bash
docker exec -it neominal-redis redis-cli
> KEYS ratelimit:*
> GET ratelimit:gateway:order-service
> TTL ratelimit:gateway:order-service
```

## Load Balancing — Birden Fazla Instance ile Test

Aynı servisin (örn. Inventory.Api) **birden fazla sağlıklı instance'ı**
Consul'a kayıtlıysa, `ConsulYarpSyncHostedService` bunların hepsini
`ClusterConfig.Destinations`'a ekler ve **RoundRobin** politikasıyla sırayla
dağıtır (bkz. `docs/Inventory.Api.md` — "İkinci Instance ile Load Balancing
Testi" bölümü, `dotnet run --launch-profile` ile ikinci instance'ı nasıl
ayağa kaldıracağınızı adım adım anlatır).

```bash
# Gateway üzerinden art arda istek atın; ConsulYarpSyncHostedService logunda
# "2 sağlıklı instance bulundu: http://host.docker.internal:5003, http://host.docker.internal:5013"
# satırını göreceksiniz. RoundRobin sayesinde istekler sırayla iki instance
# arasında dağıtılır.
```

## Doğrulama

```bash
# 1) Geçerli bir Keycloak token'ı olmadan proxy isteği -> 401 beklenir
curl -i http://localhost:8080/api/orders/123

# 2) Keycloak'tan token alın (bkz. docs/BuildingBlocks.Security.md)
curl -X POST http://localhost:8180/realms/net-microservices-mastery/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=order-service&client_secret=order-service-secret"

# 3) Token ile aynı isteği tekrarlayın -> artık 401 DEĞİL, Order.Api'den 404
#    (Order.Api'de henüz gerçek /api/orders endpoint'i yok — Modül 4'te gelecek)
#    Bu 404'ü almak, Keycloak doğrulama + Consul keşfi + YARP proxy zincirinin
#    UÇTAN UCA çalıştığını kanıtlar.
curl -i http://localhost:8080/api/orders/123 -H "Authorization: Bearer <access_token>"

# 4) Consul UI'da tüm servislerin (gateway dahil) kayıtlı olduğunu doğrulayın
# http://localhost:8500 -> Services

# 5) order-api'yi durdurup ConsulYarpSyncHostedService loglarında
#    "sağlıklı instance yok" uyarısını görün (~10-70 sn içinde)
```

## Bilinen Sınırlamalar / Sonraki Adımlar

- Geçerli bir JWT token üretme akışı (Keycloak'tan token alma) dokümante
  edildi — bkz. `docs/BuildingBlocks.Security.md`.
- ~~Rate limiting~~ ✅ Tamamlandı — yukarıdaki bölüme bakın.
- Header manipülasyonu gibi diğer Gateway sorumlulukları (Ön Hazırlık
  Dökümanı Modül 2'de bahsedilen) henüz eklenmedi.
- ~~Round-robin dışında bir load balancing politikası~~ ✅ RoundRobin
  açıkça yapılandırıldı (yukarıdaki bölüme bakın).
