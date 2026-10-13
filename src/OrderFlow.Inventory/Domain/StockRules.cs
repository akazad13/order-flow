namespace OrderFlow.Inventory.Domain;

/// <summary>
/// The reservation rules, shared by the in-memory catalog and the database store so both
/// storage modes behave identically (ADR 0005).
/// </summary>
public static class StockRules
{
    /// <summary>Lines for the same SKU count as one line (spec 001 edge case).</summary>
    public static IReadOnlyList<RequestedLine> Merge(IEnumerable<RequestedLine> lines) => lines
        .GroupBy(l => l.Sku, StringComparer.OrdinalIgnoreCase)
        .Select(g => new RequestedLine(g.Key, g.Sum(l => l.Quantity)))
        .ToList();

    /// <summary>
    /// All or nothing (spec 001, R-1): every line short of stock is reported, and an
    /// unknown SKU is a shortage with 0 available (spec 002, R-2). Does not change stock.
    /// </summary>
    public static ReservationResult Check(IReadOnlyList<RequestedLine> merged, Func<string, CatalogItem?> find)
    {
        var shortages = merged
            .Select(l => new Shortage(l.Sku, l.Quantity, find(l.Sku)?.Available ?? 0))
            .Where(s => s.Available < s.Requested)
            .ToList();

        if (shortages.Count > 0)
        {
            return new ReservationResult.Rejected(shortages);
        }

        var reserved = merged.Select(l => new ReservedLine(find(l.Sku)!.Sku, l.Quantity, find(l.Sku)!.UnitPrice)).ToList();
        return new ReservationResult.Reserved(reserved, reserved.Sum(l => l.Quantity * l.UnitPrice));
    }
}
