using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Consumers;

/// <summary>
/// No money is kept for a cancelled order (spec 006, AC-5), whatever arrived first: a charge
/// already taken is refunded once; a payment not decided yet is voided and never taken.
/// </summary>
public sealed class OrderCancelledConsumer(IPaymentGateway gateway) : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var order = context.Message;
        if (gateway.Settle(order.OrderId) is not { } refund)
        {
            return;
        }

        await context.Publish(new PaymentRefunded(order.OrderId, order.CustomerId, refund.PaymentId, refund.Amount, refund.RefundedAt));
    }
}
