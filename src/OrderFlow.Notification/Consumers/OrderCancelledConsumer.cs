using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

/// <summary>Tells the customer why their order was cancelled, in plain words (specs 002, 003).</summary>
public sealed class OrderCancelledConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<OrderCancelled>
{
    public static string TextFor(string reason) => reason switch
    {
        CancellationReasons.OutOfStock => "Your order was cancelled: some items are out of stock",
        CancellationReasons.PaymentDeclined => "Your order was cancelled: your payment was declined",
        _ => "Your order was cancelled",
    };

    public Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var order = context.Message;
        inbox.Deliver(new InboxMessage(Guid.NewGuid(), order.CustomerId, order.OrderId, TextFor(order.Reason), order.CancelledAt, clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}
