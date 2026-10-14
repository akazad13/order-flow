using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.Gateway;
using OrderFlow.Payment.Ledger;

namespace OrderFlow.Payment.Consumers;

/// <summary>
/// Payment is only ever requested here, after stock is held (spec 001, R-2).
/// </summary>
public sealed class StockReservedConsumer(IPaymentLedger ledger, TimeProvider clock) : IConsumer<StockReserved>
{
    public async Task Consume(ConsumeContext<StockReserved> context)
    {
        var order = context.Message;

        switch (await ledger.ChargeOnceAsync(order.OrderId, order.CustomerId, order.Total, context.CancellationToken))
        {
            case null:
                // A repeat (spec 004, R-1), even after a restart (spec 010), a voided order
                // (spec 006), or a stalled customer: nothing to announce.
                break;

            case ChargeResult.Succeeded(var charge):
                await context.Publish(new PaymentSucceeded(order.OrderId, charge.PaymentId, charge.Amount, charge.ChargedAt));
                break;

            case ChargeResult.Declined(var reason):
                await context.Publish(new PaymentFailed(order.OrderId, reason, clock.GetUtcNow()));
                break;
        }

        // The decision and its announcement commit together (spec 010, AC-3).
        await ledger.SaveChangesAsync(context.CancellationToken);
    }
}
