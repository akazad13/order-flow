# Plan 000: Walking skeleton

Spec: [spec.md](spec.md)

## Approach

Build the thinnest path UI, Order API, RabbitMQ, Notification, fake inbox, with every test layer represented once. Inventory and Payment exist as empty, connected services so later specs only add consumers.

## Services affected

| Service | Change |
| --- | --- |
| Order | `POST /orders`, `GET /orders/{id}`; in-memory store; publishes OrderPlaced |
| Notification | Consumes OrderPlaced; fake inbox; `GET /inbox/{customerId}` |
| Inventory, Payment | Empty services with `/health`, connected to the broker |
| Web | Order form, order status, inbox (polling every second) |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `contracts/http/order.openapi.yaml` | New: placeOrder, getOrder | New |
| `contracts/http/notification.openapi.yaml` | New: getInbox | New |
| `contracts/events/asyncapi.yaml` | New channel `order-placed` | New |
| `contracts/events/schemas/order-placed.v1.json` | New schema | New |

## Data model changes

None persisted. `Order` (id, customerId, lines, status, placedAt) lives in memory.

## Test plan (criterion to layer)

| Criterion | Unit | Component | Contract | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 | x | x | x | | `OrderTests`, `features/component/place-order.feature`, `EventSchemaContractTests` |
| AC-2 | x | x | | | `OrderTests`, `features/component/place-order.feature` |
| AC-3 | | x | | | `features/component/order-received-notification.feature` |
| AC-4 | | | | x | `features/e2e/walking-skeleton.feature` |

UI behavior (status shown, validation errors listed) is also covered by `web/src/App.test.tsx` (Vitest).

## Constitution check

- [x] Spec first (I)
- [x] Contracts updated in this PR (II)
- [x] Every AC automated (III)
- [ ] Events versioned and consumers idempotent (IV): versioned yes; idempotency deferred to spec 004
- [x] No sleeps, tests own their data (V)
- [x] No banned packages (VI)

## Risks and decisions

- Save and publish are not atomic. Accepted for week 1; fixed by the outbox in week 5.
- Queue names are prefixed per service (`notification-order-placed`). Without the prefix the queue would collide with the `order-placed` exchange; the component test caught this.
