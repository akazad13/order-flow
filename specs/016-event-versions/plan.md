# Plan 016: A new version of an event, next to the old one

Spec: [spec.md](spec.md)

## Approach

The design is ADR 0010: a new version is a new message (record `OrderCancelledV2`, URN `orderflow:order-cancelled:v2`, exchange `order-cancelled-v2`, schema `order-cancelled.v2.json`); readers move first, then the publisher switches, then the old version is retired. This spec does the first two steps for `OrderCancelled`, in one release (Q3), and leaves the third for later.

| Service | Before | After |
| --- | --- | --- |
| Order (publisher) | publishes v1 | publishes v2 only (R-2); staff cancellations get the reason `StaffRequested` |
| Order (live updates) | reads v1 (undocumented) | reads v2; documented as `orderReceivesOrderCancelledV2` |
| Inventory, Payment, Notification | read v1 | one consumer class reads both; v1 is upcast with `ToV2()` (R-1, Q2) |
| Notification | | words for `StaffRequested`: "Your order was cancelled by our support team at your request" |

The contract gate gains `readersFollow` (AC-5, R-3): by the operation naming convention, while `publishNameVN` exists, every `<service>ReceivesName` needs `<service>ReceivesNameVN`.

## Services affected

| Area | Change |
| --- | --- |
| Contracts (.NET) | `OrderCancelledV2`; `OrderCancelled.ToV2()`; `CancellationReasons.StaffRequested` |
| Order | Publishes v2; staff reason; live updates read v2 |
| Inventory, Payment, Notification | Consumers read both versions |
| Web | Words for `StaffRequested` on the customer's page and the staff page; the customer's own cancellation is "you cancelled it" again (spec 015 had changed it while staff shared the reason) |
| Gate | `readersFollow` in `scripts/check-contracts.mjs` |
| Docs | ADR 0010; constitution IV points to it |

## Contract changes

| Contract | Change | Version |
| --- | --- | --- |
| `events/asyncapi.yaml` | Channel, message and schema `OrderCancelledV2` (reason adds `StaffRequested`); `publishOrderCancelled` replaced by `publishOrderCancelledV2`; v2 readers for Inventory, Notification, Payment and Order | 1.1.0 to 2.0.0 (the gate notes the removed publish operation) |
| `http/order.openapi.yaml` | `Cancellation.reason` adds `StaffRequested` (Q4) | 1.1.0 to 2.0.0 (oasdiff: a new value in four responses) |
| `realtime/order-updates.asyncapi.yaml` | Same, through the shared `Order` schema | 1.1.0 to 2.0.0 |

## Test plan (criterion to layer)

| Criterion | Unit | Component | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Staff cancellations say so | x | x | x | `staff-orders.feature` "Staff cancel a pending order on the customer's behalf"; `order-cancelled-notification.feature` (StaffRequested); `StaffLookup.test.tsx`; `staff-support.feature` |
| AC-2 The new version is published instead of the old | | x | | `staff-orders.feature`, `customer-cancel.feature`, `cancel-order.feature` (step "OrderCancelled is published for that order with reason" checks v2 and no v1) |
| AC-3 Every reader of v1 reads v2 | | x | x | `release-stock.feature`, `settle-payment.feature`, `order-cancelled-notification.feature` (v2 by default); `live-order-updates.feature`; all E2E scenarios |
| AC-4 Old messages still work | | x | | `release-stock.feature`, `settle-payment.feature`, `order-cancelled-notification.feature` ("published as the old version (v1)") |
| AC-5 The gate keeps the migration safe | x | | | `check-contracts.test.mjs` ("readersFollow", "fails a published event version that a reader of the previous version does not read") |
| AC-6 End to end | | | x | All E2E scenarios with `docker-compose.copies.yml`, 100 runs |
| R-1 Versions share the meaning | | x | | `EventSchemaContractTests` (an upcast v1 is a valid v2); v1 scenarios above |
| R-2 One version at a time | | x | | Step "OrderCancelled is published for that order with reason" (no v1) |
| R-3 Readers move first | x | | | `check-contracts.test.mjs` ("readersFollow") |

## Constitution check

- [x] Spec first; [x] no published event edited, a new version instead (IV); [x] contracts' versions raised, breaking changes as major versions (spec 014); [x] every AC automated; [x] no sleeps; [x] no banned packages

## Risks and decisions

- **Red first, on the real contract.** Publishing `OrderCancelledV2` before adding readers, the gate failed naming inventory, notification and payment ("would miss every OrderCancelled from now on"); with the readers, it passed, noting the removed v1 publish operation as allowed by 2.0.0.
- **Mutation checks** (the service code was written before these scenarios): Notification without its v1 interface fails the v1 scenario; Inventory without its v2 interface fails all five of its cancellation scenarios; Order publishing v1 as well as v2 fails six scenarios on "v1 is no longer published".
- **Found: a Reqnroll pattern quirk.** In a step's pattern, `(v1)` means "optional v1", so the step "...of the event (v1)" never matched its own text and the scenario was reported pending. The parentheses are escaped (`\\(v1\\)`).
- **Found: an undocumented reader.** Order's live-updates consumer read `OrderCancelled` but was not in the contract, so the gate could not have protected it. It is documented now (`orderReceivesOrderCancelledV2`).
- **The gate trusts the naming convention.** An operation not named `publish<Message>` or `<service>Receives<Message>` is invisible to `readersFollow`; every operation in `contracts/events` follows the convention today.
- **Retiring v1 is open** (non-goal): the ADR's condition, then a release that drops v1 from the three readers and the contract (3.0.0).
