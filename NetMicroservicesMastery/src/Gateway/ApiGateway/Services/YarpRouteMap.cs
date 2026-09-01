namespace ApiGateway.Services;

/// <summary>
/// Modül 2 - Route (path) tanımları SABİTTİR; hangi path'in hangi backend
/// servise gideceği değişmez. Değişen tek şey, o backend servisin O ANKİ
/// sağlıklı instance adresleridir (bkz. ConsulYarpSyncHostedService) — bu
/// nedenle Route ve Consul servis adı eşlemesi tek bir merkezi listede
/// (burada) tutulur; hem Program.cs (ilk konfigürasyon) hem de
/// ConsulYarpSyncHostedService (periyodik güncelleme) bu listeyi kullanır.
/// </summary>
public static class YarpRouteMap
{
    public record Mapping(string RouteId, string ClusterId, string PathPattern, string ConsulServiceName);

    public static readonly Mapping[] All =
    [
        new("order-route", "order-cluster", "/api/orders/{**catch-all}", "order-service"),
        new("payment-route", "payment-cluster", "/api/payments/{**catch-all}", "payment-service"),
        new("inventory-route", "inventory-cluster", "/api/inventory/{**catch-all}", "inventory-service"),
    ];
}
