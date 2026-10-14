using Microsoft.EntityFrameworkCore;
using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Persistence;

/// <summary>A customer's test switch, as stored (spec 013, Q3).</summary>
public sealed class TestSwitchRow
{
    public required string CustomerId { get; init; }
    public string? DeclineReason { get; set; }
    public bool Stalled { get; set; }
}

/// <summary>
/// The fake provider's test switches in Payment's database, so every copy of the service reads the
/// same ones (spec 013, AC-2). Setting a switch is an upsert: setting it twice, or from two copies
/// at once, leaves one row.
/// </summary>
public sealed class EfTestSwitches(PaymentDbContext db) : ITestSwitches
{
    public async Task<TestSwitch?> ForAsync(string customerId, CancellationToken cancellationToken) =>
        await db.TestSwitches.AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .Select(s => new TestSwitch(s.DeclineReason, s.Stalled))
            .SingleOrDefaultAsync(cancellationToken);

    public Task DeclineAsync(string customerId, string reason, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO test_switches ("CustomerId", "DeclineReason", "Stalled") VALUES ({customerId}, {reason}, FALSE)
             ON CONFLICT ("CustomerId") DO UPDATE SET "DeclineReason" = EXCLUDED."DeclineReason"
             """,
            cancellationToken);

    public Task StallAsync(string customerId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO test_switches ("CustomerId", "DeclineReason", "Stalled") VALUES ({customerId}, NULL, TRUE)
             ON CONFLICT ("CustomerId") DO UPDATE SET "Stalled" = TRUE
             """,
            cancellationToken);
}
