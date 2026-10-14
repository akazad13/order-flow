using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

/// <summary>
/// Tells the customer why their order was cancelled, in plain words (specs 002, 003, 006, 016). A
/// reason it does not know still gets the generic text, though a new reason now comes with a new
/// version of the event (spec 016). Reads both versions: v1 until it is retired (ADR 0010).
/// </summary>
public sealed class OrderCancelledConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<OrderCancelledV2>, IConsumer<OrderCancelled>
{
    public static string TextFor(string reason) => reason switch
    {
        CancellationReasons.OutOfStock => "Your order was cancelled: some items are out of stock",
        CancellationReasons.PaymentDeclined => "Your order was cancelled: your payment was declined",
        CancellationReasons.CustomerRequested => "Your order was cancelled at your request",
        CancellationReasons.StaffRequested => "Your order was cancelled by our support team at your request",
        _ => "Your order was cancelled",
    };

    public Task Consume(ConsumeContext<OrderCancelled> context) => Handle(context, context.Message.ToV2());

    public Task Consume(ConsumeContext<OrderCancelledV2> context) => Handle(context, context.Message);

    private async Task Handle(ConsumeContext context, OrderCancelledV2 order)
    {
        await inbox.DeliverAsync(new InboxMessage(Guid.NewGuid(), order.CustomerId, order.OrderId, TextFor(order.Reason), order.CancelledAt, clock.GetUtcNow()), context.CancellationToken);
        await inbox.SaveChangesAsync(context.CancellationToken);
    }
}
