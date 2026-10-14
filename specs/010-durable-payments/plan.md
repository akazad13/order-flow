# Plan 010: Payment decisions survive a restart

Spec: [spec.md](spec.md)

## Approach

Split the fake gateway in two. `FakePaymentProvider` stands in for the external provider: it holds the test switches (declines, stalls) and decides a charge. `IPaymentLedger` remembers each order's decision and applies the charge-once and settle-once rules; it has an in-memory implementation (`InMemoryPaymentLedger`, the old gateway's logic) and a PostgreSQL one (`EfPaymentLedger`). Consumers ask the ledger, publish, then save: with the consumer outbox the decision and its announcement commit together (AC-3). Concurrency is handled like Inventory: the order ID is the primary key (copies racing to decide), the row has an `xmin` version (copies racing to refund), and the endpoint retries those failures with a fresh scope.

| Ledger finds | ChargeOnce | Settle (order cancelled) |
| --- | --- | --- |
| nothing | ask the provider; record Charged or Declined (nothing if stalled) | record Voided |
| Charged | nothing | Refunded, publish PaymentRefunded |
| Declined, Voided, Refunded | nothing | nothing |

## Services affected

| Service | Change |
| --- | --- |
| Payment | `FakePaymentProvider`, `IPaymentLedger`, `InMemoryPaymentLedger`, `EfPaymentLedger`, `PaymentDbContext` + migration; consumers save after publishing; storage switch, outbox and retry in `Program.cs` |
| Compose, CI | Payment on PostgreSQL (`payments`); the migrations check covers Payment |

## Contract changes

None. Behavior across restarts only.

## Test plan (criterion to layer)

| Criterion | Component | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Charged once across a restart | | x | | `PaymentServiceTests.A_repeat_after_a_restart_charges_nothing` |
| AC-2 Voids and refunds survive a restart | | x | | `PaymentServiceTests.A_void_survives_a_restart`; `PaymentServiceTests.A_refund_after_a_restart_happens_once` |
| AC-3 Never announced without being recorded | | x | | `PaymentServiceTests.Copies_at_the_same_moment_charge_and_refund_once` (a copy that loses the race had already decided and published; its announcement must vanish with its failed save) |
| AC-4 Copies at the same moment | | x | | `PaymentServiceTests.Copies_at_the_same_moment_charge_and_refund_once` |
| AC-5 E2E still green | x | | x | Existing `settle-payment.feature`, `take-payment.feature` (in memory); all E2E scenarios on compose, 100 runs |
| R-1 First decision wins | x | x | | `duplicate-payment.feature`, `settle-payment.feature`; integration tests above |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] consumers idempotent across restarts (IV, now for Payment too); [x] no sleeps; [x] no banned packages

## Risks and decisions

- Migrations at startup (ADR 0005): fine with one instance.
- The test switches stay in memory (non-goal); compose E2E sets them per scenario anyway.
- **Red first, for the right reasons.** Against the old in-memory Payment, the restart tests failed exactly as the retro predicted: the repeat after a restart was charged again, a voided order was charged after a restart, and a charged order was never refunded after one (the charge was forgotten). The concurrency test passed in memory (one process, one lock); it guards the PostgreSQL version.
- **Found while writing the tests: MassTransit's ambient log context.** Resolving `IBus` from a host on the test's own execution flow set an `AsyncLocal` that outlived the host, and the next host (after the "restart") failed to start with a disposed `LoggerFactory`. The test helper resolves the bus on another flow.
- **The races really happen.** With 10 orders x 4 copies, the logs show about 180 primary-key conflicts on `payments` (copies deciding the same order) and about 50 PostgreSQL serialization failures (40001, copies refunding the same charge: the consumer outbox's REPEATABLE READ transaction stops the second one before the `xmin` check would). All retried; each order charged once and refunded once.
- **Mutation check (AC-3):** without the consumer outbox, a copy that loses the race has already published, and the test fails with two `PaymentSucceeded` for one order.
- **Found by the stress run:** 2 of 100 E2E runs failed the spec 005 "status was pushed" check; a race in the check, not the product (see plan 005). Fixed; then 100 of 100.
