using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace ApiGateway.Authentication;

/// <summary>
/// Keycloak (ve genel olarak her OAuth2/OIDC provider), "scope" claim'ini
/// tek bir boşlukla ayrılmış string olarak üretir:
///   "scope": "profile email order-admin"
///
/// ASP.NET Core'un RequireClaim("scope", "order-admin") metodu TAM EŞLEŞME
/// aradığı için bu tek-string değeri asla "order-admin" ile eşleşmez ve
/// authorization her zaman 403 ile başarısız olur.
///
/// Bu transformation, tek claim'i boşluklardan ayırıp her scope için AYRI
/// bir "scope" claim'i üretir; böylece RequireClaim("scope", "order-admin")
/// artık doğru şekilde eşleşir.
/// </summary>
public class ScopeClaimTransformation : IClaimsTransformation
{
    private const string ScopeClaimType = "scope";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null)
            return Task.FromResult(principal);

        // "profile email order-admin" gibi TEK bir claim var mı?
        var rawScopeClaim = identity.FindFirst(ScopeClaimType);
        if (rawScopeClaim is null)
            return Task.FromResult(principal);

        var scopes = rawScopeClaim.Value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Zaten bölünmüşse (bu transformation birden fazla kez çalışırsa)
        // tekrar bölmeyi önle.
        if (scopes.Length <= 1)
            return Task.FromResult(principal);

        identity.RemoveClaim(rawScopeClaim);

        foreach (var scope in scopes)
        {
            identity.AddClaim(new Claim(ScopeClaimType, scope));
        }

        return Task.FromResult(principal);
    }
}
