# Plan 004: Handle duplicate events without side effects

Spec: [spec.md](spec.md)

## Approach

Every consumer that causes an effect remembers its first decision per order and ignores repeats (R-1, R-2). Duplicates in tests are real publishes with new message IDs, so no inbox can hide them.

| Service | Remembers | A repeat of | Does |
| --- | --- | --- | --- |
| Payment | outcome per order (charged or declined) | StockReserved | nothing |
| Inventory | decision per order: Held, Rejected, Released | OrderPlaced | nothing; `OrderCancelled` releases only a Held order |
| Order | state machine state | any earlier event in Confirmed or Cancelled, StockReserved in AwaitingPayment, OrderPlaced in AwaitingStock | `Ignore(...)`, so repeats are not retried and never faulted |
| Notification | message per (order, text) | any event | nothing |

## Services affected

| Service | Change |
| --- | --- |
| Payment | `FakePaymentGateway` records outcomes by order; `ChargeOnce` returns null for a repeat; consumer publishes nothing then |
| Inventory | `ReservationResult.AlreadyDecided`; in memory: decisions dictionary; PostgreSQL: `reservations.Status` (Held, Rejected, Released), rows kept on release; migration `ReservationStatus` |
| Order | `Ignore` rules in `OrderStateMachine` |
| Notification | `FakeInbox.Deliver` skips a message already delivered for the same order and text |

## Contract changes

None. Behavior only.

## Test plan (criterion to layer)

| Criterion | Unit | Component | Integration | Test |
| --- | --- | --- | --- | --- |
| AC-1 Charged once | | x | | `duplicate-payment.feature` (charged, declined) |
| AC-2 Held once | x | x | | `CatalogTests`; `duplicate-order-placed.feature` |
| AC-3 Decisions stick | x | x | | `CatalogTests`; `duplicate-order-placed.feature` (after rejection, after release) |
| AC-4 Order moves once | | x | | `duplicate-order-events.feature` (after confirm, after cancel) |
| AC-5 Told once | | x | | `duplicate-notification.feature` (no Notification unit-test project; the inbox is a few lines) |
| AC-6 Simultaneous repeats | | | x | `InventoryServiceTests.Two_copies_of_one_order_at_once_hold_once` |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] consumers idempotent (IV is now met for every consumer); [x] no sleeps; [x] no banned packages

## Risks and decisions

- Remembered decisions grow forever (non-goal); acceptable at this scale.
- Explicit out-of-order states rejected in favour of the bounded retry (spec Q2, ADR 0004).
- **Found during build: the inventory migration.** `dotnet ef` generated `defaultValue: ""` for the new `Status` column, which would have left every existing hold with an empty status (and never releasable). Changed by hand to `"Held"`; verified on the compose database: 52 existing rows became Held.
- **Found during build: a race in the first Payment version.** `ConcurrentDictionary.GetOrAdd` always returns the stored value, so two simultaneous first copies would both have counted as "decided now". Replaced with a lock.
- **Test helpers.** MassTransit's `SelectAsync` stops when the harness looks idle, and the state machine harness does not record ignored events; the Order repeat step polls the bus-level harness instead. Because two first red runs failed inside helpers, each service's scenarios were re-run against the code without the fix (git stash): Order and Inventory scenarios fail for the right reasons.
- Two copies at once in PostgreSQL: both may see "no decision yet"; the second fails on the stock row's `xmin` or the reservation's primary key and is retried, then sees the decision. Unique violations (23505) join the retried errors.
