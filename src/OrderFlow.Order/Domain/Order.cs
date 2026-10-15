namespace OrderFlow.OrderService.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Cancelled,
}

public sealed record OrderLine(string Sku, int Quantity);

/// <summary>Reason is one of <c>OrderFlow.Contracts.Events.CancellationReasons</c>.</summary>
public sealed record Cancellation(string Reason, string Detail);

public sealed class Order
{
    // For EF Core (ADR 0005).
    private Order()
    {
        CustomerId = null!;
        Lines = null!;
    }

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

    /// <summary>Null unless the order is Cancelled (specs 002, 003).</summary>
    public Cancellation? Cancellation { get; private set; }

    /// <summary>
    /// Until when a confirmed order can still be cancelled: fixed when it is confirmed (spec 022, R-2).
    /// Null for an order that was never confirmed, or was confirmed before spec 022.
    /// </summary>
    public DateTimeOffset? CancellableUntil { get; private set; }

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
    /// Spec 001: confirmed only after the stock hold (R-3); status never moves back (R-4), so
    /// confirming twice is harmless and keeps the first deadline. It can be cancelled until
    /// <paramref name="confirmedAt"/> plus <paramref name="cancelWindow"/> (spec 022).
    /// </summary>
    public void Confirm(DateTimeOffset confirmedAt, TimeSpan cancelWindow)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException($"Order {Id} is cancelled and can never be confirmed.");
        }

        if (Total is null)
        {
            throw new InvalidOperationException($"Order {Id} cannot be confirmed before its stock is held.");
        }

        if (Status == OrderStatus.Confirmed)
        {
            return;
        }

        Status = OrderStatus.Confirmed;
        CancellableUntil = confirmedAt + cancelWindow;
    }

    /// <summary>
    /// Cancelled is final (spec 002, R-3); cancelling again is harmless. A confirmed order is never
    /// cancelled by the stock or payment flows (spec 003, R-3); a request made by
    /// <paramref name="requestedAt"/> may cancel it until its deadline (spec 022, R-1).
    /// </summary>
    public void Cancel(string reason, string detail, DateTimeOffset? requestedAt = null)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status == OrderStatus.Confirmed && !CanBeCancelledAt(requestedAt))
        {
            throw new InvalidOperationException($"Order {Id} is confirmed and can no longer be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        Cancellation = new Cancellation(reason, detail);
    }

    /// <summary>A Pending order always can; a Confirmed one until its deadline (spec 022, R-1); a Cancelled one never.</summary>
    public bool CanBeCancelledAt(DateTimeOffset? at) => Status switch
    {
        OrderStatus.Pending => true,
        OrderStatus.Confirmed => at is { } time && CancellableUntil is { } until && time <= until,
        _ => false,
    };
}

public sealed record PlaceOrderResult(Order? Order, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Order is not null;

    public static PlaceOrderResult Placed(Order order) => new(order, new Dictionary<string, string[]>());

    public static PlaceOrderResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
