# Tasks 003: Cancel the order and release stock when payment fails

Plan: [plan.md](plan.md)

- [x] T1 Schemas `payment-failed.v1.json`, `stock-released.v1.json`; AsyncAPI; `payment.openapi.yaml`; lint.
- [x] T2 Gherkin: `decline-payment.feature`, `release-stock.feature`, payment scenarios in `cancel-order.feature` and `order-cancelled-notification.feature`, e2e `payment-failure.feature`. Watch them fail.
- [x] T3 Fake gateway declines per customer; test endpoint; PaymentFailed; scenario green.
- [x] T4 Catalog reservations + `Release`; unit tests (R-1, R-2).
- [x] T5 Inventory `OrderCancelledConsumer` publishes StockReleased; scenario green.
- [x] T6 State machine: PaymentFailed cancels; scenario green.
- [x] T7 Schema tests for the new events.
- [x] T8 docker compose `Payment__EnableTestEndpoints`; E2E green; stress run.
- [x] T9 Specs Done; plan tables match reality.
