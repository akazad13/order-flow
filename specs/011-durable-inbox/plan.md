# Plan 011: The customer's inbox survives a restart

Spec: [spec.md](spec.md)

## Approach

`IFakeInbox` becomes asynchronous with an explicit save, like the other stores. Two implementations: `InMemoryFakeInbox` (component tests, `dotnet run`) and `EfFakeInbox` on PostgreSQL (compose, integration tests), chosen by `Persistence:Provider` (ADR 0005). The table has a unique index on (order, text) (R-1). Delivering skips a message already there; two copies racing both add it, the second save fails on the index (23505) and the message is retried with a fresh scope.

The restart-test helper that counts handled messages moves from Payment's tests into `OrderFlow.IntegrationTesting`, so both projects share it.

## Services affected

| Service | Change |
| --- | --- |
| Notification | `IFakeInbox` async with `SaveChangesAsync`; `EfFakeInbox`, `NotificationDbContext` + migration; consumers save; storage switch and retry in `Program.cs` |
| Compose, CI | Notification on PostgreSQL (`notifications`); the migrations check covers Notification |

## Contract changes

None. `GET /inbox/{customerId}` answers the same.

## Test plan (criterion to layer)

| Criterion | Component | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Inbox survives a restart | | x | | `NotificationServiceTests.The_inbox_survives_a_restart` |
| AC-2 Told once across a restart | | x | | `NotificationServiceTests.A_repeat_after_a_restart_adds_nothing` |
| AC-3 Copies at the same moment | | x | | `NotificationServiceTests.Copies_at_the_same_moment_tell_once` |
| AC-4 E2E still green | x | | x | Existing notification scenarios (in memory); all E2E scenarios on compose, 100 runs |
| R-1, R-2 | x | x | | `duplicate-notification.feature`; the integration tests above |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] consumer idempotent across restarts; [x] no sleeps; [x] no banned packages

## Risks and decisions

- No outbox (Q1).
- **Red first, for the right reasons.** Against the in-memory inbox: after a restart the inbox was empty, and a repeat after a restart added the message again (same text, new message ID). The copies test passed in memory (one process); it guards the PostgreSQL version.
- **The race really happens:** 10 orders x 4 copies gave 75 conflicts on the (order, text) index, all retried; one message per order.
- **Mutation check (R-1):** with the index not unique, copies that pass the "already there?" check at the same moment both save, and the copies test fails.
- **On the real stack:** after the 100-run stress test, 200 inbox rows and 200 distinct (order, text) pairs; `docker compose restart notification` left a customer's inbox identical.
