# Plan 003: Cancel the order and release stock when payment fails

Spec: [spec.md](spec.md)

## Approach

The fake gateway gains a per-customer decline rule (test endpoint). On a decline Payment publishes `PaymentFailed`. The Order state machine cancels the order with reason `PaymentDeclined` and publishes `OrderCancelled` (from spec 002). Inventory now records what each order holds; on `OrderCancelled` it returns those quantities and publishes `StockReleased`, once.

```text
Inventory --StockReserved--> Payment --PaymentFailed--> Order --OrderCancelled--> Inventory --StockReleased-->
                                                                              \--> Notification
```

## Services affected

| Service | Change |
| --- | --- |
| Payment | `ChargeResult` (Succeeded or Declined); decline rules per customer; `PUT /test/declines/{customerId}` behind `Payment:EnableTestEndpoints`; publishes PaymentFailed |
| Order | State machine: PaymentFailed while AwaitingPayment cancels with `PaymentDeclined` |
| Inventory | Reservations by OrderId; `Catalog.Release(orderId)` returns held lines once; `OrderCancelledConsumer` publishes StockReleased |
| Notification | Text for `PaymentDeclined` (consumer from spec 002) |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `schemas/payment-failed.v1.json` | orderId, reason, failedAt | New |
| `schemas/stock-released.v1.json` | orderId, lines (sku, quantity), releasedAt | New |
| `asyncapi.yaml` | Channels `payment-failed`, `stock-released`; Inventory receives `order-cancelled` | Additive |
| `contracts/http/payment.openapi.yaml` | New: `setDecline` (test only) | New |

## Test plan (criterion to layer)

| Criterion | Unit | Component | Contract | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Decline announced, no charge | | x | x | | `decline-payment.feature`; PaymentFailed schema |
| AC-2 Cancelled, PaymentDeclined | | x | | | `cancel-order.feature` |
| AC-3 Stock goes back | x | x | x | x | `CatalogTests`; `release-stock.feature`; StockReleased schema; E2E stock check |
| AC-4 Customer told | | x | | x | `order-cancelled-notification.feature`; E2E inbox |
| AC-5 Customer sees it | | | | x | `features/e2e/payment-failure.feature` |
| R-1 Release only what was held | x | x | | | `CatalogTests`; release-stock "nothing held" |
| R-2 Release at most once | x | x | | | `CatalogTests`; release-stock "cancelled twice" |
| R-3 Confirmed never cancelled | x | | | | `OrderTests` |
| Edge: PaymentFailed overtakes StockReserved | | x | | | `cancel-order.feature` (relies on spec 001 retry) |

## Constitution check

- [x] Spec first; [x] contracts in the same change; [x] every AC automated; [x] events versioned; [ ] idempotent consumers: release is idempotent by design, the rest is spec 004; [x] no sleeps; [x] no banned packages

## Risks and decisions

- Reservations live in memory with the catalog; a restart forgets them (durable storage is week 5).
- The decline rule is test-only state inside a real service, behind a config flag like Inventory's.
- **Found during the week 4 stress run (2 in 100, the spec 001 happy path):** PaymentSucceeded was processed before the state machine had created the order's instance (its OrderPlaced was still in flight). MassTransit's default for a non-initiating event with no instance is a silent discard, so the message was acknowledged with no error and the order stayed Pending forever. The broker's counters showed it: every message acked, 50 OrderConfirmed for 52 successful payments. Fix: `OnMissingInstance(Fault)` for PaymentSucceeded and PaymentFailed, and the retry in `OrderStateDefinition` also handles `SagaException`. Reproduced first by the confirm-order scenario "arrives before the order is being tracked" (red, then green). Then 100 of 100 E2E green.
