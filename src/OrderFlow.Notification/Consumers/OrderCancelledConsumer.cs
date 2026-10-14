using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

/// <summary>
/// Tells the customer why their order was cancelled, in plain words (specs 002, 003, 006). A reason
/// it does not know gets the generic text: new reasons are additive (spec 006, Q3).
/// </summary>
public sealed class OrderCancelledConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<OrderCancelled>
{
    public static string TextFor(string reason) => reason switch
    {
        CancellationReasons.OutOfStock => "Your order was cancelled: some items are out of stock",
        CancellationReasons.PaymentDeclined => "Your order was cancelled: your payment was declined",
        CancellationReasons.CustomerRequested => "Your order was cancelled at your request",
        _ => "Your order was cancelled",
    };

    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var order = context.Message;
        await inbox.DeliverAsync(new InboxMessage(Guid.NewGuid(), order.CustomerId, order.OrderId, TextFor(order.Reason), order.CancelledAt, clock.GetUtcNow()), context.CancellationToken);
        await inbox.SaveChangesAsync(context.CancellationToken);
    }
}
