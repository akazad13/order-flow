# Plan 001: Place an order and see it confirmed

Spec: [spec.md](spec.md)

## Approach

Each service reacts to the previous service's event (choreography). Inventory holds stock on OrderPlaced and prices the order; Payment charges on StockReserved; the Order service runs a MassTransit state machine that tracks each order and confirms it on PaymentSucceeded. The UI polls the order until it is no longer Pending.

```text
UI --POST /orders--> Order --OrderPlaced--> Inventory --StockReserved--> Payment --PaymentSucceeded--> Order --OrderConfirmed--> Notification
                       ^ state machine tracks: Pending -> AwaitingPayment -> Confirmed (customer sees Pending / Confirmed)
```

## Services affected

| Service | Change |
| --- | --- |
| Order | `OrderStateMachine` (in-memory saga repository): starts on OrderPlaced, records total on StockReserved, confirms on PaymentSucceeded and publishes OrderConfirmed. `Order` gains `Total` and `Confirm()`. Duplicate SKUs are merged when placing. |
| Inventory | Seeded catalog (SKU, name, unit price, available). Consumes OrderPlaced, holds stock all or nothing, publishes StockReserved with line prices and total. `GET /stock`, `GET /stock/{sku}`; `PUT /stock/{sku}` only when `Inventory:EnableTestEndpoints` is true. |
| Payment | Fake gateway (always succeeds in this spec). Consumes StockReserved, charges the total once, publishes PaymentSucceeded. |
| Notification | Consumes OrderConfirmed, delivers "Your order is confirmed". |
| Web | Polls `GET /orders/{id}` every second while Pending; shows status and total. |

Shortage or unknown SKU: Inventory logs a warning and publishes nothing, so the order stays Pending. Spec 002 replaces this with StockRejected.

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `contracts/http/order.openapi.yaml` | `OrderStatus` adds `Confirmed`; `Order.total` (number or null until stock is held) | No (additive) |
| `contracts/http/inventory.openapi.yaml` | New: `listStock`, `getStock`, `setStock` (test only) | New |
| `contracts/events/asyncapi.yaml` | New channels `stock-reserved`, `payment-succeeded`, `order-confirmed` | No (additive) |
| `contracts/events/schemas/stock-reserved.v1.json` | orderId, customerId, lines (sku, quantity, unitPrice), total, reservedAt | New |
| `contracts/events/schemas/payment-succeeded.v1.json` | orderId, paymentId, amount, paidAt | New |
| `contracts/events/schemas/order-confirmed.v1.json` | orderId, customerId, total, confirmedAt | New |

Amounts in **events** are decimal strings (`"62.00"`); amounts in the **HTTP APIs** are JSON numbers. See Risks and decisions.

## Data model changes

In memory only (durable storage is week 5).

- Order: `Total` (decimal?, set when stock is held), status `Confirmed`.
- Order saga instance: `CorrelationId` (= OrderId), `CurrentState`, `Total`.
- Inventory: `CatalogItem` (Sku, Name, UnitPrice, Available); `Reservation` (OrderId, lines).
- Payment: `Charge` (PaymentId, OrderId, Amount, ChargedAt) in the fake gateway.

Seed catalog:

| SKU | Name | Unit price | Available |
| --- | --- | --- | --- |
| SKU-001 | Coffee beans, 1 kg | 18.50 | 1000 |
| SKU-002 | Pour-over kettle | 45.00 | 200 |
| SKU-003 | Paper filters, 100 | 6.25 | 5000 |

## Test plan (criterion to layer)

| Criterion | Unit | Component | Contract | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Stock is held | x | x | x | | `CatalogTests`; `features/component/hold-stock.feature`; StockReserved schema |
| AC-2 Payment follows the hold | | x | x | | `features/component/take-payment.feature`; PaymentSucceeded schema |
| AC-3 Order is confirmed | x | x | x | | `OrderTests`; `features/component/confirm-order.feature`; OrderConfirmed schema |
| AC-4 Customer is told | | x | | x | `features/component/order-confirmed-notification.feature`; E2E |
| AC-5 Customer sees it | | | | x | `features/e2e/place-order.feature` |
| AC-6 Order total | x | x | | x | `CatalogTests`; hold-stock scenario; E2E shows total |
| R-1 All or nothing | x | | | | `CatalogTests` |
| R-2 No payment without hold | | x | | | take-payment: Payment only consumes StockReserved |
| R-3 Hold, then pay, then confirm | | x | | | confirm-order: StockReserved alone leaves the order Pending |
| R-4 Status only moves forward | x | | | | `OrderTests` |
| Edge: duplicate SKU lines | x | | | | `OrderTests`, `CatalogTests` |
| Edge: concurrent orders | x | | | | `CatalogTests` (parallel reservations) |
| Edge: payment news overtakes the stock hold | | x | | x | confirm-order: "Payment news that overtakes the stock hold"; E2E stress run |
| Edge: payment news before the order is tracked (week 4) | | x | | x | confirm-order: "arrives before the order is being tracked"; E2E stress run |
| Edge: messages arrive out of order at Notification | | x | | x | order-confirmed-notification: "listed in the order things happened"; E2E inbox order |

New test projects: `OrderFlow.Inventory.UnitTests`, `OrderFlow.Inventory.ComponentTests`, `OrderFlow.Payment.ComponentTests`. Contract tests become one parameterized test per event schema. The UI test covers polling until Confirmed.

## Constitution check

- [x] Spec first (I): spec 001 Ready, questions resolved
- [x] Contracts updated in the same PR (II)
- [x] Every AC automated (III): see table
- [ ] Events versioned and consumers idempotent (IV): versioned yes; idempotency is spec 004
- [x] No sleeps, tests own their data (V): E2E sets its own stock through `PUT /stock/{sku}` on a per-run SKU
- [x] No banned packages (VI)

## Risks and decisions

- **Choreography with a tracking state machine** (Q2). Recorded in ADR 0004.
- **Message ordering (found during build, fixed).** RabbitMQ only orders messages per publisher, and every step here has a different publisher. The E2E stability run exposed two real races:
    - *PaymentSucceeded before StockReserved* at the Order state machine (1 in 40). A partitioner by OrderId was tried first and made it worse (7 in 100): it preserves arrival order, and arrival order was the problem. Fix: retry events the state machine cannot handle yet (`OrderStateDefinition`, 50 ms to 2 s back-off). The state machine also accepts StockReserved as an initiating event and ignores a late OrderPlaced.
    - *OrderConfirmed before OrderPlaced* at Notification (separate queues). Fix: the inbox is ordered by when the event happened (`occurredAt`, stamped by the Order service), not arrival. Contract `notification.openapi.yaml` 0.2.0 adds `occurredAt`.
    - Result: 100 of 100 E2E runs green, no messages in error queues. The harness cannot reproduce broker-level races; week 5 adds a Testcontainers test against real RabbitMQ. Spec 004 replaces the retry with explicit handling of every ordering.
- **Two sources of truth in Order** (saga state and `Order`). The state machine is the only writer of status; the API reads `Order`. Revisit with the outbox in week 5.
- **Money on the wire is a string** (found during build). The contract test showed MassTransit serializes `decimal` as `"62.00"`, not `62.00`, by design, to avoid float rounding. Decision: change the event schemas to a string pattern (up to 2 decimals) instead of disabling the converter. HTTP stays numeric (ASP.NET default).
- **Test endpoint in a real service.** `PUT /stock/{sku}` is off unless `Inventory:EnableTestEndpoints` is true (on in Development and docker compose, off by default). (Superseded by [spec 017](../017-admin-manages-stock/spec.md): the endpoint is real and admin-only; the switch is gone.)
