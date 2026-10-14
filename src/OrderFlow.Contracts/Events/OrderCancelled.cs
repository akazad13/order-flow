using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by the Order service when an order is cancelled. Final (specs 002, 003, 006).
/// Contract: contracts/events/asyncapi.yaml (channel "order-cancelled"),
/// schema: contracts/events/schemas/order-cancelled.v1.json.
/// </summary>
[EntityName("order-cancelled")]
[MessageUrn("orderflow:order-cancelled:v1", useDefaultPrefix: false)]
public sealed record OrderCancelled(
    Guid OrderId,
    string CustomerId,
    string Reason,
    string Detail,
    DateTimeOffset CancelledAt);

/// <summary>Values of <see cref="OrderCancelled.Reason"/>. Strings on the wire, so adding one is not breaking.</summary>
public static class CancellationReasons
{
    public const string OutOfStock = "OutOfStock";
    public const string PaymentDeclined = "PaymentDeclined";
    public const string CustomerRequested = "CustomerRequested";
}
