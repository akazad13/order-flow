using OrderFlow.Payment.Gateway;
using OrderFlow.Payment.Ledger;

namespace OrderFlow.Payment.Persistence;

/// <summary>
/// PostgreSQL storage (spec 010). Copies racing to decide an order both find no row; the second
/// insert fails on the primary key. Copies racing to refund both find Charged; the second update
/// fails on xmin. Either way the message is retried with a fresh scope and finds the decision.
/// Nothing is saved here: the consumer saves after publishing, so the consumer outbox commits the
/// decision and its announcement together (AC-3).
/// </summary>
public sealed class EfPaymentLedger(PaymentDbContext db, FakePaymentProvider provider, TimeProvider clock) : IPaymentLedger
{
    public async Task<ChargeResult?> ChargeOnceAsync(Guid orderId, string customerId, decimal amount, CancellationToken cancellationToken)
    {
        if (await db.Payments.FindAsync([orderId], cancellationToken) is not null)
        {
            return null;
        }

        var outcome = provider.Attempt(orderId, customerId, amount);
        switch (outcome)
        {
            case ChargeResult.Succeeded(var charge):
                db.Payments.Add(new PaymentRow
                {
                    OrderId = orderId, CustomerId = customerId, Status = PaymentStatus.Charged,
                    PaymentId = charge.PaymentId, Amount = charge.Amount, DecidedAt = charge.ChargedAt,
                });
                break;

            case ChargeResult.Declined(var reason):
                db.Payments.Add(new PaymentRow
                {
                    OrderId = orderId, CustomerId = customerId, Status = PaymentStatus.Declined,
                    DeclineReason = reason, DecidedAt = clock.GetUtcNow(),
                });
                break;
        }

        return outcome;
    }

    public async Task<Refund?> SettleAsync(Guid orderId, string customerId, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.FindAsync([orderId], cancellationToken);
        if (payment is null)
        {
            // Cancelled before it was charged: remember it, so it is never charged (spec 006).
            db.Payments.Add(new PaymentRow { OrderId = orderId, CustomerId = customerId, Status = PaymentStatus.Voided, DecidedAt = clock.GetUtcNow() });
            return null;
        }

        if (payment.Status != PaymentStatus.Charged)
        {
            return null;
        }

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAt = clock.GetUtcNow();
        return new Refund(payment.PaymentId!.Value, orderId, payment.Amount!.Value, payment.RefundedAt.Value);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
