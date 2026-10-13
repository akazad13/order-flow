namespace OrderFlow.Inventory.Domain;

public sealed record CatalogItem(string Sku, string Name, decimal UnitPrice, int Available);

public sealed record RequestedLine(string Sku, int Quantity);

public sealed record ReservedLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary>An unknown SKU is a shortage with <see cref="Available"/> 0 (spec 002, R-2).</summary>
public sealed record Shortage(string Sku, int Requested, int Available);

public abstract record ReservationResult
{
    public sealed record Reserved(IReadOnlyList<ReservedLine> Lines, decimal Total) : ReservationResult;

    public sealed record Rejected(IReadOnlyList<Shortage> Shortages) : ReservationResult;
}

/// <summary>
/// Items, prices, available stock and what each order holds. Thread-safe: one lock
/// makes a reservation all or nothing across every line (spec 001, R-1) and makes a
/// release happen at most once (spec 003, R-2). In memory until week 5.
/// </summary>
public sealed class Catalog
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CatalogItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, IReadOnlyList<ReservedLine>> _reservations = new();

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

    public ReservationResult TryReserve(Guid orderId, IReadOnlyList<RequestedLine> lines)
    {
        var merged = lines
            .GroupBy(l => l.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RequestedLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        lock (_lock)
        {
            var shortages = merged
                .Select(l => new Shortage(l.Sku, l.Quantity, _items.GetValueOrDefault(l.Sku)?.Available ?? 0))
                .Where(s => s.Available < s.Requested)
                .ToList();

            if (shortages.Count > 0)
            {
                return new ReservationResult.Rejected(shortages);
            }

            var reserved = new List<ReservedLine>(merged.Count);
            foreach (var line in merged)
            {
                var item = _items[line.Sku];
                _items[line.Sku] = item with { Available = item.Available - line.Quantity };
                reserved.Add(new ReservedLine(item.Sku, line.Quantity, item.UnitPrice));
            }

            _reservations[orderId] = reserved;
            return new ReservationResult.Reserved(reserved, reserved.Sum(l => l.Quantity * l.UnitPrice));
        }
    }

    /// <summary>
    /// Returns the quantities held for the order to available stock. Returns the released
    /// lines the first time, and nothing if the order held nothing or was already released.
    /// </summary>
    public IReadOnlyList<ReservedLine> Release(Guid orderId)
    {
        lock (_lock)
        {
            if (!_reservations.Remove(orderId, out var held))
            {
                return [];
            }

            foreach (var line in held)
            {
                // The item may have been replaced (PUT /stock) since; release onto the current record.
                if (_items.TryGetValue(line.Sku, out var item))
                {
                    _items[line.Sku] = item with { Available = item.Available + line.Quantity };
                }
            }

            return held;
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
