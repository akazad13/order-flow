# Tasks 006: Customer cancels a pending order

Plan: [plan.md](plan.md)

- [x] T1 Contracts: OpenAPI, event schemas, AsyncAPI, payment test endpoint; contract tests.
- [x] T2 Order: scenarios (red); endpoint, event, state machine; green.
- [x] T3 Inventory: unit and scenario (red); release of an undecided order; green.
- [x] T4 Payment: scenarios (red); settle (void or refund), stall switch; green.
- [x] T5 Notification: scenarios (red); texts and refund consumer; green.
- [x] T6 Integration: the race on PostgreSQL (Order); cancel before placement (Inventory).
- [x] T7 Web: tests (red), client, button; Pact interactions; provider verification green.
- [x] T8 E2E: `customer-cancel.feature`; green on compose; stress 100 of 100 (5 scenarios x 20), no error queues.
- [x] T9 Compose comment, plan notes; spec Done.
