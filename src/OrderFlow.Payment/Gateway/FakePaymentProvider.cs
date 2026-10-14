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
/// per customer so parallel tests do not interfere, decline their payments (spec 003, Q1) or
/// leave them undecided (spec 006, Q4). Like a real provider, it is the same for every copy of
/// the service: the switches are shared through <see cref="ITestSwitches"/> (spec 013).
/// </summary>
public sealed class FakePaymentProvider(ITestSwitches switches, TimeProvider clock)
{
    /// <summary>Charges or declines; null if the customer's payments are stalled (no decision yet).</summary>
    public async Task<ChargeResult?> AttemptAsync(Guid orderId, string customerId, decimal amount, CancellationToken cancellationToken)
    {
        switch (await switches.ForAsync(customerId, cancellationToken))
        {
            case { Stalled: true }:
                return null;

            case { DeclineReason: { } reason }:
                return new ChargeResult.Declined(reason);

            default:
                return new ChargeResult.Succeeded(new Charge(Guid.NewGuid(), orderId, amount, clock.GetUtcNow()));
        }
    }
}
