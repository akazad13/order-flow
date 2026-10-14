using MassTransit;

namespace OrderFlow.Contracts.Events;

/// <summary>
/// Published by Payment when it refunds the charge of a cancelled order, at most once (spec 006).
/// Contract: contracts/events/asyncapi.yaml (channel "payment-refunded"),
/// schema: contracts/events/schemas/payment-refunded.v1.json.
/// </summary>
[EntityName("payment-refunded")]
[MessageUrn("orderflow:payment-refunded:v1", useDefaultPrefix: false)]
public sealed record PaymentRefunded(
    Guid OrderId,
    string CustomerId,
    Guid PaymentId,
    decimal Amount,
    DateTimeOffset RefundedAt);
