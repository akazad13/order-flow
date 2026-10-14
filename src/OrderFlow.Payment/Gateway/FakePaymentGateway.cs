using System.Collections.Concurrent;

namespace OrderFlow.Payment.Gateway;

public sealed record Charge(Guid PaymentId, Guid OrderId, decimal Amount, DateTimeOffset ChargedAt);

public abstract record ChargeResult
{
    public sealed record Succeeded(Charge Charge) : ChargeResult;

    public sealed record Declined(string Reason) : ChargeResult;
}

public interface IPaymentGateway
{
    /// <summary>
    /// Decides an order's payment once (spec 004, R-2): the first call charges or declines;
    /// a repeat for the same order returns null and charges nothing.
    /// </summary>
    ChargeResult? ChargeOnce(Guid orderId, string customerId, decimal amount);
}

/// <summary>
/// Stands in for a real payment provider and records every successful charge so tests can
/// check the customer was charged exactly once, or not at all. Declines a customer's
/// payments once a test asks it to (spec 003, Q1): per customer, so parallel tests do
/// not interfere.
/// </summary>
public sealed class FakePaymentGateway(TimeProvider clock) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, string> _declines = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ChargeResult> _outcomes = new();

    public ChargeResult? ChargeOnce(Guid orderId, string customerId, decimal amount)
    {
        // One lock, so two copies arriving at the same moment cannot both decide.
        lock (_lock)
        {
            if (_outcomes.ContainsKey(orderId))
            {
                return null;
            }

            var outcome = Decide(orderId, customerId, amount);
            _outcomes[orderId] = outcome;
            return outcome;
        }
    }

    public void DeclineFor(string customerId, string reason) => _declines[customerId] = reason;

    private ChargeResult Decide(Guid orderId, string customerId, decimal amount)
    {
        if (_declines.TryGetValue(customerId, out var reason))
        {
            return new ChargeResult.Declined(reason);
        }

        return new ChargeResult.Succeeded(new Charge(Guid.NewGuid(), orderId, amount, clock.GetUtcNow()));
    }

    /// <summary>Charges actually taken: the outcome stored for the order, if it was a charge.</summary>
    public IReadOnlyList<Charge> ChargesFor(Guid orderId)
    {
        lock (_lock)
        {
            return _outcomes.TryGetValue(orderId, out var outcome) && outcome is ChargeResult.Succeeded(var charge) ? [charge] : [];
        }
    }
}
