using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by Inventory when stock held for a cancelled order is available again. At most once per order (spec 003).
/// Contract: contracts/events/asyncapi.yaml (channel "stock-released"),
/// schema: contracts/events/schemas/stock-released.v1.json.
/// </summary>
[EntityName("stock-released")]
[MessageUrn("orderflow:stock-released:v1", useDefaultPrefix: false)]
public sealed record StockReleased(
    Guid OrderId,
    IReadOnlyList<StockReleasedLine> Lines,
    DateTimeOffset ReleasedAt);

public sealed record StockReleasedLine(string Sku, int Quantity);
