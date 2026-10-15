namespace OrderFlow.OrderService;

/// <summary>Settings under <c>Orders</c>.</summary>
public sealed class OrderOptions
{
    public const string Section = "Orders";

    /// <summary>How long after it is confirmed an order can still be cancelled (spec 022, R-1). Default 24 hours.</summary>
    public TimeSpan CancelWindow { get; set; } = TimeSpan.FromHours(24);
}
