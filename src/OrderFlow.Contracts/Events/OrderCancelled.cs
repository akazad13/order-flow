using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Version 1 of "an order was cancelled" (specs 002, 003, 006). No longer published since spec 016:
/// <see cref="OrderCancelledV2"/> replaced it. Readers keep handling it, by <see cref="ToV2"/>, until
/// it is retired (ADR 0010).
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
    DateTimeOffset CancelledAt)
{
    /// <summary>The same cancellation in the current shape (spec 016, R-1): every v1 reason is a v2 reason.</summary>
    public OrderCancelledV2 ToV2() => new(OrderId, CustomerId, Reason, Detail, CancelledAt);
}

/// <summary>
/// Published by the Order service when an order is cancelled. Final (specs 002, 003, 006). Version 2
/// (spec 016) may carry the reason <see cref="CancellationReasons.StaffRequested"/>.
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
