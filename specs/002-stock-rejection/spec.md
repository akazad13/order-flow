# Spec 002: Reject an order when stock is short

- **Status:** Done
- **Milestone:** Week 4, Unhappy paths
- **Builds on:** [Spec 001](../001-place-order/spec.md)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

When an item is out of stock, or does not exist, the order is silently left Pending forever (spec 001 non-goal). The customer never learns the order failed, and support cannot tell why.

## User stories

- As a customer, I want to be told quickly when my order cannot be fulfilled, and why, so that I can order something else.
- As the shop, I want an order that cannot be fulfilled to end cleanly, holding no stock and taking no money.

## Acceptance criteria

- **AC-1 Nothing is held.** Given at least one line of an order asks for more than is available (or an unknown item), when the order is placed, then no stock is held for any line and the shop announces the shortage, listing every short line with the quantity requested and available.
- **AC-2 Order is cancelled.** Given the shortage is announced, then the order's status becomes Cancelled with the reason "out of stock" and a detail naming the short items.
- **AC-3 No payment.** Given an order was cancelled for a shortage, then the customer is never charged.
- **AC-4 Customer is told.** Given an order was cancelled for a shortage, then the customer's inbox contains "Your order was cancelled: some items are out of stock", after "We received your order".
- **AC-5 Customer sees it.** Given the customer placed the order in the UI, then within 10 seconds they see the status Cancelled and the reason, without reloading.

## Business rules

- **R-1** A shortage on one line cancels the whole order (no partial fulfilment).
- **R-2** An unknown item counts as a shortage with 0 available.
- **R-3** Cancelled is final: a cancelled order is never confirmed.

## Edge cases

- Every line is short: all are listed.
- The shortage news reaches the Order service before its own "order placed" news: the order is still cancelled.

## Non-goals

- Back-orders, suggesting alternatives, partial shipments.
- Reserving stock for a while before rejecting.

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 Cancelled or a separate Rejected status?** (answer: Cancelled, with a reason. One end state is simpler for customers; the reason tells the cases apart. Spec 003 reuses it.)
- [x] **Q2 How much detail does the customer get?** (answer: the inbox says "some items are out of stock"; the order itself carries the detail per item, shown in the UI.) (Changed by [spec 020](../020-itemised-shortage-message/spec.md), after the user's review: the inbox names each short item too.)
