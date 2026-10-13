using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Domain;
using OrderFlow.OrderService.Persistence;

namespace OrderFlow.OrderService.Api;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", PlaceOrder).WithName("placeOrder");
        app.MapGet("/orders/{orderId:guid}", GetOrder).WithName("getOrder");
        return app;
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        IOrderRepository orders,
        IPublishEndpoint publisher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines?.Select(l => new OrderLine(l.Sku, l.Quantity)).ToList();
        var result = Order.Place(request.CustomerId, lines, clock);
        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.Errors);
        }

        var order = result.Order!;
        await orders.AddAsync(order, cancellationToken);

        // Not atomic with the save: the outbox (week 5) closes that gap.
        await publisher.Publish(
            new OrderPlaced(
                order.Id,
                order.CustomerId,
                order.Lines.Select(l => new OrderPlacedLine(l.Sku, l.Quantity)).ToList(),
                order.PlacedAt),
            cancellationToken);

        return Results.Created($"/orders/{order.Id}", OrderResponse.From(order));
    }

    private static async Task<IResult> GetOrder(Guid orderId, IOrderRepository orders, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(OrderResponse.From(order));
    }
}
