using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by Inventory when an order cannot be held. Nothing was held (spec 002).
/// Contract: contracts/events/asyncapi.yaml (channel "stock-rejected"),
/// schema: contracts/events/schemas/stock-rejected.v1.json.
/// </summary>
[EntityName("stock-rejected")]
[MessageUrn("orderflow:stock-rejected:v1", useDefaultPrefix: false)]
public sealed record StockRejected(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<StockShortage> Shortages,
    DateTimeOffset RejectedAt);

/// <summary>An unknown SKU has <see cref="Available"/> 0.</summary>
public sealed record StockShortage(string Sku, int Requested, int Available);
