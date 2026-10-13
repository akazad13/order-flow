using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Consumers;

public sealed class OrderPlacedConsumer(IFakeInbox inbox, TimeProvider clock) : IConsumer<OrderPlaced>
{
    public const string OrderReceivedText = "We received your order";

    public Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var order = context.Message;
        inbox.Deliver(new InboxMessage(Guid.NewGuid(), order.CustomerId, order.OrderId, OrderReceivedText, order.PlacedAt, clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}
