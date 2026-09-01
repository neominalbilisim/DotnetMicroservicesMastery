using Microsoft.EntityFrameworkCore;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Idempotency;

namespace Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory servisinin kendine ait, diğer servislerin veritabanına doğrudan erişmediği
/// (Database-per-Service) EF Core DbContext'i.
/// TODO (Modül 4): MassTransit Outbox tablolarını (AddOutboxEntities) buraya dahil edin.
/// </summary>
public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryAggregate> Inventorys => Set<InventoryAggregate>();

    /// <summary>Modül 5 - Idempotent Consumer için işlenmiş mesaj kayıtları.</summary>
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // TODO: Entity konfigürasyonları (IEntityTypeConfiguration) burada uygulanacak.
        // TODO (Modül 4): modelBuilder.AddInboxStateEntity(); AddOutboxMessageEntity(); AddOutboxStateEntity();

        modelBuilder.Entity<ProcessedMessage>().HasKey(x => x.MessageId);
    }
}
