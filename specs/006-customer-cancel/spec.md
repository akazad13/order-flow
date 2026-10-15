# Spec 006: Customer cancels a pending order

- **Status:** Done
- **Milestone:** Week 7, Live status and customer cancel
- **Builds on:** [Spec 003](../003-payment-failure/spec.md) (release on cancel), [Spec 004](../004-duplicate-events/spec.md) (decisions stick), [Spec 005](../005-live-order-status/spec.md) (status is pushed)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

A customer who changes their mind has no way out: the order runs to Confirmed or to a cancellation the shop decides. Cancelling is harder than it looks in an event-driven system, because stock and payment are being decided at the same moment, in other services, by messages that may arrive in any order. Whatever the timing, a cancelled order must end with no stock held and no money kept.

## User stories

- As a customer, I want to cancel an order that is not confirmed yet, so that I am not charged for something I no longer want.
- As the shop, I want a cancelled order's stock back on sale and any payment returned, whatever the timing.

## Acceptance criteria

- **AC-1 Cancel a pending order.** Given a Pending order, when the customer asks to cancel it, then the request is accepted, the order becomes Cancelled with the reason "customer requested", and the shop announces the cancellation once.
- **AC-2 Too late once confirmed.** Given a Confirmed order, when the customer asks to cancel it, then the request is refused with a conflict that says the order is already confirmed, and the order is unchanged. (Changed by [spec 022](../022-cancel-confirmed-orders/spec.md), after the user's review: a confirmed order can be cancelled within a window, 24 hours by default.)
- **AC-3 Already cancelled.** Asking to cancel a Cancelled order changes nothing and returns the order as it is.
- **AC-4 Stock goes back.** Stock held for the order is released once. Stock not yet held when the order was cancelled is never held for it.
- **AC-5 No money kept.** A payment not yet taken is never taken. A payment already taken is refunded once, and the refund is announced.
- **AC-6 Confirmation and cancellation race.** If the payment succeeds while the cancellation is being handled, the order ends either Confirmed or Cancelled, never both: exactly one of "confirmed" and "cancelled" is announced, and the order's status matches it.
- **AC-7 Customer is told.** The inbox shows "Your order was cancelled at your request", and after a refund "Your payment of 37.00 was refunded".
- **AC-8 Customer does it in the UI.** While the order is Pending the page offers "Cancel order". After cancelling, the customer sees the status Cancelled and the reason "you cancelled it" within 10 seconds, without reloading. (Spec 015 changed the words to "cancelled at your request" while staff cancellations shared this reason; [spec 016](../016-event-versions/spec.md) gave staff a reason of their own and restored "you cancelled it".) If it was too late, they are told the order is already confirmed.

## Business rules

- **R-1** Only the Order service's state machine changes an order's status (ADR 0004); the API only records the request.
- **R-2** A cancellation request for an order that is still Pending is accepted, even if the order is confirmed a moment later (AC-6 decides the outcome).
- **R-3** Cancelled and Confirmed stay final (spec 001, R-4; spec 002, R-3).

## Edge cases

- The request reaches the state machine before the order is tracked (its OrderPlaced is still in flight): the request waits its turn and is then handled (same retry as spec 001).
- The cancellation reaches Inventory before the order's placement does: Inventory remembers the order as released and never holds stock for it.
- The cancellation reaches Payment before the stock-held news: Payment remembers the order as void and never charges it.
- The customer clicks Cancel twice: the second request changes nothing.

## Non-goals

- Cancelling a Confirmed order (returns, partial cancellation).
- Checking that the caller owns the order (no authentication yet).
- Real refunds through a payment provider; the fake gateway records them.

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 How does the API cancel without breaking R-1?** (answer: the API checks the order, then announces `OrderCancellationRequested` and answers 202 Accepted; the state machine decides. The UI learns the outcome from the push, spec 005.)
- [x] **Q2 Who returns the money?** (answer: Payment, reacting to `OrderCancelled`: it voids an undecided payment or refunds a taken one, and announces `PaymentRefunded`. Choreography, like the stock release in spec 003.)
- [x] **Q3 Is a new cancellation reason a breaking event change?** (answer: no. `CustomerRequested` is added to `OrderCancelled` v1; consumers must treat an unknown reason generically, which Notification already does. Recorded in plan.)
- [x] **Q4 How do E2E tests keep an order Pending long enough to cancel it?** (answer: like declines in spec 003, a test-only switch makes Payment leave a customer's payments undecided.)
