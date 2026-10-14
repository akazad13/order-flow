using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by the Order API when a customer, or staff on their behalf (spec 015), asks to
/// cancel a Pending order. The Order service's state machine decides (spec 006, R-1).
/// <see cref="RequestedBy"/> is one of <see cref="CancellationRequesters"/>; a request without
/// it (published before spec 015) is the customer's.
/// Contract: contracts/events/asyncapi.yaml (channel "order-cancellation-requested"),
/// schema: contracts/events/schemas/order-cancellation-requested.v1.json.
/// </summary>
[EntityName("order-cancellation-requested")]
[MessageUrn("orderflow:order-cancellation-requested:v1", useDefaultPrefix: false)]
public sealed record OrderCancellationRequested(
    Guid OrderId,
    DateTimeOffset RequestedAt,
    string? RequestedBy = null);

/// <summary>Who asked to cancel (spec 015). Matches the enum in the schema.</summary>
public static class CancellationRequesters
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
}
