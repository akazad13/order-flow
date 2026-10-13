# Tasks 002: Reject an order when stock is short

Plan: [plan.md](plan.md)

- [x] T1 Schemas `stock-rejected.v1.json`, `order-cancelled.v1.json`; AsyncAPI channels; Order API `Cancelled` + `cancellation`; lint.
- [x] T2 Gherkin: `reject-stock.feature`, `cancel-order.feature` (shortage scenarios), `order-cancelled-notification.feature`, e2e `stock-rejection.feature`. Watch them fail.
- [x] T3 Catalog lists every shortage; unit tests.
- [x] T4 Inventory publishes StockRejected; `reject-stock.feature` green.
- [x] T5 `Order.Cancel`; unit tests (R-3).
- [x] T6 State machine: StockRejected cancels (initially and while AwaitingStock); `cancel-order.feature` green.
- [x] T7 Notification `OrderCancelledConsumer`; scenario green.
- [x] T8 Schema tests for the new events.
- [x] T9 UI shows Cancelled + reason; Vitest.
- [x] T10 E2E green; stress run. (100 runs across all 4 E2E scenarios: found a spec 001 race, see plan 003; then 100 of 100.)
