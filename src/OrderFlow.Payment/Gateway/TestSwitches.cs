using System.Collections.Concurrent;

namespace OrderFlow.Payment.Gateway;

/// <summary>A customer's test switch: decline their payments with a reason (spec 003, Q1), or leave them undecided (spec 006, Q4).</summary>
public sealed record TestSwitch(string? DeclineReason, bool Stalled);

/// <summary>
/// Where the fake provider's test switches are kept: in memory (<see cref="InMemoryTestSwitches"/>)
/// or in Payment's database (<c>EfTestSwitches</c>), so that a switch set through one copy of the
/// service applies on every copy (spec 013, AC-2).
/// </summary>
public interface ITestSwitches
{
    Task<TestSwitch?> ForAsync(string customerId, CancellationToken cancellationToken);

    Task DeclineAsync(string customerId, string reason, CancellationToken cancellationToken);

    Task StallAsync(string customerId, CancellationToken cancellationToken);
}

/// <summary>Component tests and plain <c>dotnet run</c>: one copy, switches lost on restart.</summary>
public sealed class InMemoryTestSwitches : ITestSwitches
{
    private readonly ConcurrentDictionary<string, TestSwitch> _switches = new();

    public Task<TestSwitch?> ForAsync(string customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_switches.GetValueOrDefault(customerId));

    public Task DeclineAsync(string customerId, string reason, CancellationToken cancellationToken)
    {
        _switches.AddOrUpdate(customerId, new TestSwitch(reason, false), (_, current) => current with { DeclineReason = reason });
        return Task.CompletedTask;
    }

    public Task StallAsync(string customerId, CancellationToken cancellationToken)
    {
        _switches.AddOrUpdate(customerId, new TestSwitch(null, true), (_, current) => current with { Stalled = true });
        return Task.CompletedTask;
    }
}
