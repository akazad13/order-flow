using System.Globalization;
using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

/// <summary>Tells the customer their payment for a cancelled order was refunded (spec 006, AC-7).</summary>
public sealed class PaymentRefundedConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<PaymentRefunded>
{
    public static string TextFor(decimal amount) =>
        $"Your payment of {amount.ToString("0.00", CultureInfo.InvariantCulture)} was refunded";

    public Task Consume(ConsumeContext<PaymentRefunded> context)
    {
        var refund = context.Message;
        inbox.Deliver(new InboxMessage(Guid.NewGuid(), refund.CustomerId, refund.OrderId, TextFor(refund.Amount), refund.RefundedAt, clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}
