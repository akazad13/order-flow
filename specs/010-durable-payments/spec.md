# Spec 010: Payment decisions survive a restart

- **Status:** Done
- **Milestone:** After week 8, first follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 004](../004-duplicate-events/spec.md) (charge once), [Spec 006](../006-customer-cancel/spec.md) (refund or void), [Spec 007](../007-durable-state/spec.md) (durable state, ADR 0005)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Payment remembers what it decided for each order (charged, declined, voided, refunded) only in memory. RabbitMQ delivers a message at least once, so a repeat of "stock held" can arrive after the service restarts: today it finds no memory of the order and charges the customer again. The same restart forgets refunds and voids, so a cancelled order's late "stock held" news can be charged and never refunded. Order and Inventory already keep their state in PostgreSQL (spec 007); Payment is the one service where forgetting costs the customer money.

## User stories

- As a customer, I want to be charged at most once per order and refunded at most once, even if the shop's services restart.
- As the shop, I want every payment decision recorded, so that a restart never changes what happened to a customer's money.

## Acceptance criteria

- **AC-1 Charged once across a restart.** Given an order was charged and the Payment service restarts, when the same stock-held news arrives again, then the customer is not charged again and nothing new is announced.
- **AC-2 Voids and refunds survive a restart.** Given an order was cancelled before it was charged and the service restarts, when its stock-held news arrives, then it is never charged. Given an order was charged, the service restarts and the order is cancelled, then it is refunded once; a repeat of the cancellation after another restart refunds nothing more.
- **AC-3 Never announced without being recorded.** A payment decision is announced only if it was saved, and every saved decision is announced: the decision and its announcement are committed together.
- **AC-4 Copies at the same moment.** Given two copies of the stock-held news, or of the cancellation, arrive at the same moment for many orders, then each order is charged at most once and refunded at most once.
- **AC-5 Everything still works end to end.** The full E2E suite passes with Payment on PostgreSQL, 100 runs in a row.

## Business rules

- **R-1** The first decision for an order wins and is kept: Charged, Declined or Voided; a Charged order may later become Refunded, once (specs 004, 006).

## Edge cases

- A copy arrives while the first is still being saved: both may find no decision; the second fails to save (the order's row already exists) and is retried, then finds the decision.
- A charge and a cancellation for the same order at the same moment: whichever saves first decides; the other is retried and sees it (refund, or nothing to charge).
- Two cancellations of a charged order at the same moment: both try to mark it refunded; the second fails on the row's version and is retried, then finds it refunded.

## Non-goals

- A real payment provider. (With one, the charge call would carry the order ID as an idempotency key, so a retried attempt cannot charge twice at the provider either.)
- Keeping the test-only switches (declines, stalls) across a restart: tests set them again.
- Notification's inbox in PostgreSQL: a separate follow-up.

## Open questions

Decided on the user's behalf (they asked to start this follow-up, 2026-10-14). Revisit in review.

- [x] **Q1 Same pattern as Order and Inventory?** (answer: yes: EF Core on PostgreSQL in compose and integration tests, in memory for component tests and `dotnet run`, and the MassTransit consumer outbox so the decision and its announcement commit together; ADR 0005 extended, no new ADR.)
- [x] **Q2 Its own database?** (answer: yes, `payments`, created by the service's migrations like `orders` and `inventory`.)
