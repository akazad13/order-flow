namespace OrderFlow.OrderService.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
}

public sealed record OrderLine(string Sku, int Quantity);

public sealed class Order
{
    private Order(Guid id, string customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset placedAt)
    {
        Id = id;
        CustomerId = customerId;
        Lines = lines;
        PlacedAt = placedAt;
        Status = OrderStatus.Pending;
    }

    public Guid Id { get; }
    public string CustomerId { get; }
    public IReadOnlyList<OrderLine> Lines { get; }
    public DateTimeOffset PlacedAt { get; }
    public OrderStatus Status { get; private set; }

    /// <summary>Null until stock is held and Inventory has priced the order (spec 001, AC-6).</summary>
    public decimal? Total { get; private set; }

    /// <summary>
    /// Spec 000, AC-1 and AC-2: a valid order starts Pending; an invalid one is
    /// rejected with one error per offending field. Spec 001: lines for the same
    /// SKU are merged.
    /// </summary>
    public static PlaceOrderResult Place(string? customerId, IReadOnlyList<OrderLine>? lines, TimeProvider clock)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(customerId))
        {
            errors["customerId"] = ["Customer ID is required."];
        }

        if (lines is null || lines.Count == 0)
        {
            errors["lines"] = ["An order needs at least one line."];
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i].Sku))
                {
                    errors[$"lines[{i}].sku"] = ["SKU is required."];
                }

                if (lines[i].Quantity < 1)
                {
                    errors[$"lines[{i}].quantity"] = ["Quantity must be at least 1."];
                }
            }
        }

        if (errors.Count > 0)
        {
            return PlaceOrderResult.Invalid(errors);
        }

        var merged = lines!
            .GroupBy(l => l.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OrderLine(g.First().Sku, g.Sum(l => l.Quantity)))
            .ToList();

        return PlaceOrderResult.Placed(new Order(Guid.NewGuid(), customerId!, merged, clock.GetUtcNow()));
    }

    /// <summary>Stock for every line is held; the order now has a price.</summary>
    public void RecordStockHeld(decimal total)
    {
        if (Status == OrderStatus.Pending)
        {
            Total = total;
        }
    }

    /// <summary>
    /// Spec 001: confirmed only after the stock hold (R-3); status never moves
    /// back (R-4), so confirming twice is harmless.
    /// </summary>
    public void Confirm()
    {
        if (Total is null)
        {
            throw new InvalidOperationException($"Order {Id} cannot be confirmed before its stock is held.");
        }

        Status = OrderStatus.Confirmed;
    }
}

public sealed record PlaceOrderResult(Order? Order, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Order is not null;

    public static PlaceOrderResult Placed(Order order) => new(order, new Dictionary<string, string[]>());

    public static PlaceOrderResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
