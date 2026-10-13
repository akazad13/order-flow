using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by the Order service when stock is held and payment succeeded.
/// Contract: contracts/events/asyncapi.yaml (channel "order-confirmed"),
/// schema: contracts/events/schemas/order-confirmed.v1.json.
/// </summary>
[EntityName("order-confirmed")]
[MessageUrn("orderflow:order-confirmed:v1", useDefaultPrefix: false)]
public sealed record OrderConfirmed(
    Guid OrderId,
    string CustomerId,
    decimal Total,
    DateTimeOffset ConfirmedAt);
