using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Security;

/// <summary>
/// Modül 2 - "Merkezi Kimlik Doğrulama: Keycloak ve AuthServer Mantığı".
/// Keycloak (AuthServer) tarafından üretilen JWT Access Token'ı JwtBearer
/// middleware üzerinden doğrular.
///
/// MİMARİ KARAR: Bu proje, JWT doğrulamasını YALNIZCA API Gateway'de yapar.
/// Order/Payment/Inventory gibi downstream servisler kendi başlarına token
/// doğrulamaz — kimlik doğrulama sorumluluğu tek bir noktada (Gateway'de)
/// toplanır (bkz. Ön Hazırlık Dökümanı Modül 2 - "Merkezi Yönetim" ilkesi).
///
///   Kullanıcı Giriş Yapar -> Keycloak (AuthServer) -> JWT Access Token Üretilir
///   -> API Gateway Token'ı Doğrular -> (geçerliyse) İlgili Mikroservise Proxy'ler
/// </summary>
public static class KeycloakAuthenticationExtensions
{
    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Keycloak:Authority"];
        var audience = configuration["Keycloak:Audience"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                // DEV ORTAMI: Keycloak (dev mode) HTTPS kullanmıyor. PROD'da bu
                // MUTLAKA true olmalı ve Keycloak HTTPS arkasında olmalıdır.
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    // Realm/issuer, Keycloak container'ının kendi adresini (http://keycloak:8080/...)
                    // token içine yazar. Gateway dışarıdan farklı bir host/port ile
                    // erişiliyorsa (örn. localhost:8180) issuer uyuşmazlığı yaşanabilir
                    // — bu durumda ValidIssuer'ı appsettings üzerinden açıkça set edin.
                };
            });

        return services;
    }
}
