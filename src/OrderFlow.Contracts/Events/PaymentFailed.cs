using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by Payment when a payment is declined. No charge was made (spec 003).
/// Contract: contracts/events/asyncapi.yaml (channel "payment-failed"),
/// schema: contracts/events/schemas/payment-failed.v1.json.
/// </summary>
[EntityName("payment-failed")]
[MessageUrn("orderflow:payment-failed:v1", useDefaultPrefix: false)]
public sealed record PaymentFailed(
    Guid OrderId,
    string Reason,
    DateTimeOffset FailedAt);
