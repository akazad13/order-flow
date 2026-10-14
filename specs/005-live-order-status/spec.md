# Spec 005: See order status change live

- **Status:** Done
- **Milestone:** Week 7, Live status and customer cancel
- **Builds on:** [Spec 001](../001-place-order/spec.md) (Q3: poll until this spec)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The UI learns that an order is Confirmed or Cancelled by re-reading it every second (spec 001, Q3). Every open order page costs the Order API one request a second, mostly to hear "still Pending", and the customer waits up to a second longer than needed. The Order service already knows the moment an order's status changes; it should tell the page.

## User stories

- As a customer, I want my order's status to change on screen as soon as it changes, without reloading.
- As the shop, I want open order pages to cost nothing while nothing happens.

## Acceptance criteria

- **AC-1 Pushed on change.** Given a client watching an order, when the order becomes Confirmed or Cancelled, then the client is sent the order, with exactly the fields and values that reading the order (`GET /orders/{orderId}`) returns.
- **AC-2 Only that order.** A client watching one order is sent nothing about other orders.
- **AC-3 Nothing missed.** A change that happened before the client started watching, or while its connection was down, still reaches the screen: the UI reads the order once each time it starts (or restarts) watching.
- **AC-4 No polling.** While watching, the UI does not re-read the order on a timer.
- **AC-5 Never backwards.** The UI never replaces a final status (Confirmed, Cancelled) with Pending, even if a slow read answers after a push.
- **AC-6 Customer sees it live.** On the real stack the customer sees Confirmed within 10 seconds of placing the order, and the page read the order at most once after placing it.

## Edge cases

- The order is final before the client starts watching: the one read after watching shows it (AC-3).
- WebSockets are blocked by a proxy: the client falls back to another transport (SignalR negotiates one), with no change in behavior.
- Two pushes for the same change (an event delivered twice, spec 004): the screen shows the same thing twice; harmless.

## Non-goals

- Pushing the inbox: it stays polled (the Notification service would need its own hub).
- Pushing intermediate changes (the total appearing while Pending): only final statuses are pushed.
- Authorization: anyone who knows an order ID can watch it, as anyone can read it today.
- Several Order service instances (a SignalR backplane such as Redis).

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 Where does the hub live?** (answer: in the Order service, the owner of order status, at `/hubs/orders`.)
- [x] **Q2 When is the push sent?** (answer: only after the new status is committed. The Order service listens to its own `OrderConfirmed` and `OrderCancelled`, which the outbox delivers after commit, reads the order and pushes it. Pushing from inside the state machine could show a status whose transaction then rolls back.)
- [x] **Q3 What is the contract?** (answer: an AsyncAPI document for the hub, `contracts/realtime/order-updates.asyncapi.yaml`; the payload is the `Order` schema from `order.openapi.yaml`, so the UI reuses its generated `Order` type.)
