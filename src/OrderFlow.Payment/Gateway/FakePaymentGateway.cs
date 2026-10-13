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
    ChargeResult Charge(Guid orderId, string customerId, decimal amount);
}

/// <summary>
/// Stands in for a real payment provider and records every successful charge so tests can
/// check the customer was charged exactly once, or not at all. Declines a customer's
/// payments once a test asks it to (spec 003, Q1): per customer, so parallel tests do
/// not interfere.
/// </summary>
public sealed class FakePaymentGateway(TimeProvider clock) : IPaymentGateway
{
    private readonly ConcurrentQueue<Charge> _charges = new();
    private readonly ConcurrentDictionary<string, string> _declines = new();

    public ChargeResult Charge(Guid orderId, string customerId, decimal amount)
    {
        if (_declines.TryGetValue(customerId, out var reason))
        {
            return new ChargeResult.Declined(reason);
        }

        var charge = new Charge(Guid.NewGuid(), orderId, amount, clock.GetUtcNow());
        _charges.Enqueue(charge);
        return new ChargeResult.Succeeded(charge);
    }

    public void DeclineFor(string customerId, string reason) => _declines[customerId] = reason;

    public IReadOnlyList<Charge> ChargesFor(Guid orderId) => _charges.Where(c => c.OrderId == orderId).ToList();
}
