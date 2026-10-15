using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Gateway.Persistence;

/// <summary>One login session: the serialized ticket, tokens included, keyed by what the cookie holds (spec 023).</summary>
public sealed class SessionRow
{
    public required string Key { get; init; }
    public required byte[] Ticket { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Sessions and the keys that protect the cookie, shared by every copy of the gateway (spec 023, AC-6).
/// </summary>
public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionRow>(session =>
        {
            session.ToTable("sessions");
            session.HasKey(s => s.Key);
            session.Property(s => s.Key).HasMaxLength(64);
            session.HasIndex(s => s.ExpiresAt);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without running the app's storage switch.</summary>
internal sealed class GatewayDbContextDesignFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql("Host=localhost;Database=gateway").Options);
}
