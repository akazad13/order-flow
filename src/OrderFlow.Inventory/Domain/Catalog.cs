namespace OrderFlow.Inventory.Domain;

public sealed record CatalogItem(string Sku, string Name, decimal UnitPrice, int Available);

public sealed record RequestedLine(string Sku, int Quantity);

public sealed record ReservedLine(string Sku, int Quantity, decimal UnitPrice);

public abstract record ReservationResult
{
    public sealed record Reserved(IReadOnlyList<ReservedLine> Lines, decimal Total) : ReservationResult;

    public sealed record Rejected(string Reason) : ReservationResult;
}

/// <summary>
/// Items, prices and available stock. Thread-safe: one lock makes a reservation
/// all or nothing across every line (spec 001, R-1) and prevents overselling.
/// In memory until durable storage arrives in week 5.
/// </summary>
public sealed class Catalog
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CatalogItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public Catalog(IEnumerable<CatalogItem>? seed = null)
    {
        foreach (var item in seed ?? [])
        {
            _items[item.Sku] = item;
        }
    }

    public IReadOnlyList<CatalogItem> List()
    {
        lock (_lock)
        {
            return _items.Values.OrderBy(i => i.Sku, StringComparer.Ordinal).ToList();
        }
    }

    public CatalogItem? Get(string sku)
    {
        lock (_lock)
        {
            return _items.GetValueOrDefault(sku);
        }
    }

    public CatalogItem Set(string sku, string name, decimal unitPrice, int available)
    {
        var item = new CatalogItem(sku, name, unitPrice, available);
        lock (_lock)
        {
            _items[sku] = item;
        }

        return item;
    }

    public ReservationResult TryReserve(IReadOnlyList<RequestedLine> lines)
    {
        var merged = lines
            .GroupBy(l => l.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RequestedLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        lock (_lock)
        {
            foreach (var line in merged)
            {
                if (!_items.TryGetValue(line.Sku, out var item))
                {
                    return new ReservationResult.Rejected($"Unknown SKU {line.Sku}.");
                }

                if (item.Available < line.Quantity)
                {
                    return new ReservationResult.Rejected($"Only {item.Available} of {line.Sku} available, {line.Quantity} requested.");
                }
            }

            var reserved = new List<ReservedLine>(merged.Count);
            foreach (var line in merged)
            {
                var item = _items[line.Sku];
                _items[line.Sku] = item with { Available = item.Available - line.Quantity };
                reserved.Add(new ReservedLine(item.Sku, line.Quantity, item.UnitPrice));
            }

            return new ReservationResult.Reserved(reserved, reserved.Sum(l => l.Quantity * l.UnitPrice));
        }
    }
}

public static class CatalogSeed
{
    public static IReadOnlyList<CatalogItem> Items { get; } =
    [
        new("SKU-001", "Coffee beans, 1 kg", 18.50m, 1000),
        new("SKU-002", "Pour-over kettle", 45.00m, 200),
        new("SKU-003", "Paper filters, 100", 6.25m, 5000),
    ];
}
