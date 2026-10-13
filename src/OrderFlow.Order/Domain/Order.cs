namespace OrderFlow.OrderService.Domain;

public enum OrderStatus
{
    Pending,
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

    /// <summary>
    /// Spec 000, AC-1 and AC-2: a valid order starts Pending; an invalid one is
    /// rejected with one error per offending field.
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

        return PlaceOrderResult.Placed(new Order(Guid.NewGuid(), customerId!, lines!, clock.GetUtcNow()));
    }
}

public sealed record PlaceOrderResult(Order? Order, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Order is not null;

    public static PlaceOrderResult Placed(Order order) => new(order, new Dictionary<string, string[]>());

    public static PlaceOrderResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
