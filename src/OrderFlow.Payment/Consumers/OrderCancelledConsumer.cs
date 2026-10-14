using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.Ledger;

namespace OrderFlow.Payment.Consumers;

/// <summary>
/// No money is kept for a cancelled order (spec 006, AC-5), whatever arrived first: a charge
/// already taken is refunded once; a payment not decided yet is voided and never taken.
/// Reads both versions of the event: v1 until it is retired (spec 016, ADR 0010).
/// </summary>
public sealed class OrderCancelledConsumer(IPaymentLedger ledger) : IConsumer<OrderCancelledV2>, IConsumer<OrderCancelled>
{
    public Task Consume(ConsumeContext<OrderCancelled> context) => Handle(context, context.Message.ToV2());

    public Task Consume(ConsumeContext<OrderCancelledV2> context) => Handle(context, context.Message);

    private async Task Handle(ConsumeContext context, OrderCancelledV2 order)
    {
        if (await ledger.SettleAsync(order.OrderId, order.CustomerId, context.CancellationToken) is { } refund)
        {
            await context.Publish(new PaymentRefunded(order.OrderId, order.CustomerId, refund.PaymentId, refund.Amount, refund.RefundedAt));
        }

        // Saved even with nothing to announce: a void is a decision (spec 010, AC-2).
        await ledger.SaveChangesAsync(context.CancellationToken);
    }
}
