# Plan 022: Cancel a confirmed order, for a while

Spec: [spec.md](spec.md)

## Approach

When the state machine confirms an order (`ConfirmOrderActivity`), it fixes the deadline: confirmation time plus `Orders:CancelWindow` (24 hours by default), on the order (`Order.CancellableUntil`, shown as `cancellableUntil`) and in the state machine's record (`OrderState.CancellableUntil`) (R-2). Then:

| Where | Pending | Confirmed, request by the deadline | Confirmed, later | Cancelled |
| --- | --- | --- | --- | --- |
| API `POST /orders/{id}/cancellation` | 202 | 202 | 409 "Too late to cancel." | 200 (unchanged) |
| State machine on `OrderCancellationRequested` | cancel | cancel (the request's `RequestedAt` decides, AC-3) | ignore | ignore |

Cancelling a confirmed order publishes `OrderCancelled` v2 like any other cancellation; Payment refunds the charge and Inventory releases the stock, as they already did for any cancellation (Q5). The pages (customer, staff) offer to cancel while `canCancel(order)` (Pending, or Confirmed before `cancellableUntil`) and keep watching the order until then; "too late" shows only when the API says so.

## Services affected

| Area | Change |
| --- | --- |
| Order | `OrderOptions.CancelWindow`; `Order.Confirm(at, window)`, `CancellableUntil`, `CanBeCancelledAt`, `Cancel(..., requestedAt)`; `OrderState.CancellableUntil`; state machine and API rules above; migration `AddCancellableUntil` |
| Web | `canCancel` (`src/orders.ts`); customer and staff pages; status order Pending, Confirmed, Cancelled |
| Tests | Fake clock in the Order component tests (`Microsoft.Extensions.TimeProvider.Testing` 10.10.0); Pact provider state for an order confirmed too long ago |

## Contract changes

| Contract | Change | Version |
| --- | --- | --- |
| `http/order.openapi.yaml` | `Order.cancellableUntil` (optional, nullable date-time); 409 is "too late" | 2.0.0 to 2.1.0 |
| `realtime/order-updates.asyncapi.yaml` | The same, through the shared `Order` schema | 2.0.0 to 2.1.0 |
| `pacts/orderflow-web-orderflow-order-api.json` | "a request to cancel a confirmed order" becomes "... an order confirmed too long ago" | regenerated |

## Test plan (criterion to layer)

| Criterion | Unit | Component | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Within the window | x | x | x | x | `OrderTests.A_confirmed_order_can_be_cancelled_until_its_deadline`; `cancel-confirmed-order.feature` "Cancelling within the window", "Staff cancel a confirmed order for the customer within the window"; `OrderServiceTests.Cancellation_and_payment_racing_end_one_way`; `customer-cancel.feature` (E2E: refund, stock back, inbox) |
| AC-2 After the window | x | x | | | `cancel-confirmed-order.feature` "Too late once the window has passed"; `customer-cancel.feature`, `staff-orders.feature` (too late after 25 hours); `order-api.pact.test.ts` |
| AC-3 A slow request still counts | x | x | | | `cancel-confirmed-order.feature` "A request made in time counts, even if it is handled late"; `App.test.tsx` "keeps cancelling when the payment wins the race" |
| AC-4 The page knows | x | x | | x | `cancel-confirmed-order.feature` "A confirmed order says until when it can be cancelled"; `App.test.tsx` "offers to cancel a confirmed order until its deadline, then not"; `customer-cancel.feature` (E2E) |
| AC-5 Pending orders as before | | x | | x | `customer-cancel.feature` (component and E2E) |
| AC-6 End to end | | | | x | `customer-cancel.feature` "Customer cancels a confirmed order within the window, and is refunded" |
| R-1 Until the deadline, then final | x | x | | | `OrderTests`; `cancel-confirmed-order.feature` |
| R-2 The deadline is fixed at confirmation | x | | | | `OrderTests.A_confirmed_order_can_be_cancelled_until_its_deadline` (a second confirmation with another window keeps the first deadline) |

## Constitution check

- [x] Spec first; [x] contracts changed compatibly, minor versions (spec 014 gate); [x] no new events or reasons (IV); [x] every AC automated; [x] no sleeps (a fake clock); [x] no banned packages

## Risks and decisions

- **Code before scenarios, so mutations instead of red first.** With the state machine never honouring a request for a confirmed order, the three "within the window" scenarios fail; with the API skipping the deadline check, the three "too late" scenarios fail. (A first API mutation, `when false`, did not compile, so it proved nothing; redone with a condition that compiles.)
- **The contract gate caught a required field.** `cancellableUntil` was first added as required; for the live updates that is breaking (an older message would not have it), though oasdiff accepted it for HTTP responses. It is optional; the service always sends it.
- **Found: a Pact file keeps renamed interactions.** The consumer test merges into the committed pact, so after renaming the "too late" interaction the old one stayed, and the provider rightly answered it with 202. The pact was regenerated from scratch.
- **Found: `docker compose up --build` built new images without recreating the containers** (as in spec 020). The E2E run then exercised the old Order. Recreated explicitly, and checked each container's image ID against the new image: the spec 022 scenarios, the suite 11 of 11, the stress run 99 of 99.
- **The race in spec 006's integration test changed meaning.** A cancellation and a payment at the same moment now always end Cancelled (and refunded once if the payment won), because a confirmed order can still be cancelled; the test asserts exactly that.
- **Orders confirmed before spec 022** have no deadline and stay final.
