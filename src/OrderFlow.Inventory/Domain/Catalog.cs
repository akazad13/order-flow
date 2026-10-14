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

    /// <summary>A repeat: stock for this order was already decided (spec 004). Nothing changed.</summary>
    public sealed record AlreadyDecided : ReservationResult;
}

/// <summary>The first stock decision for an order, remembered so repeats change nothing (spec 004, R-2).</summary>
public enum StockDecision
{
    Held,
    Rejected,
    Released,
}

/// <summary>
/// Items, prices, available stock and what each order holds. Thread-safe: one lock
/// makes a reservation all or nothing across every line (spec 001, R-1) and makes a
/// release happen at most once (spec 003, R-2). The in-memory storage mode (ADR 0005).
/// </summary>
public sealed class Catalog
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CatalogItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, IReadOnlyList<ReservedLine>> _reservations = new();
    private readonly Dictionary<Guid, StockDecision> _decisions = new();

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
        var merged = StockRules.Merge(lines);

        lock (_lock)
        {
            if (_decisions.ContainsKey(orderId))
            {
                return new ReservationResult.AlreadyDecided();
            }

            var result = StockRules.Check(merged, sku => _items.GetValueOrDefault(sku));
            _decisions[orderId] = result is ReservationResult.Reserved ? StockDecision.Held : StockDecision.Rejected;
            if (result is ReservationResult.Reserved reserved)
            {
                foreach (var line in reserved.Lines)
                {
                    var item = _items[line.Sku];
                    _items[line.Sku] = item with { Available = item.Available - line.Quantity };
                }

                _reservations[orderId] = reserved.Lines;
            }

            return result;
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
            if (_decisions.GetValueOrDefault(orderId) != StockDecision.Held || !_reservations.Remove(orderId, out var held))
            {
                return [];
            }

            _decisions[orderId] = StockDecision.Released;

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
