using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

public sealed class OrderConfirmedConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<OrderConfirmed>
{
    public const string OrderConfirmedText = "Your order is confirmed";

    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var order = context.Message;
        await inbox.DeliverAsync(new InboxMessage(Guid.NewGuid(), order.CustomerId, order.OrderId, OrderConfirmedText, order.ConfirmedAt, clock.GetUtcNow()), context.CancellationToken);
        await inbox.SaveChangesAsync(context.CancellationToken);
    }
}
