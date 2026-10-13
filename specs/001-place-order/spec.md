# Spec 001: Place an order and see it confirmed

- **Status:** Done
- **Milestone:** Weeks 2 to 3, Place order end to end
- **Builds on:** [Spec 000](../000-walking-skeleton/spec.md) (order accepted as Pending, "We received your order")
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Today an order is accepted and then nothing happens: it stays Pending forever. A customer needs to know whether the shop can actually fulfil the order and has been paid, and the shop needs stock held for the order so it is not sold twice.

This spec covers the **happy path only**: everything is in stock and payment succeeds. Shortages and failed payments are specs 002 and 003.

## User stories

- As a customer, I want my order confirmed once the items are held for me and I have paid, so that I know it will be delivered.
- As a customer, I want to see my order move from Pending to Confirmed without reloading, so that I am not left wondering.
- As the shop, I want stock held for an order before taking payment, so that I never charge for items I cannot ship.

## Acceptance criteria

- **AC-1 Stock is held.** Given every item in an order is in stock, when the order is placed, then the ordered quantity of each item is held for that order and the available stock drops by the same amount.
- **AC-2 Payment follows the hold.** Given the stock for an order is held, when payment is requested, then the customer is charged the order total exactly once.
- **AC-3 Order is confirmed.** Given payment for an order succeeded, then the order's status becomes Confirmed and the shop announces that the order is confirmed.
- **AC-4 Customer is told.** Given an order is confirmed, then the customer's inbox contains "Your order is confirmed" for that order, after "We received your order".
- **AC-5 Customer sees it.** Given the customer placed an order in the UI, then the status they see changes from Pending to Confirmed within 10 seconds, without reloading the page.
- **AC-6 Order total.** Given an order, its total is the sum over its lines of quantity times the item's unit price, and the customer sees the total once the order is confirmed.

## Business rules

- **R-1** Stock is held all or nothing: either every line of the order is held, or none is (partial holds are spec 002).
- **R-2** Payment is never requested for an order whose stock is not fully held.
- **R-3** An order is Confirmed only after both the stock hold and the payment have succeeded, in that order.
- **R-4** Status only moves forward: Pending, then Confirmed. A Confirmed order never goes back to Pending.

## Edge cases

- Two orders for the same item placed at the same moment, with enough stock for both: both are confirmed and stock drops by the sum.
- An order with two lines for the same SKU: treated as one line with the summed quantity.
- The customer reloads the page while the order is Pending: they still see the order and its current status.
- The customer's messages arrive out of order (confirmation before receipt): the inbox still lists them in the order things happened. *(Added during build: found by the E2E stability run.)*
- The news that payment succeeded arrives before the news that stock is held (possible because different services send them): the order is still confirmed. *(Added during build: the E2E stability run found 1 failure in 40.)*

## Non-goals

- Out of stock, partial stock (spec 002).
- Payment failure, releasing held stock (spec 003).
- Duplicate or out-of-order events (spec 004).
- Push updates to the UI (spec 005); this spec only needs the change to appear without a manual reload.
- Customer cancellation (spec 006).
- Real money, currencies other than one shop currency, taxes, shipping costs.
- Durable storage (week 5); state may be lost when a service restarts.

## Open questions

Resolved 2026-10-14. Q1 is a business question for this spec; Q2 to Q4 decide how the plan meets it.

- [x] **Q1 Prices.** Where does an item's unit price come from? (answer: the shop's catalog, kept with the stock. The price is fixed when the stock is held; the customer never supplies a price.)
- [x] **Q2 Coordination.** Does the Order service direct each step (commands), or does each service react to the previous service's event? (answer: each service reacts to the previous event; the Order service tracks the order's progress and decides when it is Confirmed.)
- [x] **Q3 Status in the UI.** How does the UI learn the order is Confirmed before SignalR arrives in spec 005? (answer: the UI re-reads the order every second while it is Pending.)
- [x] **Q4 Stock in tests.** How do tests and the demo get known stock levels? (answer: a few items are stocked at startup, and outside production a test can set the stock of any item.)
