using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by the Order service when an order is cancelled. Final (specs 002, 003, 006). Version 2
/// (spec 016) may carry the reason <see cref="CancellationReasons.StaffRequested"/>. Version 1
/// ("orderflow:order-cancelled:v1", exchange "order-cancelled") was retired in spec 018 (ADR 0010).
/// Contract: contracts/events/asyncapi.yaml (channel "order-cancelled-v2"),
/// schema: contracts/events/schemas/order-cancelled.v2.json.
/// </summary>
[EntityName("order-cancelled-v2")]
[MessageUrn("orderflow:order-cancelled:v2", useDefaultPrefix: false)]
public sealed record OrderCancelledV2(
    Guid OrderId,
    string CustomerId,
    string Reason,
    string Detail,
    DateTimeOffset CancelledAt);

/// <summary>
/// Values of <see cref="OrderCancelledV2.Reason"/>. A new value is a breaking change for readers that
/// decide by the reason, so it needs a new version of the event (constitution IV, specs 014 and 016).
/// </summary>
public static class CancellationReasons
{
    public const string OutOfStock = "OutOfStock";
    public const string PaymentDeclined = "PaymentDeclined";
    public const string CustomerRequested = "CustomerRequested";

    /// <summary>Our support team cancelled the order at the customer's request (spec 016; v2 only).</summary>
    public const string StaffRequested = "StaffRequested";
}
