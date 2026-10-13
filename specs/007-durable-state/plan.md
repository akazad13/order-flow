# Plan 007: Durable state and reliable announcements

Spec: [spec.md](spec.md)

## Approach

PostgreSQL via EF Core for the Order and Inventory services, plus the MassTransit Entity Framework **transactional outbox**: messages are written to an outbox table in the same transaction as the state change, and a delivery service sends them once the broker is reachable. The consumer side of the outbox also records received message IDs (an inbox).

Storage is chosen by configuration, `Persistence:Provider` = `InMemory` (default; unit and component tests, `dotnet run`) or `Postgres` (docker compose, integration tests). The domain, consumers and state machine are identical in both; only repositories and MassTransit wiring differ. See ADR 0005.

## Services affected

| Service | Change |
| --- | --- |
| Order | `OrderDbContext`: orders, saga state (`OrderStateMap`), outbox/inbox tables. EF saga repository (pessimistic locks). Bus outbox for `POST /orders` (save + publish OrderPlaced in one transaction). Consumer outbox on the state machine endpoint. `IOrderRepository.SaveChangesAsync`. |
| Inventory | `ICatalogStore` with `InMemoryCatalogStore` (wrapping today's `Catalog`) and `EfCatalogStore`; shared `StockRules`. `InventoryDbContext`: stock items (optimistic concurrency on `xmin`), reservations, outbox/inbox. Consumers call `SaveChangesAsync` after publishing; a concurrency conflict retries the message. |
| ServiceDefaults | `RabbitMq:Port` setting (containers use random ports). |
| docker compose | `postgres` service; Order and Inventory use `Persistence__Provider=Postgres`. |

Schema is managed with EF Core migrations (`dotnet ef`, local tool) and applied at startup.

## Contract changes

None. Storage is internal; every HTTP and event contract is unchanged, which the existing contract, component and E2E tests confirm.

## Test plan (criterion to layer)

New layer: `tests/OrderFlow.Order.IntegrationTests`, `tests/OrderFlow.Inventory.IntegrationTests`, using Testcontainers (PostgreSQL + RabbitMQ). The service runs in-process (WebApplicationFactory) against the real containers; a separate test bus plays the other services. Marked `[Trait("Category", "Integration")]`.

| Criterion | Integration | E2E | Test |
| --- | --- | --- | --- |
| AC-1 Orders survive restart | x | | `OrderDurabilityTests.Order_and_its_progress_survive_a_restart` |
| AC-2 Stock and holds survive restart | x | | `InventoryDurabilityTests.Holds_survive_a_restart_and_are_released_once` |
| AC-3 Saved and announced together | x | | `OrderOutboxTests.Order_placed_while_the_broker_is_down_is_announced_when_it_is_back` |
| AC-4 No overselling | x | | `InventoryConcurrencyTests.Twenty_orders_for_ten_items_hold_exactly_ten` |
| AC-5 Many orders at once | x | | `OrderConcurrencyTests.Thirty_orders_at_once_all_confirm` |
| AC-6 E2E still green | | x | All E2E scenarios, 100 runs |
| R-1, R-2 | x | | Covered by AC-3 and AC-4 (outbox commits state and message together) |

## Constitution check

- [x] Spec first; [x] no contract changes (verified by unchanged contract tests); [x] every AC automated; [x] no sleeps (tests wait on received messages with timeouts); [x] no banned packages
- Constitution III's table gains an Integration row.

## Risks and decisions

- **Two storage modes** could drift. Mitigation: the same component scenarios cannot catch EF-only bugs, so every Postgres-specific behavior (outbox, locking, concurrency tokens) has an integration test.
- **Migrations at startup** are fine for one instance; with several instances they would race. Noted in ADR 0005.
- **Found during build: PostgreSQL serialization failures (40001).** The MassTransit outbox and saga repository use REPEATABLE READ; two events for one order updating its row at once made the second transaction fail instead of wait (9 of 30 orders lost to the error queue in AC-5's first run). Fix: retry serialization failures and deadlocks (40001, 40P01) wherever they are wrapped, in both services.
- **Found during build: a restart mid-message.** Disposing the first host while it was still handling OrderPlaced faulted that message (disposed service provider). The AC-1 test now waits until the order's progress is stored before restarting, which is also a sharper test of "its progress survives".
- **EF Core version skew.** `Microsoft.EntityFrameworkCore.Design` (private) lifted the services to 10.0.13 while tests resolved an older version. `Microsoft.EntityFrameworkCore.Relational` is now pinned centrally.
- **Testcontainers needs Docker** for `dotnet test` of the whole solution. `--filter Category!=Integration` keeps the fast loop Docker-free.
