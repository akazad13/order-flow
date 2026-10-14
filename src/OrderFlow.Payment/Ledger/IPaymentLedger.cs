using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Ledger;

/// <summary>What happened to an order's payment. The first of Charged, Declined or Voided wins; Charged may become Refunded, once (spec 010, R-1).</summary>
public enum PaymentStatus
{
    Charged,
    Declined,
    Voided,
    Refunded,
}

/// <summary>
/// Remembers each order's payment decision, in memory (<see cref="InMemoryPaymentLedger"/>) or
/// PostgreSQL (<c>EfPaymentLedger</c>), chosen by <c>Persistence:Provider</c> (ADR 0005, spec 010).
/// </summary>
public interface IPaymentLedger
{
    /// <summary>
    /// Decides an order's payment once (spec 004, R-2): the first call asks the provider to charge;
    /// a repeat, or an order already voided (spec 006), returns null and charges nothing. Also null
    /// while the customer's payments are stalled.
    /// </summary>
    Task<ChargeResult?> ChargeOnceAsync(Guid orderId, string customerId, decimal amount, CancellationToken cancellationToken);

    /// <summary>
    /// Settles a cancelled order's payment once (spec 006, AC-5): refunds a charge that was taken,
    /// or voids a payment not decided yet so it is never taken. Returns the refund the first time a
    /// charge is refunded; null otherwise.
    /// </summary>
    Task<Refund?> SettleAsync(Guid orderId, string customerId, CancellationToken cancellationToken);

    /// <summary>Commits decisions together with the messages published in this scope (the consumer outbox, spec 010 AC-3).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
