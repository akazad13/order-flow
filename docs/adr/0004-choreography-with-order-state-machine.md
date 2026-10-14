# 0004. Choreography, with an Order state machine as tracker

- **Status:** Accepted
- **Date:** 2026-10-14
- **Spec:** [001](../../specs/001-place-order/spec.md), question Q2

## Context

Placing an order now spans three services: Inventory holds stock, Payment charges, Order confirms. Either the Order service directs each step with commands (orchestration), or each service reacts to the previous event (choreography).

## Decision

Choreography for the steps, with a MassTransit state machine in the Order service that tracks each order:

- Inventory consumes `OrderPlaced`, publishes `StockReserved`.
- Payment consumes `StockReserved`, publishes `PaymentSucceeded`.
- `OrderStateMachine` consumes all three, holds the order's progress (Pending, AwaitingPayment, Confirmed) and publishes `OrderConfirmed`. It is the only writer of order status.

## Consequences

- Services stay decoupled: Payment does not know the Order service exists.
- The whole flow is visible in one place (the state machine) even though no one directs it.
- Compensation (spec 003) is spread out: Inventory must react to a payment failure itself. If that gets awkward, revisit with an ADR that moves to orchestration.
- The state machine must tolerate events arriving in an unexpected order. This happened in practice during spec 001 (different publishers, so no ordering guarantee); for now an event that arrives too early is retried with back-off (`OrderStateDefinition`), including one that arrives before the order's instance exists (`OnMissingInstance(Fault)`, week 4; the default silently discards). Spec 004 reconsidered modelling each ordering as explicit states and kept the retry: it is proven under 100-run stress tests and far smaller. What spec 004 added instead is explicit `Ignore` rules for repeats, so a duplicate is acknowledged rather than retried and faulted, while an early event is still retried.
