using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.Api;

// Shapes match contracts/http/order.openapi.yaml.
public sealed record OrderLineDto(string Sku, int Quantity);

public sealed record CancellationDto(string Reason, string Detail);

public sealed record PlaceOrderRequest(string? CustomerId, IReadOnlyList<OrderLineDto>? Lines);

public sealed record OrderResponse(
    Guid OrderId,
    string CustomerId,
    string Status,
    IReadOnlyList<OrderLineDto> Lines,
    decimal? Total,
    CancellationDto? Cancellation,
    DateTimeOffset PlacedAt)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status.ToString(),
        order.Lines.Select(l => new OrderLineDto(l.Sku, l.Quantity)).ToList(),
        order.Total,
        order.Cancellation is { } c ? new CancellationDto(c.Reason, c.Detail) : null,
        order.PlacedAt);
}
