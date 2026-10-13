using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by the Order service when a customer's order has been accepted.
/// Contract: contracts/events/asyncapi.yaml (channel "order-placed"),
/// schema: contracts/events/schemas/order-placed.v1.json.
/// </summary>
[EntityName("order-placed")]
[MessageUrn("orderflow:order-placed:v1", useDefaultPrefix: false)]
public sealed record OrderPlaced(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderPlacedLine> Lines,
    DateTimeOffset PlacedAt);

public sealed record OrderPlacedLine(string Sku, int Quantity);
