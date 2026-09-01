using BuildingBlocks.Messaging.Contracts.Commands;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern'in COMPENSATING (telafi edici) adımının alıcı
/// tarafı. Order.Api, ödeme başarısız olduğunda bunu SEND eder — SADECE
/// Inventory.Api dinler (bu bir Command'dır, Event değil). Az önce
/// rezerve edilen stoğu SİMÜLE OLARAK geri bırakır.
/// </summary>
public class ReleaseInventoryCommandConsumer(ILogger<ReleaseInventoryCommandConsumer> logger) : IConsumer<ReleaseInventoryCommand>
{
    public Task Consume(ConsumeContext<ReleaseInventoryCommand> context)
    {
        var orderId = context.Message.OrderId;

        Console.WriteLine($"↩️ [Inventory.Api/Saga] TELAFİ (COMPENSATION) — Stok geri bırakıldı (simüle) — OrderId={orderId}");
        logger.LogInformation("[Inventory.Api/Saga] Stok rezervasyonu geri alındı (compensation): OrderId={OrderId}", orderId);

        return Task.CompletedTask;
    }
}
