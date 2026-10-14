# Spec 011: The customer's inbox survives a restart

- **Status:** Done
- **Milestone:** After week 8, second follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 004](../004-duplicate-events/spec.md) (told once), [Spec 007](../007-durable-state/spec.md), [Spec 010](../010-durable-payments/spec.md) (durable state, ADR 0005)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The Notification service keeps every customer's inbox in memory. A restart empties every inbox, and it also forgets which messages were already sent: a repeat of an event after the restart tells the customer the same thing again as if it were new. It is the last service whose state does not survive a restart.

## User stories

- As a customer, I want my messages to stay in my inbox, whatever happens to the shop's services.
- As a customer, I want to be told each thing once, even if the shop's services restart.

## Acceptance criteria

- **AC-1 Inbox survives a restart.** Given a customer was sent messages and the Notification service restarts, then their inbox still lists the same messages, in the same order.
- **AC-2 Told once across a restart.** Given a customer was told something about an order and the service restarts, when the same event arrives again, then the inbox still holds exactly the original message.
- **AC-3 Copies at the same moment.** Given copies of the same event arrive at the same moment for many orders, then each order's inbox holds one message per thing said.
- **AC-4 Everything still works end to end.** The full E2E suite passes with Notification on PostgreSQL, 100 runs in a row, with no faulted messages.

## Business rules

- **R-1** One message per order and text (spec 004, AC-5), whatever restarts in between.
- **R-2** The inbox is ordered by when things happened, then by when they were received (spec 001).

## Edge cases

- Two copies saving at the same moment: the second fails on the (order, text) uniqueness rule, is retried, and finds the message already there.

## Non-goals

- Real email or SMS; deleting or archiving messages.
- The MassTransit outbox for this service: Notification publishes nothing, so there is no announcement to commit with the message (Q1).

## Open questions

Decided on the user's behalf (they asked for this follow-up, 2026-10-14). Revisit in review.

- [x] **Q1 Outbox or not?** (answer: not. The outbox commits a state change with the messages it publishes; Notification publishes nothing. The uniqueness rule in the database makes delivery idempotent, and conflicts are retried, as in Inventory and Payment.)
- [x] **Q2 Its own database?** (answer: yes, `notifications`, created by the service's migrations.)
