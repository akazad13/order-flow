# Spec 022: Cancel a confirmed order, for a while

- **Status:** Done
- **Milestone:** After week 8, from the review of the decisions taken on the user's behalf ([retro](../../docs/retro.md))
- **Builds on:** [Spec 006](../006-customer-cancel/spec.md) (cancel a pending order; changes its AC-2), [Spec 015](../015-staff-and-token-refresh/spec.md) (staff cancel for the customer), [Spec 016](../016-event-versions/spec.md) (`OrderCancelled` v2)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Once an order is confirmed, nobody can cancel it: the customer is told "too late" (spec 006, AC-2), even a minute after paying. Reviewing that rule, the user wants confirmed orders to be cancellable too, for a set time after they were confirmed, with the money refunded and the stock put back.

## User stories

- As a customer, I want to cancel an order I have just paid for, and get my money back, so that a mistake is not final at once.
- As the shop, I want a cut-off, after which a confirmed order stays confirmed.

## Acceptance criteria

- **AC-1 Within the window.** Given an order confirmed less than the cancellation window ago (24 hours unless configured otherwise), when the customer (or staff for them) cancels it, then it is cancelled, the payment is refunded once, the stock is released, and the customer is told about both, as for any cancellation.
- **AC-2 After the window.** Given an order confirmed longer ago than the window, when someone asks to cancel it, then the request is refused as too late (409) and nothing changes.
- **AC-3 A slow request still counts.** A cancellation asked for within the window is honoured even if it is handled after the window has passed (the time it was asked for decides).
- **AC-4 The page knows.** A confirmed order says until when it can be cancelled; the customer's page and the staff page offer to cancel it until then, and say when it is too late.
- **AC-5 Pending orders as before.** A pending order can be cancelled at any time, as in spec 006.
- **AC-6 End to end.** On the real stack, a customer whose order was confirmed cancels it, sees it cancelled, gets the stock back and is told about the refund.

## Business rules

- **R-1** A confirmed order can be cancelled until its confirmation time plus the cancellation window; after that it is final. A cancelled order is always final.
- **R-2** The deadline is fixed when the order is confirmed: changing the window later does not move it for orders already confirmed.

## Edge cases

- The customer cancels at the moment the deadline passes: the time of the request decides (AC-3).
- The payment succeeds and the customer cancels at the same moment (spec 006, AC-6): either the order is cancelled while pending, or it is confirmed and then cancelled within the window; either way it ends cancelled and refunded once.
- A repeat of the cancellation request: the order is already cancelled; nothing more happens (spec 004).

## Non-goals

- Shipping (the deadline stands in for it), partial cancellation, or a fee.
- A different window per customer or per item.

## Open questions

- [x] **Q1 Cancel confirmed orders?** (answered by the user, 2026-10-15, reviewing spec 006: yes.)
- [x] **Q2 Until when?** (answered by the user, 2026-10-15: for a set time after confirmation, a setting.)

Decided on the user's behalf (2026-10-15). Revisit in review.

- [x] **Q3 How long by default?** (answer: 24 hours, `Orders:CancelWindow`.)
- [x] **Q4 Where is the deadline kept?** (answer: on the order (shown as `cancellableUntil`) and in the state machine's record, both set when the order is confirmed (R-2); the state machine compares it with the request's time (AC-3).)
- [x] **Q5 New events or reasons?** (answer: none. A confirmed order's cancellation is announced like any other (`OrderCancelled` v2, reason `CustomerRequested` or `StaffRequested`); Payment already refunds a taken charge and Inventory already releases held stock on any cancellation.)
