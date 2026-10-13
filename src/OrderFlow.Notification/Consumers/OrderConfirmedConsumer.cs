using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

public sealed class OrderConfirmedConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<OrderConfirmed>
{
    public const string OrderConfirmedText = "Your order is confirmed";

    public Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var order = context.Message;
        inbox.Deliver(new InboxMessage(Guid.NewGuid(), order.CustomerId, order.OrderId, OrderConfirmedText, order.ConfirmedAt, clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}
