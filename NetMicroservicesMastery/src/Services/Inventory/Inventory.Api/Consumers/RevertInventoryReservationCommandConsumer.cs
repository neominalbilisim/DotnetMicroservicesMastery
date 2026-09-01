using BuildingBlocks.Messaging.Contracts.Saga;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Consumers;

/// <summary>
/// Modül 4 - Saga Pattern (ORCHESTRATION)'ın COMPENSATING (telafi edici)
/// adımının alıcı tarafı. Saga.Api, ödeme başarısız olduğunda bunu gönderir
/// — az önce rezerve edilen stoğu SİMÜLE OLARAK geri bırakır. Choreography
/// örneğindeki ReleaseInventoryCommandConsumer ile aynı işi yapar, ama
/// Saga.Api'den (RabbitMQ) gelir.
/// </summary>
public class RevertInventoryReservationCommandConsumer(ILogger<RevertInventoryReservationCommandConsumer> logger)
    : IConsumer<RevertInventoryReservationCommand>
{
    public Task Consume(ConsumeContext<RevertInventoryReservationCommand> context)
    {
        var orderId = context.Message.CorrelationId;

        Console.WriteLine($"↩️ [Inventory.Api/SagaOrchestrator] TELAFİ (COMPENSATION) — Stok geri bırakıldı (simüle) — OrderId={orderId}");
        logger.LogInformation("[Inventory.Api/SagaOrchestrator] Stok rezervasyonu geri alındı (compensation): OrderId={OrderId}", orderId);

        return Task.CompletedTask;
    }
}
