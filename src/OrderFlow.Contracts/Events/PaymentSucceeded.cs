using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by Payment when the order total was charged.
/// Contract: contracts/events/asyncapi.yaml (channel "payment-succeeded"),
/// schema: contracts/events/schemas/payment-succeeded.v1.json.
/// </summary>
[EntityName("payment-succeeded")]
[MessageUrn("orderflow:payment-succeeded:v1", useDefaultPrefix: false)]
public sealed record PaymentSucceeded(
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    DateTimeOffset PaidAt);
