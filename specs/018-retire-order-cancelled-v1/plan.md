# Plan 018: Retire OrderCancelled v1

Spec: [spec.md](spec.md)

## Approach

Step 3 of ADR 0010, for `OrderCancelled`:

1. **Check the precondition** on the running stack: every Order copy has published v2 only since the spec 016 rebuild, and `rabbitmqctl list_queues` shows the three readers' queues empty, each with both copies consuming.
2. **Remove v1**: the record and its upcast (`OrderCancelled`, `ToV2()`), each reader's v1 interface, the contract's v1 channel, message, schema file and reader operations (events contract 3.0.0), the v1 scenarios and steps from spec 016, and the v1 contract-test rows; tests that published v1 publish v2.
3. **Keep the routing** (R-1): nothing deletes the `order-cancelled` exchange or its bindings on existing brokers, so a stray v1 reaches the readers' queues, where MassTransit, finding no consumer for it, moves it to the skipped queue that the error-queue check reports.

## Services affected

| Area | Change |
| --- | --- |
| Contracts (.NET) | `OrderCancelled` (v1) and `ToV2()` removed |
| Inventory, Payment, Notification | Read `OrderCancelledV2` only |
| Order | Comment only |
| Tests | v1 scenarios and steps removed; v2 in integration tests; `InfrastructureFixture.CreateBus` and `QueueDepthAsync`; the stray-v1 test |

## Contract changes

| Contract | Change | Version |
| --- | --- | --- |
| `events/asyncapi.yaml` | Channel `orderCancelled`, message `OrderCancelled`, schema `order-cancelled.v1.json` and the three v1 reader operations removed | 2.0.0 to 3.0.0 (the gate lists the four removals as allowed by the new major version) |

## Test plan (criterion to layer)

| Criterion | Unit | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Gone from the contract | x | | | `check-contracts.test.mjs` (removals need a new major version); `npm run check:contracts`; `EventSchemaContractTests` (every schema file has a test, so no orphan) |
| AC-2 Gone from the code | | x | | The build (no `OrderCancelled` type); `InventoryServiceTests.A_stray_v1_cancellation_is_set_aside_not_lost` (Inventory no longer consumes v1) |
| AC-3 Nothing else changes | | x | x | `release-stock.feature`, `settle-payment.feature`, `order-cancelled-notification.feature`, `staff-orders.feature`; integration tests; all E2E scenarios with `docker-compose.copies.yml`, 100 runs |
| AC-4 A stray v1 is not lost silently | | x | | `InventoryServiceTests.A_stray_v1_cancellation_is_set_aside_not_lost`; `check-error-queues.test.mjs` (a skipped queue is reported) |
| R-1 Retiring removes the reading, not the routing | | x | | `InventoryServiceTests.A_stray_v1_cancellation_is_set_aside_not_lost` |

## Constitution check

- [x] Spec first; [x] contract change as a new major version (spec 014); [x] ADR 0010 followed, condition checked; [x] every AC automated (the precondition is a recorded check of the running system); [x] no message lost quietly (V); [x] no sleeps; [x] no banned packages

## Risks and decisions

- **The precondition, checked** (2026-10-15, before any change): `payment-order-cancelled 0 messages, 2 consumers`, `notification-order-cancelled 0, 2`, `inventory-order-cancelled 0, 2`; every Order copy built from spec 016 or later. CI and fresh brokers never had v1 bindings.
- **Red first.** The stray-v1 test failed against the code before the retirement: Inventory still consumed v1, so nothing reached the skipped queue. After it: the stray v1 is in `inventory-order-cancelled_skipped`.
- **The precondition is a check, not a test.** Whether every running copy publishes v2 is a fact about deployments, not code; it is recorded here, as ADR 0010 asks. CI cannot see it.
- **Old bindings stay** (R-1, Q1). On this stack the `order-cancelled` exchange still routes to the three readers' queues; nothing publishes to it.
- **Seen on the real broker, after the retirement.** A v1 cancellation published to `order-cancelled` through RabbitMQ's management API was routed to all three readers and set aside in `inventory-order-cancelled_skipped`, `payment-order-cancelled_skipped` and `notification-order-cancelled_skipped`; the error-queue check failed, naming all three. The three test messages were then deleted.
