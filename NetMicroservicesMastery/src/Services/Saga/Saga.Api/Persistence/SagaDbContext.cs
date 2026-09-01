using Microsoft.EntityFrameworkCore;
using Saga.Api.Sagas;

namespace Saga.Api.Persistence;

/// <summary>
/// Saga.Api'nin kendine ait veritabanı (Database-per-Service — saga_db).
/// OrderSagaState: MassTransit'in Saga repository'sinin okuyup yazdığı tablo.
/// OrderSagaStateHistory: "event streaming" izleme tablosu (bkz. o sınıftaki açıklama).
/// </summary>
public class SagaDbContext(DbContextOptions<SagaDbContext> options) : DbContext(options)
{
    public DbSet<OrderSagaState> OrderSagas => Set<OrderSagaState>();
    public DbSet<OrderSagaStateHistory> OrderSagaHistory => Set<OrderSagaStateHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderSagaState>(entity =>
        {
            entity.HasKey(x => x.CorrelationId);
            entity.Property(x => x.CurrentState).HasMaxLength(64);

            // PostgreSQL'in native "xmin" sistem kolonunu optimistic concurrency
            // token'ı olarak kullanır (shadow property) — MassTransit'in EF Core
            // saga repository'si eşzamanlı güncellemeleri (aynı saga instance'ına
            // iki mesaj aynı anda gelirse) güvenli şekilde yönetmek için bir
            // concurrency token gerektirir. (Not: `UseXminAsConcurrencyToken()`
            // tek satırlık extension'ı bazı Npgsql.EntityFrameworkCore.PostgreSQL
            // sürümlerinde/using kombinasyonlarında derleme hatası verebiliyor —
            // bu, doğrudan shadow property tanımlayan daha temel/kesin yöntemdir.)
            entity.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsRowVersion();
        });

        modelBuilder.Entity<OrderSagaStateHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
        });
    }
}
