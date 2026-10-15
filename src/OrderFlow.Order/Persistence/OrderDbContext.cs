using System.Text.Json;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.OrderService.Domain;
using OrderFlow.OrderService.StateMachine;

namespace OrderFlow.OrderService.Persistence;

/// <summary>
/// Orders, the state machine's instances and the MassTransit outbox/inbox tables, in one
/// database so a state change and its messages commit in one transaction (ADR 0005).
/// </summary>
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        new OrderStateMap().Configure(modelBuilder);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Order> order)
    {
        order.ToTable("orders");
        order.HasKey(o => o.Id);
        order.Property(o => o.CustomerId).HasMaxLength(200);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
        order.Property(o => o.Total).HasPrecision(12, 2);
        order.Property(o => o.CancellableUntil); // spec 022

        // Lines never change after placing; one jsonb column is simpler than a child table.
        order.Property(o => o.Lines)
            .HasColumnType("jsonb")
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, Json),
                json => JsonSerializer.Deserialize<List<OrderLine>>(json, Json)!,
                new ValueComparer<IReadOnlyList<OrderLine>>(
                    (a, b) => a!.SequenceEqual(b!),
                    lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line)),
                    lines => lines.ToList()));

        order.OwnsOne(o => o.Cancellation, cancellation =>
        {
            cancellation.Property(c => c.Reason).HasColumnName("cancellation_reason").HasMaxLength(32);
            cancellation.Property(c => c.Detail).HasColumnName("cancellation_detail").HasMaxLength(1000);
        });
    }
}

internal sealed class OrderStateMap : SagaClassMap<OrderState>
{
    protected override void Configure(EntityTypeBuilder<OrderState> entity, ModelBuilder model)
    {
        entity.ToTable("order_states");
        entity.Property(x => x.CurrentState).HasMaxLength(64);
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without running the app's storage switch.</summary>
internal sealed class OrderDbContextDesignFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrderDbContext>().UseNpgsql("Host=localhost;Database=orders").Options);
}
