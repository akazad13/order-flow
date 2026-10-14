# Plan 013: Run several copies of each service

Spec: [spec.md](spec.md)

## Approach

Fix the three breaks the retro found, and one more found while testing:

| Break | Fix |
| --- | --- |
| Live updates: one shared queue, so one copy hears each change | `OrderUpdatesConsumer` gets a queue per copy (MassTransit `InstanceId`, `Temporary`: auto-deleted when the copy stops). Every copy hears every change and pushes to its own browsers (Q1). |
| Payment's test switches in one copy's memory | `ITestSwitches`: `InMemoryTestSwitches`, or `EfTestSwitches` on a `test_switches` table in `payments` (Q3). `FakePaymentProvider` reads them on each attempt. |
| Migrations at startup race | `app.MigrateAsync<TContext>(seed)` in ServiceDefaults: a PostgreSQL advisory lock named after the database, taken on the `postgres` maintenance database (the service's own may not exist yet), around migrating and seeding (Q2). |
| Found: the browser's live connection is two requests (negotiate, then the WebSocket) | The web client skips negotiation and opens the WebSocket directly, so nothing depends on reaching the same copy twice (AC-4). |

Two smaller changes support these. Each service now takes requests only once its bus is connected (`WaitUntilStarted`), so a copy never accepts a browser before its live-updates queue exists. E2E runs with `docker-compose.copies.yml`, which adds `order-2`, `inventory-2`, `payment-2` and `notification-2`. Each answers to its service's name as a network alias, so the web server spreads requests across both copies, and RabbitMQ shares each service queue between them.

## Services affected

| Service | Change |
| --- | --- |
| ServiceDefaults | `MigrationExtensions.MigrateAsync`; `WaitUntilStarted` |
| Order | Live-updates queue per copy; migrate through the helper |
| Payment | `ITestSwitches` and its two stores, `test_switches` table + migration; `FakePaymentProvider.AttemptAsync`; the in-memory ledger uses a semaphore (the attempt is now asynchronous) |
| Inventory | Migrate and seed the catalog through the helper |
| Notification | Migrate through the helper |
| Web | Live connection: WebSocket only, no negotiation |
| Compose, CI | `docker-compose.copies.yml`; CI E2E and nightly run with both files (`COMPOSE_FILE`) and wait for the second copies (`npm run wait:copies`) |

## Contract changes

None. The live-updates hub (`contracts/realtime/order-updates.asyncapi.yaml`) is unchanged; clients may still negotiate when they talk to a single copy.

## Test plan (criterion to layer)

| Criterion | Component | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Live updates reach watchers on every copy | | x | x | `SeveralCopiesTests.Watchers_on_either_copy_see_the_final_status` (each order watched on both copies); `customer-cancel.feature` with two copies |
| AC-2 Test switches apply on every copy | | x | x | `SeveralCopiesTests.A_switch_set_through_one_copy_applies_on_another`; `payment-failure.feature`, `customer-cancel.feature` with two copies |
| AC-3 Copies can start together | | x | | `SeveralCopiesTests.Three_copies_start_together_on_a_new_database` (Order, Payment); `InventoryServiceTests.Three_copies_start_together_on_a_new_database`; `NotificationServiceTests.Three_copies_start_together_on_a_new_database` |
| AC-4 The live connection works behind a load balancer | x | | x | `live.test.ts`; `customer-cancel.feature` with two copies behind the web server |
| AC-5 Everything still works end to end | | | x | All E2E scenarios with `docker-compose.copies.yml`, 100 runs |
| R-1 Work done once whichever copy takes it | | x | x | Existing concurrency tests (specs 007, 010, 011); E2E with two copies |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] consumers idempotent (IV, now with real copies); [x] no sleeps; [x] no banned packages

## Risks and decisions

- **A leftover queue on existing brokers.** The old shared `order-order-updates` queue stays bound to its exchanges after the change, and nothing consumes it any more. On an existing broker it must be deleted once (`rabbitmqctl delete_queue order-order-updates`); a fresh broker never has it.
- **The web server looks the copies up once.** nginx resolves `order` and `notification` when it starts; a copy started later is not used until the web server restarts (non-goal).
- **Red first.** Against the old code: the watcher on the second copy got no push; a decline set through one copy was ignored by the other (no `PaymentFailed`); three copies starting together on a new database failed on Inventory (two copies both seeded the catalog).
- **Found during build: EF Core's migration lock is not enough.** With one migration per service, the startup test passed for Order, Payment and Notification, so EF Core's own lock (Npgsql takes a `LOCK TABLE` while migrating) looked sufficient. Once Payment had a second migration, copies starting together failed with `42P07: relation "test_switches" already exists`, and Inventory with `42701: column "Status" already exists`: the earlier passes were luck. Hence the advisory lock; 10 of 10 repeats then passed.
- **E2E red first for AC-4.** With two copies and the old web page, the cancel scenario (which waits until the page is watching) failed 5 of 30 runs: the negotiate request and the WebSocket reached different copies, and the page's retries did not always connect in time. With the fix, 30 of 30, in half the time; then the stress run, 102 of 102, while the integration tests ran alongside.
- `test_switches` exists in every Payment database, including one that would talk to a real provider; it is only ever written through the test endpoints, which are off unless `Payment:EnableTestEndpoints` is set.
