# Plan 005: See order status change live

Spec: [spec.md](spec.md)

## Approach

The Order service hosts a SignalR hub. A client calls `WatchOrder(orderId)` and joins that order's group. A new consumer in the Order service, `OrderUpdatesConsumer`, receives the service's own `OrderConfirmed` and `OrderCancelled` (after commit: the outbox only delivers committed messages), reads the order and sends `OrderUpdated(order)` to the group.

```text
state machine --OrderConfirmed/OrderCancelled--> RabbitMQ --> OrderUpdatesConsumer --OrderUpdated--> hub group "order-{id}" --> browser
```

The UI replaces the order poll with `watchOrder(orderId, onUpdate, onWatching)` (`web/src/api/live.ts`). `onWatching` fires after every (re)join and triggers one read (AC-3). Updates go through `latest(prev, next)`, which keeps a final status (AC-5).

## Services affected

| Part | Change |
| --- | --- |
| Order | `AddSignalR`, `OrderUpdatesHub` at `/hubs/orders`, `OrderUpdatesConsumer` |
| Web | `@microsoft/signalr`; `live.ts`; App watches instead of polling the order (the inbox poll stays) |
| nginx, Vite | WebSocket upgrade for `/order-api/hubs/` |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `contracts/realtime/order-updates.asyncapi.yaml` | New: `WatchOrder` (client to server), `OrderUpdated` (server to client, `Order` schema) | New |
| `package.json` | `lint:asyncapi` also validates the realtime document | No |

## Test plan (criterion to layer)

| Criterion | Unit | Component | Web | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Pushed on change | | x | x | x | `live-order-updates.feature` (confirmed, cancelled; payload deep-equals GET); `App.test.tsx`; `features/e2e/customer-cancel.feature` (push frame) |
| AC-2 Only that order | | x | | | `live-order-updates.feature` "other orders" |
| AC-3 Nothing missed | | | x | | `App.test.tsx` (reads once per watch start) |
| AC-4 No polling | | | x | x | `App.test.tsx` (fake timers); E2E read count |
| AC-5 Never backwards | | | x | | `App.test.tsx` (slow read after push) |
| AC-6 Customer sees it live | | | | x | `features/e2e/place-order.feature` |

## Constitution check

- [x] Spec first; [x] contract in the same change (AsyncAPI for the hub); [x] every AC automated; [x] no new events; [x] pushes are idempotent (same order twice); [x] no sleeps (channels with timeouts, fake timers); [x] no banned packages

## Risks and decisions

- A push sent while the client is between connect and `WatchOrder` is lost; AC-3's read after joining covers it.
- One consumer queue (`order-order-updates`) per instance would be needed for several instances (non-goal).
- **Found during build: flaky harness waits (constitution V).** A spec 001 scenario failed 1 run in 8: `Saga.Consumed.Any<StockReserved>` gave up after 2 s. The harness's `Any` and `SelectAsync` stop as soon as the bus looks idle, which on a busy machine can be before the message is handled. With all test projects running at once, Inventory's `SelectAsync(...).Take(n)` helpers then failed in most runs. Every positive wait in the component tests now polls (`tests/Shared/Eventually.cs`, linked into each project); negative checks ("nothing published") keep `Any`, since waiting for idle is right there. Then 10 of 10 full fast runs green.
- **Found during build: the web image did not build.** The Pact test (spec 008) imports `node:path`. `tsc` found Node's types in the repo root's `node_modules`, which CI's web job and the Docker build do not have. Added `@types/node` (22) to `web`.
- **E2E check strengthened.** "At most one read" alone can pass with a 1-second poll (the order confirms in about a second). The scenario also checks that an `OrderUpdated` frame arrived on the hub's WebSocket; against the old Order image that check failed.
- **Found later (spec 010 stress run, 2 in 100): the E2E push check raced.** "The status was pushed to the page" sat in the place-order scenario, but an order can be confirmed before the page joins the hub; then it arrives through the one read (AC-3), correctly, and no push frame exists. The check moved to the cancel scenario, which first waits until the page is watching (`data-watching`), so the outcome can only arrive as a push.
- Mutation checks: broadcasting to every client fails "nothing about other orders"; a timer re-read fails AC-4; dropping the stale-read guards fails AC-5.
