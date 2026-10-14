using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Ledger;

/// <summary>
/// The in-memory storage mode (ADR 0005): component tests and plain <c>dotnet run</c>. Forgets
/// everything on restart, which is why compose uses PostgreSQL (spec 010). Records every charge
/// and refund so tests can check the customer was charged exactly once, or not at all.
/// </summary>
public sealed class InMemoryPaymentLedger(FakePaymentProvider provider, TimeProvider clock) : IPaymentLedger
{
    // A semaphore rather than a lock: asking the provider is asynchronous (spec 013).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, ChargeResult> _outcomes = new();
    private readonly HashSet<Guid> _voided = [];
    private readonly Dictionary<Guid, Refund> _refunds = new();

    public async Task<ChargeResult?> ChargeOnceAsync(Guid orderId, string customerId, decimal amount, CancellationToken cancellationToken)
    {
        // One gate, so two copies (or a charge and a cancellation) arriving at the same moment
        // cannot both decide.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_outcomes.ContainsKey(orderId) || _voided.Contains(orderId))
            {
                return null;
            }

            var outcome = await provider.AttemptAsync(orderId, customerId, amount, cancellationToken);
            if (outcome is not null)
            {
                _outcomes[orderId] = outcome;
            }

            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Refund?> SettleAsync(Guid orderId, string customerId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
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
        finally
        {
            _gate.Release();
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Charges actually taken (refunded or not).</summary>
    public IReadOnlyList<Charge> ChargesFor(Guid orderId)
    {
        _gate.Wait();
        try
        {
            return _outcomes.TryGetValue(orderId, out var outcome) && outcome is ChargeResult.Succeeded(var charge) ? [charge] : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Refunds made for the order.</summary>
    public IReadOnlyList<Refund> RefundsFor(Guid orderId)
    {
        _gate.Wait();
        try
        {
            return _refunds.TryGetValue(orderId, out var refund) ? [refund] : [];
        }
        finally
        {
            _gate.Release();
        }
    }
}
