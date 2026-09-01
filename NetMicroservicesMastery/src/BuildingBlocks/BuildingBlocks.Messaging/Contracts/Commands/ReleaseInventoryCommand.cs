using System;

namespace BuildingBlocks.Messaging.Contracts.Commands;

/// <summary>
/// Modül 4 - Saga Pattern'in COMPENSATING (telafi edici) adımı. Order.Api,
/// ödeme başarısız olduğunda bunu Inventory.Api'ye SEND eder (Command —
/// SADECE Inventory.Api dinler) — az önce rezerve edilen stoğun geri
/// bırakılmasını talep eder.
/// </summary>
public record ReleaseInventoryCommand(
    Guid CommandId,
    Guid OrderId) : IIntegrationCommand
{
    public string PartitionKey => OrderId.ToString();
}
