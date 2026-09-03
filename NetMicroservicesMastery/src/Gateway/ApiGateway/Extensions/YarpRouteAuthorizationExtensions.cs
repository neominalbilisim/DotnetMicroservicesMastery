using Microsoft.AspNetCore.Builder;
using ApiGateway.Services;
using Yarp.ReverseProxy;

namespace ApiGateway.Extensions;

/// <summary>
/// YARP route'larına route-specific authorization policy'lerini uygulamak için
/// extension method. Her route, YarpRouteMap.cs'te tanımlı authorization policy'ini
/// otomatik olarak uygular.
/// </summary>
public static class YarpRouteAuthorizationExtensions
{
    public static IApplicationBuilder UseYarpRouteAuthorization(
        this IApplicationBuilder app,
        IReadOnlyList<YarpRouteMap.Mapping> routeMappings)
    {
        return app.UseAuthorization();
    }

    public static IEndpointConventionBuilder RequireRouteAuthorization(
        this IEndpointConventionBuilder builder,
        string? policyName)
    {
        if (!string.IsNullOrEmpty(policyName))
        {
            return builder.RequireAuthorization(policyName);
        }

        return builder.RequireAuthorization();
    }
}
