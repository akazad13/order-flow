# Spec 003: Cancel the order and release stock when payment fails

- **Status:** Done
- **Milestone:** Week 4, Unhappy paths
- **Builds on:** [Spec 001](../001-place-order/spec.md), [Spec 002](../002-stock-rejection/spec.md)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Spec 001 assumes payment always succeeds. When it is declined, stock stays held for an order that will never ship, so other customers cannot buy it, and the order is stuck.

## User stories

- As a customer whose payment was declined, I want to be told, so that I can try again with another card.
- As the shop, I want stock held for a failed order to go back on sale straight away.

## Acceptance criteria

- **AC-1 Declined payment is announced.** Given stock is held for an order and the customer's payment is declined, then the shop announces the payment failed, with the reason, and records no charge.
- **AC-2 Order is cancelled.** Given the payment failed, then the order's status becomes Cancelled with the reason "payment declined".
- **AC-3 Stock goes back.** Given an order is cancelled after its stock was held, then exactly the held quantities return to available stock, and the shop announces the release.
- **AC-4 Customer is told.** Given an order was cancelled because payment failed, then the customer's inbox contains "Your order was cancelled: your payment was declined", after "We received your order".
- **AC-5 Customer sees it.** Given the customer placed the order in the UI, then within 10 seconds they see the status Cancelled and the reason, without reloading.

## Business rules

- **R-1** Stock is released only for an order that actually held it (a shortage-cancelled order releases nothing).
- **R-2** Stock for an order is released at most once, even if the cancellation is announced twice.
- **R-3** A confirmed order is never cancelled by these flows (customer cancellation is spec 006).

## Edge cases

- The payment-failed news reaches the Order service before the stock-held news: the order is still cancelled (spec 001 showed this can happen).
- Prices or stock change between hold and release: the release returns the held quantity, not a recomputed one.

## Non-goals

- Retrying the payment, or letting the customer change card.
- Partial refunds; real payment providers.

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 How do tests make a payment fail?** (answer: outside production a test can mark a customer's payments as declined. Per customer, so parallel tests do not interfere.)
- [x] **Q2 Who releases the stock?** (answer: the stock side reacts to the order being cancelled; it keeps a record of what each order holds.)
