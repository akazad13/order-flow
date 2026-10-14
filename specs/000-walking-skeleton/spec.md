# Spec 000: Walking skeleton

- **Status:** Done
- **Milestone:** Week 1, Foundations
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Before building real features we need proof that every moving part is wired together: a customer action in the UI reaches a service, that service announces it as an event, another service reacts, and the customer can see the result. Without this, the first real feature would also have to debug the plumbing.

## User stories

- As a customer, I want to place an order and be told it was received, so that I know my request went through.
- As a developer, I want one thin path through every layer, tested at every layer, so that later features only add behavior.

## Acceptance criteria

- **AC-1** Given a customer ID and at least one line with a SKU and a quantity of 1 or more, when the customer places the order, then it is accepted with status Pending and an OrderPlaced event is published for it.
- **AC-2** Given an order with a missing customer ID, no lines, a blank SKU or a quantity below 1, when it is placed, then it is rejected with one error per invalid field and no event is published. (Amended by [spec 012](../012-login-and-ownership/spec.md): the customer now comes from the login, so a request can no longer leave it out.)
- **AC-3** Given an OrderPlaced event, when the Notification service receives it, then the customer's inbox contains "We received your order".
- **AC-4** Given the full stack is running, when a customer places an order in the UI, then they see the order and, shortly after, "We received your order" in their inbox. *(Amended by spec 001: the order may already show Confirmed, so the UI status is no longer asserted here; spec 001 AC-5 covers it.)*

## Edge cases

- The same customer places two orders: both appear in the inbox (one message each).
- Notification is down when the order is placed: the event waits in the broker queue and is delivered on restart (RabbitMQ durability; not tested this week).

## Non-goals

- Stock, payment, confirmation and cancellation (specs 001 to 006).
- Persistence beyond process memory (milestone 4, week 5).
- Atomic save-and-publish (outbox, week 5).
- Idempotent consumers (spec 004).

## Open questions

- [x] Is the UI allowed to call the Notification service directly? (answer: yes, read-only fake inbox, for tests and demo only)
- [x] In-memory bus or RabbitMQ for the running stack? (answer: RabbitMQ; the in-memory transport cannot span processes. Tests use the in-memory harness. See ADR 0002.)
