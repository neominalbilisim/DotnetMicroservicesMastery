# BuildingBlocks.Security

**Modül:** Modül 2 — Güvenlik, Dayanıklılık ve Konfigürasyon
**Konu:** Service Discovery (Consul) + Merkezi Kimlik Doğrulama (Keycloak)

## İçerik

| Dosya | Sorumluluk |
|---|---|
| `ConsulExtensions.cs` | `AddConsulServiceDiscovery()` — servisi Consul'a kaydeden hosted service'i DI'ya ekler |
| `ConsulRegistrationHostedService.cs` | Uygulama başlarken Consul'a kayıt, kapanırken kayıt silme |
| `KeycloakAuthenticationExtensions.cs` | `AddKeycloakAuthentication()` — JWT Bearer doğrulaması (SADECE ApiGateway'de kullanılır) |

## Mimari Karar: "Tüm Servisler Consul'a Kayıt Olur, Sadece Gateway JWT Doğrular"

| Servis | Consul'a kayıt olur mu? | Keycloak/JWT doğrular mı? |
|---|---|---|
| Order.Api | ✅ | ❌ (Gateway'e güvenir) |
| Payment.Api | ✅ | ❌ (Gateway'e güvenir) |
| Inventory.Api | ✅ | ❌ (Gateway'e güvenir) |
| JobService | ✅ (envanter/gözlem amaçlı) | ❌ (HTTP trafiği almaz) |
| **ApiGateway** | ✅ | ✅ **(tek doğrulama noktası)** |

Bu, Ön Hazırlık Dökümanı Modül 2'deki "Merkezi Yönetim" ilkesiyle örtüşür:
kimlik doğrulama mantığı her serviste tekrarlanmaz, tek ve iyi test edilmiş
bir noktada (Gateway) toplanır.

## Consul: Servis Kaydı Nasıl Çalışır

```
Servis Başlar -> ConsulRegistrationHostedService.StartAsync()
             -> Agent.ServiceRegister (ID, Name, Address, Port, HealthCheck URL)
             -> Consul, 10sn'de bir /health/ready endpoint'ini kontrol eder
             -> Servis Kapanır -> Agent.ServiceDeregister
```

Health check 1 dakika boyunca "critical" kalırsa Consul kaydı **otomatik
tamamen siler** (`DeregisterCriticalServiceAfter`) — hayalet kayıt kalmaz.

### appsettings.json Anahtarları

```json
{
  "Consul": {
    "Address": "http://consul:8500",
    "ServiceName": "order-service",
    "ServiceAddress": "order-api",
    "ServicePort": 8080
  }
}
```

| Ortam | `ServiceAddress` değeri | Neden |
|---|---|---|
| Docker | Container adı (örn. `order-api`) | Consul, aynı Docker ağındaki container'a bu isimle ulaşır |
| Lokal (`dotnet run`) | `host.docker.internal` | Consul (Docker'da), host makinede çalışan uygulamaya bu köprü üzerinden ulaşır |

### Bir Serviste Nasıl Kullanılır (Program.cs)

```csharp
builder.Services.AddConsulServiceDiscovery(builder.Configuration, defaultServiceName: "order-service");
```

### Doğrulama

`http://localhost:8500` (Consul UI) → "Services" sekmesinde `order-service`,
`payment-service`, `inventory-service`, `api-gateway`, `job-service`'in
hepsini **yeşil (healthy)** olarak görmelisiniz.

## Keycloak: JWT Doğrulama Nasıl Çalışır (Sadece ApiGateway)

```
Kullanıcı Keycloak'a giriş yapar -> JWT Access Token alır
  -> İstemci, token'ı Authorization: Bearer <token> ile ApiGateway'e gönderir
  -> ApiGateway (JwtBearer middleware) token'ı doğrular (issuer/audience/imza/süre)
  -> Geçerliyse -> YARP, isteği ilgili mikroservise proxy'ler
  -> Geçersizse -> 401 Unauthorized (mikroservise hiç ulaşmaz)
```

### appsettings.json Anahtarları (sadece ApiGateway'de)

```json
{
  "Keycloak": {
    "Authority": "http://keycloak:8080/realms/net-microservices-mastery",
    "Audience": "api-gateway"
  }
}
```

### Bir Serviste Nasıl Kullanılır (sadece ApiGateway/Program.cs)

```csharp
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
...
app.UseAuthentication();
app.UseAuthorization();
...
app.MapReverseProxy().RequireAuthorization();  // proxy edilen TÜM istekler JWT gerektirir
```

`/health/*` ve `/` endpoint'leri `RequireAuthorization()`'dan **etkilenmez**
(ayrı `MapHealthChecks`/`MapGet` çağrılarıyla map'lendiği için) — Consul'un
bu endpoint'lere token olmadan erişebilmesi gerekir.

### ⚠️ Realm Kurulumu

`docker-compose.infra.yml`, Keycloak'ı `--import-realm` ile başlatıp
`infra/keycloak/realm-export.json`'daki `api-gateway`, `order-service`,
`payment-service`, `inventory-service` client'larını (her biri sabit bir
secret, `serviceAccountsEnabled` ve Gateway audience mapper'ı ile), bir de
test kullanıcısını (`testuser` / `Test1234!`) otomatik yükler.

**⚠️ ÖNEMLİ:** Keycloak, realm'i **SADECE ilk açılışta** (veri volume'ü
boşken) import eder — Seq/Grafana admin şifresi ile aynı davranış kalıbı.
`realm-export.json`'ı güncellediyseniz ve Keycloak zaten bir kez çalıştıysa,
değişikliklerin etkili olması için:

```bash
docker compose -f docker-compose.infra.yml stop keycloak
docker compose -f docker-compose.infra.yml rm -f keycloak
docker volume rm neominal-microservices-infra_keycloak-data   # tam ad için: docker volume ls | grep keycloak
docker compose -f docker-compose.infra.yml up -d keycloak
```

### Postman'dan Token Alma (client_credentials)

```
POST http://localhost:8180/realms/net-microservices-mastery/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
client_id=order-service
client_secret=order-service-secret
```

Dönen `access_token`'ı, ApiGateway'e atılan isteklerde `Authorization: Bearer <token>`
olarak kullanın. Bu client'ın audience mapper'ı sayesinde token'ın `aud`
claim'i otomatik olarak `api-gateway` içerir — Gateway'in `Keycloak:Audience`
doğrulamasını geçer.

## Bilinen Sınırlamalar / Sonraki Adımlar

- Token alma akışı (login sayfası, Postman/curl ile token üretme) henüz
  dokümante edilmedi — bir sonraki doğrulama adımında ele alınacak.
- Rol/claim tabanlı yetkilendirme (örn. sadece `order.write` rolü olanlar
  sipariş oluşturabilsin) henüz tanımlanmadı; şu an sadece "geçerli token
  var mı" kontrol ediliyor.
- Consul health check'i şu an sadece `/health/ready`'yi kontrol ediyor
  (Postgres/Redis); Vault/Kafka gibi diğer bağımlılıklar health check
  zincirine henüz dahil değil.
