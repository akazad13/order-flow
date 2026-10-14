using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Persistence;

/// <summary>One message in a customer's inbox (spec 011).</summary>
public sealed class InboxMessageRow
{
    public Guid MessageId { get; init; }
    public required string CustomerId { get; init; }
    public Guid OrderId { get; init; }
    public required string Text { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }

    public InboxMessage ToMessage() => new(MessageId, CustomerId, OrderId, Text, OccurredAt, ReceivedAt);
}

/// <summary>
/// Customers' inboxes. No MassTransit outbox tables: this service publishes nothing (spec 011, Q1).
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<InboxMessageRow> Messages => Set<InboxMessageRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessageRow>(message =>
        {
            message.ToTable("inbox_messages");
            message.HasKey(m => m.MessageId);
            message.Property(m => m.CustomerId).HasMaxLength(200);
            message.Property(m => m.Text).HasMaxLength(500);
            message.HasIndex(m => m.CustomerId);

            // R-1: one message per order and text, whatever restarts in between.
            message.HasIndex(m => new { m.OrderId, m.Text }).IsUnique();
        });
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without running the app's storage switch.</summary>
internal sealed class NotificationDbContextDesignFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql("Host=localhost;Database=notifications").Options);
}
