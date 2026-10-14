using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Persistence;

/// <summary>A catalog row. <see cref="Version"/> maps to PostgreSQL's xmin for optimistic concurrency.</summary>
public sealed class StockItemRow
{
    public required string Sku { get; init; }
    public required string Name { get; set; }
    public decimal UnitPrice { get; set; }
    public int Available { get; set; }
    public uint Version { get; set; }

    public CatalogItem ToItem() => new(Sku, Name, UnitPrice, Available);
}

/// <summary>
/// The stock decision for one order (spec 004): Held (with its lines), Rejected (no lines) or
/// Released. Kept after release, so a late repeat of the order is not held again.
/// </summary>
public sealed class ReservationRow
{
    public Guid OrderId { get; init; }
    public StockDecision Status { get; set; }
    public required List<ReservedLine> Lines { get; init; }
}

/// <summary>Stock, holds and the MassTransit outbox/inbox tables in one database (ADR 0005).</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DbSet<StockItemRow> StockItems => Set<StockItemRow>();

    public DbSet<ReservationRow> Reservations => Set<ReservationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItemRow>(item =>
        {
            item.ToTable("stock_items");
            item.HasKey(i => i.Sku);
            item.Property(i => i.Sku).HasMaxLength(100);
            item.Property(i => i.Name).HasMaxLength(200);
            item.Property(i => i.UnitPrice).HasPrecision(12, 2);
            item.Property(i => i.Version).IsRowVersion();
        });

        modelBuilder.Entity<ReservationRow>(reservation =>
        {
            reservation.ToTable("reservations");
            reservation.HasKey(r => r.OrderId);
            reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            reservation.Property(r => r.Lines)
                .HasColumnType("jsonb")
                .HasConversion(
                    lines => JsonSerializer.Serialize(lines, Json),
                    json => JsonSerializer.Deserialize<List<ReservedLine>>(json, Json)!,
                    new ValueComparer<List<ReservedLine>>(
                        (a, b) => a!.SequenceEqual(b!),
                        lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line)),
                        lines => lines.ToList()));
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without running the app's storage switch.</summary>
internal sealed class InventoryDbContextDesignFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql("Host=localhost;Database=inventory").Options);
}
