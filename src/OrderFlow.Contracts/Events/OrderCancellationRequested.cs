using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by the Order API when a customer asks to cancel a Pending order. The Order
/// service's state machine decides (spec 006, R-1).
/// Contract: contracts/events/asyncapi.yaml (channel "order-cancellation-requested"),
/// schema: contracts/events/schemas/order-cancellation-requested.v1.json.
/// </summary>
[EntityName("order-cancellation-requested")]
[MessageUrn("orderflow:order-cancellation-requested:v1", useDefaultPrefix: false)]
public sealed record OrderCancellationRequested(
    Guid OrderId,
    DateTimeOffset RequestedAt);
