# Spec 007: Durable state and reliable announcements

- **Status:** Done
- **Milestone:** Week 5, Real infrastructure
- **Builds on:** specs 000 to 003
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

> Numbered 007 because 004 to 006 are already promised to features (idempotency, live status, customer cancel).

## Problem

Orders, their progress, stock levels and stock holds all live in process memory. Restarting the Order or Inventory service loses every order and every hold: customers see "not found", held stock is never released, and stock counts reset to the seed. Separately, an order is saved and then announced as two steps; if the announcement fails, the order exists but nothing ever happens to it.

## User stories

- As the shop, I want orders and stock to survive a restart or a deploy, so that no customer's order is lost.
- As the shop, I want an order to be announced if and only if it was saved, so that no order is stuck and no ghost order is processed.
- As a developer, I want tests that run against the real database and broker, so that infrastructure bugs are caught before E2E.

## Acceptance criteria

- **AC-1 Orders survive a restart.** Given an order was placed, when the Order service restarts, then the order is still there with the same status and total, and events that arrive after the restart still move it forward (e.g. it is confirmed).
- **AC-2 Stock and holds survive a restart.** Given stock was held for an order, when the Inventory service restarts, then available stock is unchanged, and a later cancellation still releases exactly what was held.
- **AC-3 Saved and announced together.** Given the message broker is unreachable when a customer places an order, then the order is still accepted, and it is announced as soon as the broker is back, without anyone retrying by hand.
- **AC-4 No overselling with real storage.** Given an item with 10 in stock, when 20 orders for 1 each are processed at the same time, then exactly 10 are held and 10 are rejected, and available stock ends at 0.
- **AC-5 Many orders at once.** Given 30 orders are placed at the same time, then every one reaches Confirmed.
- **AC-6 Everything still works end to end.** The full E2E suite passes 100 runs in a row against the stack with storage.

## Business rules

- **R-1** An order is never announced unless it was saved, and never saved without being announced (eventually).
- **R-2** Stock changes and the matching announcement (held, rejected, released) are committed together.

## Non-goals

- Durable Payment and Notification state: both are fakes standing in for external systems.
- Backups, high availability, multiple instances per service.
- Duplicate deliveries handled as business rules (spec 004); the storage brings duplicate-message protection for free, but 004 owns the behavior.

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 Should fast tests keep running without Docker?** (answer: yes. Unit and component tests keep in-memory storage; a new integration layer uses real PostgreSQL and RabbitMQ in containers.)
- [x] **Q2 One database or one per service?** (answer: one PostgreSQL server, one database per service. Services never read each other's tables.)
