# Spec 020: The inbox names the short items

- **Status:** Done
- **Milestone:** After week 8, from the review of the decisions taken on the user's behalf ([retro](../../docs/retro.md))
- **Builds on:** [Spec 002](../002-stock-rejection/spec.md) (stock rejection; changes its Q2)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

When an order is cancelled because stock is short, the customer's inbox says only "Your order was cancelled: some items are out of stock". Which items, and how many were left, appears only on the order page. Spec 002 chose this (Q2); reviewing it, the user wants the inbox to say which items were short.

## User stories

- As a customer whose order was cancelled for a shortage, I want my inbox to tell me which items were short and how many were left, so that I can order what is available without going back to the order page.

## Acceptance criteria

- **AC-1 Each short item is named.** Given an order cancelled because stock was short, then the customer's inbox message names every short item with how many were available and how many were requested, in the same words as the order page: "Your order was cancelled: some items are out of stock. Only 1 of SKU-002 available, 3 requested."
- **AC-2 Other reasons unchanged.** Messages for a declined payment, the customer's or the support team's cancellation, and a reason the service does not know are as before.
- **AC-3 End to end.** On the real stack, a customer who orders more than is in stock reads in their inbox which item was short and how many were left.

## Business rules

- **R-1** The inbox and the order page describe a shortage in the same words: the detail the Order service writes when it cancels (spec 002).

## Edge cases

- Several items short: all are named, in the order the stock check reported them.
- A repeat of the same cancellation (spec 004): the message is the same, so the inbox still has it once (spec 011).

## Non-goals

- Item names instead of SKUs (customers order by SKU; names are not in the shortage event).
- Suggesting what to order instead.

## Open questions

- [x] **Q1 Name the short items in the inbox?** (answered by the user, 2026-10-15, reviewing spec 002's Q2: yes.)

Decided on the user's behalf (2026-10-15). Revisit in review.

- [x] **Q2 Where do the words come from?** (answer: the cancellation's detail, which Order already writes per short item and the order page shows (R-1). No contract change: `OrderCancelled` v2 carries it.)
