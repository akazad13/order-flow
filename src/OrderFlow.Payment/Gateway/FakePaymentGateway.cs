using System.Collections.Concurrent;

namespace OrderFlow.Payment.Gateway;

public sealed record Charge(Guid PaymentId, Guid OrderId, decimal Amount, DateTimeOffset ChargedAt);

public interface IPaymentGateway
{
    Charge Charge(Guid orderId, decimal amount);
}

/// <summary>
/// Stands in for a real payment provider and records every charge so tests can
/// check the customer was charged exactly once. Always succeeds until spec 003
/// adds a failure switch.
/// </summary>
public sealed class FakePaymentGateway(TimeProvider clock) : IPaymentGateway
{
    private readonly ConcurrentQueue<Charge> _charges = new();

    public Charge Charge(Guid orderId, decimal amount)
    {
        var charge = new Charge(Guid.NewGuid(), orderId, amount, clock.GetUtcNow());
        _charges.Enqueue(charge);
        return charge;
    }

    public IReadOnlyList<Charge> ChargesFor(Guid orderId) => _charges.Where(c => c.OrderId == orderId).ToList();
}
