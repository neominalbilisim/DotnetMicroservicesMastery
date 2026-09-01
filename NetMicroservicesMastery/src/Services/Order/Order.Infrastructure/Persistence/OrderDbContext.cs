using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;

namespace Order.Infrastructure.Persistence;

/// <summary>
/// Order servisinin kendine ait, diğer servislerin veritabanına doğrudan erişmediği
/// (Database-per-Service) EF Core DbContext'i.
/// </summary>
public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderAggregate> Orders => Set<OrderAggregate>();

    /// <summary>
    /// Modül 4 - Outbox Pattern. Order kaydıyla AYNI transaction'da (aynı
    /// SaveChangesAsync çağrısında) yazılır — bkz. OutboxOrderEventPublisher.cs.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // TODO: Entity konfigürasyonları (IEntityTypeConfiguration) burada uygulanacak.
    }
}
