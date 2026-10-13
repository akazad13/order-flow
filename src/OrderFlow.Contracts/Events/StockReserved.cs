using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by Inventory when every line of an order is held. Prices are fixed here.
/// Contract: contracts/events/asyncapi.yaml (channel "stock-reserved"),
/// schema: contracts/events/schemas/stock-reserved.v1.json.
/// </summary>
[EntityName("stock-reserved")]
[MessageUrn("orderflow:stock-reserved:v1", useDefaultPrefix: false)]
public sealed record StockReserved(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<StockReservedLine> Lines,
    decimal Total,
    DateTimeOffset ReservedAt);

public sealed record StockReservedLine(string Sku, int Quantity, decimal UnitPrice);
