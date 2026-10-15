using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Persistence;

namespace OrderFlow.OrderService.Api;

/// <summary>Methods the hub calls on a client. Matches contracts/realtime/order-updates.asyncapi.yaml.</summary>
public interface IOrderUpdatesClient
{
    Task OrderUpdated(OrderResponse order);
}

/// <summary>
/// Live order status (spec 005). A client joins one group per watched order. Changes made
/// before it joined are not replayed: the client reads the order once after joining (AC-3).
/// Connecting needs a login, and only the order's customer, or staff, may watch it (specs 012, 015).
/// </summary>
[Authorize]
public sealed class OrderUpdatesHub(IOrderRepository orders) : Hub<IOrderUpdatesClient>
{
    public const string Path = "/hubs/orders";

    public async Task WatchOrder(Guid orderId)
    {
        // Someone else's order is refused exactly like an unknown one (spec 012, R-2), unless the caller is staff (spec 015).
        if (await orders.FindAllowedAsync(orderId, Context.User!, Context.ConnectionAborted) is null)
        {
            throw new HubException("Order not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(orderId));
    }

    internal static string GroupFor(Guid orderId) => $"order-{orderId}";
}

/// <summary>
/// Pushes an order to its watchers once its final status is committed (spec 005, Q2): the
/// outbox only delivers OrderConfirmed and OrderCancelled after the state machine's
/// transaction commits, so the order read here already has the new status.
/// </summary>
public sealed class OrderUpdatesConsumer(IOrderRepository orders, IHubContext<OrderUpdatesHub, IOrderUpdatesClient> hub)
    : IConsumer<OrderConfirmed>, IConsumer<OrderCancelledV2>
{
    public Task Consume(ConsumeContext<OrderConfirmed> context) => Push(context.Message.OrderId, context.CancellationToken);

    // Spec 016: the current version of the event (v1 retired, spec 018).
    public Task Consume(ConsumeContext<OrderCancelledV2> context) => Push(context.Message.OrderId, context.CancellationToken);

    private async Task Push(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        // Sending twice (a repeated event, spec 004) only shows the same thing twice.
        await hub.Clients.Group(OrderUpdatesHub.GroupFor(orderId)).OrderUpdated(OrderResponse.From(order));
    }
}
