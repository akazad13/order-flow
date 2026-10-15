# Spec 018: Retire OrderCancelled v1

- **Status:** Done
- **Milestone:** After week 8, ninth follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 016](../016-event-versions/spec.md) (`OrderCancelled` v2; ADR 0010, step 3)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Since spec 016 the Order service publishes only `OrderCancelled` v2, but Inventory, Payment and Notification still read v1 too, so that a v1 published before the switch was not lost. Each of them carries a second interface, the upcast and a contract entry for a message nobody publishes. ADR 0010 says when v1 may go: once every copy of the publisher publishes v2, and each reader's queue has been empty at least once since.

## User stories

- As a developer, I want an event version nobody publishes to be gone from the code and the contract, so that there is one way the event looks.
- As the shop, I want a stray old message, if one ever turns up, to be noticed rather than lost.

## Precondition

ADR 0010's condition holds before v1 is removed: every Order copy publishes v2 only, and each reader's queue (`inventory-order-cancelled`, `payment-order-cancelled`, `notification-order-cancelled`) has been empty since. This is a fact about the running system, not about the code, so it is checked on the running stack and recorded in the plan rather than tested.

## Acceptance criteria

- **AC-1 Gone from the contract.** The events contract no longer has the v1 channel, message, schema or readers; it is a breaking change, so the contract gets a new major version (3.0.0), and the contract gate passes it.
- **AC-2 Gone from the code.** No v1 record, no upcast; each reader reads v2 only.
- **AC-3 Nothing else changes.** Every cancellation is still handled as before: stock released, payment settled, the customer told, the page updated. The full E2E suite passes 100 runs in a row with two copies of every service, with no faulted messages.
- **AC-4 A stray v1 is not lost silently.** Given a broker where a reader's queue was bound to the v1 exchange (as on any broker that ran v1), when a v1 cancellation arrives after the retirement, then it ends up in that reader's skipped queue, which the error-queue check after E2E reports (spec 009, AC-6).

## Business rules

- **R-1** Retiring removes the reading, not the routing: an existing broker keeps its bindings from the v1 exchange to the readers' queues, so anything still published there is set aside where it can be seen (AC-4), instead of being dropped.

## Edge cases

- A fresh broker (CI, a new environment) never had v1 bindings: nothing routes v1 to the readers, and nothing publishes it.
- An Order copy older than spec 016 started by mistake: its v1 cancellations land in the skipped queues and the check reports them.

## Non-goals

- Removing the v1 exchange and its bindings from existing brokers (R-1 keeps them on purpose).
- Retiring anything else.

## Open questions

Decided on the user's behalf (they asked to retire v1, 2026-10-15). Revisit in review.

- [x] **Q1 Keep the old bindings on existing brokers?** (answer: yes (R-1): with them a stray v1 is visible in a skipped queue; without them it would be dropped without a trace. They cost nothing.)
- [x] **Q2 How is a stray v1 tested (AC-4)?** (answer: an integration test on the real broker sets up the queue as a v1 reader would have left it, then starts the retired Inventory and publishes a v1: it must reach `inventory-order-cancelled_skipped`.)
