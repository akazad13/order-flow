# Spec 004: Handle duplicate events without side effects

- **Status:** Done
- **Milestone:** Week 6, Contracts and idempotency
- **Builds on:** specs 001 to 003, 007
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Message delivery is "at least once": the broker redelivers after a crash or a lost acknowledgement, and a service that restarts between sending and recording can send the same news again with a new message ID. The outbox's inbox (spec 007) only drops an exact repeat of a message it already stored, and only with PostgreSQL. Today a repeated "stock held" would charge the customer twice, a repeated "order placed" would hold stock twice, and the customer would get the same message twice.

## User stories

- As a customer, I want to be charged once per order, however often the shop's systems repeat themselves.
- As the shop, I want stock held once per order, so repeats never make items look sold out.
- As a customer, I want each update about my order once.

## Acceptance criteria

- **AC-1 Charged once.** Given an order's payment was already decided (charged or declined), when the news that its stock is held arrives again, then the customer is not charged again and no second payment result is announced.
- **AC-2 Held once.** Given stock was already decided for an order (held or rejected), when the order placement arrives again, then nothing more is held or released and no second stock result is announced.
- **AC-3 Decisions stick.** Given an order was rejected for a shortage, when the same order placement arrives again after the stock was refilled, then it is still not held. Given an order's stock was released after a cancellation, when the order placement arrives again, then nothing is held again.
- **AC-4 Order moves once.** Given an order is already Confirmed or Cancelled, when any of its earlier events arrives again, then its status and total do not change, and the confirmation or cancellation is announced only once.
- **AC-5 Told once.** Given a customer was already told something about an order, when the same event arrives again, then the inbox still shows that message once.
- **AC-6 Repeats at the same moment.** Given two copies of an order placement arrive at the same moment (PostgreSQL storage), then stock is held once.

## Business rules

- **R-1** A repeat is acknowledged and ignored: no new effects, no new announcements (announcing again would only multiply duplicates downstream).
- **R-2** The first decision for an order wins and is remembered (held, rejected, released, charged, declined).

## Non-goals

- Deduplicating by message ID in memory mode (the PostgreSQL inbox already does it there).
- Expiring the remembered decisions.

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 Ignore a repeat, or announce the original result again?** (answer: ignore, R-1. With the outbox, the original announcement is never lost, so repeating it only adds duplicates.)
- [x] **Q2 Model each ordering of events as explicit states (the plan for spec 004 since spec 001)?** (answer: no. Specs 001 and 003 showed the bounded retry handles events that arrive early, under 100-run stress, with far less code. This spec only adds explicit "ignore" rules for repeats. ADR 0004 is updated.)
