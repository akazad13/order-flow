using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Ledger;

/// <summary>
/// The in-memory storage mode (ADR 0005): component tests and plain <c>dotnet run</c>. Forgets
/// everything on restart, which is why compose uses PostgreSQL (spec 010). Records every charge
/// and refund so tests can check the customer was charged exactly once, or not at all.
/// </summary>
public sealed class InMemoryPaymentLedger(FakePaymentProvider provider, TimeProvider clock) : IPaymentLedger
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ChargeResult> _outcomes = new();
    private readonly HashSet<Guid> _voided = [];
    private readonly Dictionary<Guid, Refund> _refunds = new();

    public Task<ChargeResult?> ChargeOnceAsync(Guid orderId, string customerId, decimal amount, CancellationToken cancellationToken)
    {
        // One lock, so two copies (or a charge and a cancellation) arriving at the same moment
        // cannot both decide.
        lock (_lock)
        {
            if (_outcomes.ContainsKey(orderId) || _voided.Contains(orderId))
            {
                return Task.FromResult<ChargeResult?>(null);
            }

            var outcome = provider.Attempt(orderId, customerId, amount);
            if (outcome is not null)
            {
                _outcomes[orderId] = outcome;
            }

            return Task.FromResult(outcome);
        }
    }

    public Task<Refund?> SettleAsync(Guid orderId, string customerId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_outcomes.TryGetValue(orderId, out var outcome))
            {
                _voided.Add(orderId);
                return Task.FromResult<Refund?>(null);
            }

            if (outcome is not ChargeResult.Succeeded(var charge) || _refunds.ContainsKey(orderId))
            {
                return Task.FromResult<Refund?>(null);
            }

            var refund = new Refund(charge.PaymentId, orderId, charge.Amount, clock.GetUtcNow());
            _refunds[orderId] = refund;
            return Task.FromResult<Refund?>(refund);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Charges actually taken (refunded or not).</summary>
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
