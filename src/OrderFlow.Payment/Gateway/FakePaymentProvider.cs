using System.Collections.Concurrent;

namespace OrderFlow.Payment.Gateway;

public sealed record Charge(Guid PaymentId, Guid OrderId, decimal Amount, DateTimeOffset ChargedAt);

public sealed record Refund(Guid PaymentId, Guid OrderId, decimal Amount, DateTimeOffset RefundedAt);

public abstract record ChargeResult
{
    public sealed record Succeeded(Charge Charge) : ChargeResult;

    public sealed record Declined(string Reason) : ChargeResult;
}

/// <summary>
/// Stands in for an external payment provider: decides one charge attempt. It remembers
/// nothing about orders; <see cref="Ledger.IPaymentLedger"/> does (spec 010). Test switches,
/// per customer so parallel tests do not interfere: decline their payments (spec 003, Q1) or
/// leave them undecided (spec 006, Q4). The switches live in memory and are lost on restart
/// (spec 010, non-goal).
/// </summary>
public sealed class FakePaymentProvider(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, string> _declines = new();
    private readonly ConcurrentDictionary<string, bool> _stalls = new();

    /// <summary>Charges or declines; null if the customer's payments are stalled (no decision yet).</summary>
    public ChargeResult? Attempt(Guid orderId, string customerId, decimal amount)
    {
        if (_stalls.ContainsKey(customerId))
        {
            return null;
        }

        return _declines.TryGetValue(customerId, out var reason)
            ? new ChargeResult.Declined(reason)
            : new ChargeResult.Succeeded(new Charge(Guid.NewGuid(), orderId, amount, clock.GetUtcNow()));
    }

    public void DeclineFor(string customerId, string reason) => _declines[customerId] = reason;

    public void StallFor(string customerId) => _stalls[customerId] = true;
}
