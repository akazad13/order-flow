# Tasks 001: Place an order and see it confirmed

Plan: [plan.md](plan.md)

Each task ends with a test going green. Write the failing scenario or test first.

## 1. Decisions and contracts

- [x] T1 ADR 0004: choreography with an Order state machine as tracker.
- [x] T2 Event schemas: `stock-reserved.v1.json`, `payment-succeeded.v1.json`, `order-confirmed.v1.json`; add channels and operations to `asyncapi.yaml`.
- [x] T3 `order.openapi.yaml`: `Confirmed` status, `total`. New `inventory.openapi.yaml`.
- [x] T4 `npm run lint:contracts` clean.
- [x] T5 Gherkin: `hold-stock`, `take-payment`, `confirm-order`, `order-confirmed-notification` (component); `place-order` (e2e). Run them and watch them fail.

## 2. Inventory (AC-1, AC-6, R-1)

- [x] T6 `OrderFlow.Inventory.UnitTests`: catalog holds stock all or nothing, prices lines, totals, survives parallel reservations.
- [x] T7 Catalog + seed + stock endpoints (`PUT` behind `Inventory:EnableTestEndpoints`).
- [x] T8 `OrderPlacedConsumer` publishes StockReserved; `hold-stock.feature` green.

## 3. Payment (AC-2, R-2)

- [x] T9 Fake gateway + `StockReservedConsumer` publishes PaymentSucceeded; `take-payment.feature` green.

## 4. Order (AC-3, R-3, R-4)

- [x] T10 `Order`: `Total`, `Confirm()`, merge duplicate SKUs; unit tests.
- [x] T11 `OrderStateMachine` + in-memory saga repository; publishes OrderConfirmed; `confirm-order.feature` green.
- [x] T12 `GET /orders/{id}` returns status and total.

## 5. Notification (AC-4)

- [x] T13 `OrderConfirmedConsumer`; `order-confirmed-notification.feature` green.

## 6. Contract tests

- [x] T14 One parameterized schema test per event (OrderPlaced, StockReserved, PaymentSucceeded, OrderConfirmed). (Found: MassTransit sends decimals as strings; schemas changed to a decimal-string pattern.)

## 7. UI and E2E (AC-5, AC-6)

- [x] T15 `npm run gen:api`; UI polls the order while Pending and shows the total; Vitest test.
- [x] T16 docker compose: `Inventory__EnableTestEndpoints=true`.
- [x] T17 E2E steps for `place-order.feature` (sets its own stock first); green.

## 8. Verify

- [x] T18 All layers green: `dotnet test`, web tests, contract lint, E2E.
- [x] T19 E2E 20 runs without a failure. (Ran 100: found two ordering races, fixed both, then 100 of 100 green.)
- [x] T20 Spec status Done; plan test table matches reality.
