# Plan 002: Reject an order when stock is short

Spec: [spec.md](spec.md)

## Approach

Inventory stops logging and starts announcing: on a shortage it publishes `StockRejected` listing every short line. The Order state machine cancels the order (`Order.Cancel`) and publishes `OrderCancelled` with reason `OutOfStock`. Notification turns that into an inbox message. Payment never hears of the order. `OrderCancelled` is shared with spec 003.

```text
Order --OrderPlaced--> Inventory --StockRejected--> Order (state machine) --OrderCancelled--> Notification
                                                                                         \--> Inventory (nothing held: no-op)
```

## Services affected

| Service | Change |
| --- | --- |
| Inventory | `Catalog.TryReserve` reports every short line (requested, available); consumer publishes StockRejected |
| Order | `OrderStatus.Cancelled`, `Order.Cancel(reason, detail)`; state machine handles StockRejected (also as initiating event), state `Cancelled`; API returns `cancellation` |
| Notification | `OrderCancelledConsumer`, text by reason |
| Web | Shows Cancelled and the reason; stops polling when not Pending (already) |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `schemas/stock-rejected.v1.json` | orderId, customerId, shortages (sku, requested, available), rejectedAt | New |
| `schemas/order-cancelled.v1.json` | orderId, customerId, reason (`OutOfStock` or `PaymentDeclined`), detail, cancelledAt | New |
| `asyncapi.yaml` | Channels `stock-rejected`, `order-cancelled` | Additive |
| `order.openapi.yaml` | `OrderStatus` adds `Cancelled`; `Order.cancellation` (reason, detail) or null | Additive |

Reasons are strings in the contract (not a C# enum on the wire), with constants in `OrderFlow.Contracts`.

## Test plan (criterion to layer)

| Criterion | Unit | Component | Contract | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Nothing held, shortage announced | x | x | x | | `CatalogTests`; `reject-stock.feature`; StockRejected schema |
| AC-2 Cancelled, reason OutOfStock | x | x | x | | `OrderTests`; `cancel-order.feature`; OrderCancelled schema |
| AC-3 No payment | | x | | | `decline-payment.feature` "a shortage is never charged" (added week 8, spec 009); by construction Payment only consumes StockReserved (spec 001 R-2) |
| AC-4 Customer told | | x | | x | `order-cancelled-notification.feature`; E2E inbox |
| AC-5 Customer sees it | | | | x | `features/e2e/stock-rejection.feature` |
| R-1, R-2 | x | x | | | `CatalogTests` (all short lines listed, unknown = 0) |
| R-3 Cancelled is final | x | | | | `OrderTests` |
| Edge: shortage overtakes OrderPlaced | | x | | | `cancel-order.feature` |

## Constitution check

- [x] Spec first; [x] contracts in the same change; [x] every AC automated (AC-3 by construction, noted); [x] events versioned; [x] no sleeps; [x] no banned packages

## Risks and decisions

- `OrderCancelled` reaches Inventory for shortage cancellations too. Inventory must treat "nothing held" as a no-op (spec 003 R-1).
