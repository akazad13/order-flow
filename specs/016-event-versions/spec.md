# Spec 016: A new version of an event, next to the old one

- **Status:** Done
- **Milestone:** After week 8, seventh follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 014](../014-contract-compatibility/spec.md) (no breaking edit of a published event, constitution IV), [Spec 015](../015-staff-and-token-refresh/spec.md) (staff cancel on the customer's behalf)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Constitution IV says a breaking change to an event "is a new version, not an edit", and since spec 014 the contract gate refuses the edit. Nobody has said what a new version looks like: what it is called, how services that read the old one keep working while the new one rolls out, and when the old one can go.

Spec 015 needed one and worked around it: a staff cancellation is announced as "at the customer's request", because a new reason in `OrderCancelled` would have been a breaking edit. So the customer is told "Your order was cancelled at your request" even when support pressed the button. This spec designs event versions and uses the design for that change: `OrderCancelled` v2, with the reason `StaffRequested`.

## User stories

- As a developer, I want one way to publish a new version of an event, so that a breaking change never breaks a service that reads the old one.
- As a customer whose order support cancelled for me, I want to be told support cancelled it.

## Acceptance criteria

- **AC-1 Staff cancellations say so.** Given staff cancel a customer's order, then the order's reason is "StaffRequested" (reading it, and in its live update), the customer's inbox says "Your order was cancelled by our support team at your request", and the customer's and staff's pages say so in their words.
- **AC-2 The new version is published instead of the old.** When an order is cancelled for any reason, the Order service publishes `OrderCancelled` v2, and no longer v1.
- **AC-3 Every reader of v1 reads v2.** Inventory releases the stock, Payment refunds or voids, and Notification tells the customer for a v2 cancellation exactly as for a v1 one; Order's live updates push it.
- **AC-4 Old messages still work.** Given a v1 cancellation published before the switch (still on the broker during a deploy), when it arrives, then Inventory, Payment and Notification handle it as before.
- **AC-5 The gate keeps the migration safe.** The contract gate fails when a new version of an event is published while a service that reads the previous version does not read the new one (it would miss every such event from then on). Stopping publishing the old version is a breaking change of the events contract, so it needs a new major version.
- **AC-6 End to end.** With two copies of every service, the full E2E suite passes 100 runs in a row with no faulted messages, and the staff scenario shows the new words.

The design itself (names, the order of releases, when to retire) is written down in ADR 0010.

## Business rules

- **R-1** Versions of one event share the meaning; a reader handles both the same way, by turning an old-version message into the new shape ("upcasting") and handling that.
- **R-2** A publisher publishes one version of an event, never two at once: readers of both would act twice.
- **R-3** Readers move first: a new version is published only once every reader of the old one reads it too (AC-5).

## Edge cases

- A v1 and a v2 for the same order (one from before the switch, one after, e.g. a repeat): readers are idempotent per order (specs 004, 010, 011), so the second does nothing new.
- A copy of Order still running the old code during a deploy publishes v1: readers handle it (AC-4).
- A reader that does not care about the reason (Inventory, Payment) still moves to v2: it would otherwise stop receiving cancellations.

## Non-goals

- Retiring v1 now: it is a later release, once the condition in the ADR holds (every Order copy publishes v2, and each reader's queue has been empty once since).
- Versioning other events, or the HTTP APIs side by side (their breaking changes are major versions of the contract; spec 014).
- A schema registry.

## Open questions

- [x] **Q1 Which event first?** (answered by the user, 2026-10-15: `OrderCancelled` v2, adding the reason `StaffRequested`.)

Decided on the user's behalf (they asked to start this follow-up, 2026-10-15). Revisit in review.

- [x] **Q2 One reader class for both versions, or two?** (answer: one class consuming both, so one queue per service keeps receiving cancellations; v1 is upcast to v2 and handled once (R-1).)
- [x] **Q3 Switch the publisher in the same release as the readers?** (answer: here, yes: compose starts every service together and E2E waits until all are up, so readers are listening before anything is cancelled. The ADR keeps readers-first as separate releases for anything deployed piecemeal, and the gate enforces the order in the contract (R-3).)
- [x] **Q4 The order API and the live updates also gain `StaffRequested`.** (answer: yes, so the order's own record matches the event; a new value in a response is breaking for clients, so both contracts get a new major version, 2.0.0. The web client is the only client and moves in the same change.)
