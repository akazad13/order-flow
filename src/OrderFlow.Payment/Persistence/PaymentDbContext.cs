using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderFlow.Payment.Ledger;

namespace OrderFlow.Payment.Persistence;

/// <summary>
/// One order's payment decision (spec 010). <see cref="PaymentId"/> and <see cref="Amount"/> are set
/// for a charge; <see cref="DeclineReason"/> for a decline; a voided order has neither.
/// <see cref="Version"/> maps to PostgreSQL's xmin, so two refunds of one charge cannot both win.
/// </summary>
public sealed class PaymentRow
{
    public Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public PaymentStatus Status { get; set; }
    public Guid? PaymentId { get; init; }
    public decimal? Amount { get; init; }
    public string? DeclineReason { get; init; }
    public DateTimeOffset DecidedAt { get; init; }
    public DateTimeOffset? RefundedAt { get; set; }
    public uint Version { get; set; }
}

/// <summary>Payment decisions and the MassTransit outbox/inbox tables in one database (ADR 0005, spec 010).</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRow> Payments => Set<PaymentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentRow>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.OrderId);
            payment.Property(p => p.CustomerId).HasMaxLength(200);
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
            payment.Property(p => p.Amount).HasPrecision(12, 2);
            payment.Property(p => p.DeclineReason).HasMaxLength(500);
            payment.Property(p => p.Version).IsRowVersion();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without running the app's storage switch.</summary>
internal sealed class PaymentDbContextDesignFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql("Host=localhost;Database=payments").Options);
}
