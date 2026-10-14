using System.Security.Claims;
using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Domain;
using OrderFlow.OrderService.Persistence;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.OrderService.Api;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // Spec 012: every order operation needs a login.
        var orders = app.MapGroup("/orders").RequireAuthorization();
        orders.MapPost("", PlaceOrder).WithName("placeOrder");
        orders.MapGet("/{orderId:guid}", GetOrder).WithName("getOrder");
        orders.MapPost("/{orderId:guid}/cancellation", CancelOrder).WithName("cancelOrder");
        return app;
    }

    /// <summary>
    /// The order, if the logged-in user may see it: their own, or any order for staff (spec 015).
    /// For a customer, someone else's order is treated exactly like one that does not exist, so
    /// order IDs reveal nothing (spec 012, R-2).
    /// </summary>
    public static async Task<Order?> FindAllowedAsync(this IOrderRepository orders, Guid orderId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken);
        return order is not null && (order.CustomerId == user.CustomerId() || user.IsStaff()) ? order : null;
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        ClaimsPrincipal user,
        IOrderRepository orders,
        IPublishEndpoint publisher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines?.Select(l => new OrderLine(l.Sku, l.Quantity)).ToList();
        var result = Order.Place(user.CustomerId(), lines, clock);
        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.Errors);
        }

        var order = result.Order!;
        await orders.AddAsync(order, cancellationToken);

        // With Postgres the bus outbox writes OrderPlaced to the same transaction as the order
        // (spec 007, R-1); SaveChanges commits both, and the outbox delivers it later.
        await publisher.Publish(
            new OrderPlaced(
                order.Id,
                order.CustomerId,
                order.Lines.Select(l => new OrderPlacedLine(l.Sku, l.Quantity)).ToList(),
                order.PlacedAt),
            cancellationToken);
        await orders.SaveChangesAsync(cancellationToken);

        return Results.Created($"/orders/{order.Id}", OrderResponse.From(order));
    }

    /// <summary>
    /// Spec 006. Records the customer's request, or staff's on their behalf (spec 015); the state
    /// machine decides (R-1), so a Pending order gets 202 and the outcome is pushed to watchers (spec 005).
    /// </summary>
    private static async Task<IResult> CancelOrder(
        Guid orderId,
        ClaimsPrincipal user,
        IOrderRepository orders,
        IPublishEndpoint publisher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var order = await orders.FindAllowedAsync(orderId, user, cancellationToken);
        switch (order?.Status)
        {
            case null:
                return Results.NotFound();

            case OrderStatus.Confirmed:
                return Results.Problem(
                    title: "The order is already confirmed.",
                    detail: "A confirmed order cannot be cancelled.",
                    statusCode: StatusCodes.Status409Conflict);

            case OrderStatus.Cancelled:
                return Results.Ok(OrderResponse.From(order));

            default:
                // Postgres: the bus outbox stores the request; SaveChanges commits and delivers it.
                // Staff cancelling their own order do so as its customer (spec 015, R-2).
                var requestedBy = order.CustomerId == user.CustomerId() ? CancellationRequesters.Customer : CancellationRequesters.Staff;
                await publisher.Publish(new OrderCancellationRequested(order.Id, clock.GetUtcNow(), requestedBy), cancellationToken);
                await orders.SaveChangesAsync(cancellationToken);
                return Results.Accepted($"/orders/{order.Id}", OrderResponse.From(order));
        }
    }

    private static async Task<IResult> GetOrder(Guid orderId, ClaimsPrincipal user, IOrderRepository orders, CancellationToken cancellationToken)
    {
        var order = await orders.FindAllowedAsync(orderId, user, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(OrderResponse.From(order));
    }
}
