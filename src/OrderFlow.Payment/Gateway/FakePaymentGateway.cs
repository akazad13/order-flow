using System.Collections.Concurrent;

namespace OrderFlow.Payment.Gateway;

public sealed record Charge(Guid PaymentId, Guid OrderId, decimal Amount, DateTimeOffset ChargedAt);

public sealed record Refund(Guid PaymentId, Guid OrderId, decimal Amount, DateTimeOffset RefundedAt);

public abstract record ChargeResult
{
    public sealed record Succeeded(Charge Charge) : ChargeResult;

    public sealed record Declined(string Reason) : ChargeResult;
}

public interface IPaymentGateway
{
    /// <summary>
    /// Decides an order's payment once (spec 004, R-2): the first call charges or declines;
    /// a repeat for the same order, or an order already voided (spec 006), returns null and
    /// charges nothing.
    /// </summary>
    ChargeResult? ChargeOnce(Guid orderId, string customerId, decimal amount);

    /// <summary>
    /// Settles a cancelled order's payment once (spec 006, AC-5): refunds a charge that was
    /// taken, or voids a payment not decided yet so it is never taken. Returns the refund the
    /// first time a charge is refunded; null otherwise (voided, declined, or already settled).
    /// </summary>
    Refund? Settle(Guid orderId);
}

/// <summary>
/// Stands in for a real payment provider and records every charge and refund so tests can
/// check the customer was charged exactly once, or not at all. Test switches, per customer so
/// parallel tests do not interfere: decline their payments (spec 003, Q1), or leave them
/// undecided (spec 006, Q4).
/// </summary>
public sealed class FakePaymentGateway(TimeProvider clock) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, string> _declines = new();
    private readonly ConcurrentDictionary<string, bool> _stalls = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ChargeResult> _outcomes = new();
    private readonly HashSet<Guid> _voided = [];
    private readonly Dictionary<Guid, Refund> _refunds = new();

    public ChargeResult? ChargeOnce(Guid orderId, string customerId, decimal amount)
    {
        // One lock, so two copies (or a charge and a cancellation) arriving at the same moment
        // cannot both decide.
        lock (_lock)
        {
            if (_outcomes.ContainsKey(orderId) || _voided.Contains(orderId) || _stalls.ContainsKey(customerId))
            {
                return null;
            }

            var outcome = Decide(orderId, customerId, amount);
            _outcomes[orderId] = outcome;
            return outcome;
        }
    }

    public Refund? Settle(Guid orderId)
    {
        lock (_lock)
        {
            if (!_outcomes.TryGetValue(orderId, out var outcome))
            {
                _voided.Add(orderId);
                return null;
            }

            if (outcome is not ChargeResult.Succeeded(var charge) || _refunds.ContainsKey(orderId))
            {
                return null;
            }

            var refund = new Refund(charge.PaymentId, orderId, charge.Amount, clock.GetUtcNow());
            _refunds[orderId] = refund;
            return refund;
        }
    }

    public void DeclineFor(string customerId, string reason) => _declines[customerId] = reason;

    public void StallFor(string customerId) => _stalls[customerId] = true;

    private ChargeResult Decide(Guid orderId, string customerId, decimal amount)
    {
        if (_declines.TryGetValue(customerId, out var reason))
        {
            return new ChargeResult.Declined(reason);
        }

        return new ChargeResult.Succeeded(new Charge(Guid.NewGuid(), orderId, amount, clock.GetUtcNow()));
    }

    /// <summary>Charges actually taken (refunded or not): the outcome stored for the order, if it was a charge.</summary>
    public IReadOnlyList<Charge> ChargesFor(Guid orderId)
    {
        lock (_lock)
        {
            return _outcomes.TryGetValue(orderId, out var outcome) && outcome is ChargeResult.Succeeded(var charge) ? [charge] : [];
        }
    }

    /// <summary>Refunds made for the order.</summary>
    public IReadOnlyList<Refund> RefundsFor(Guid orderId)
    {
        lock (_lock)
        {
            return _refunds.TryGetValue(orderId, out var refund) ? [refund] : [];
        }
    }
}
