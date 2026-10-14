# Plan 006: Customer cancels a pending order

Spec: [spec.md](spec.md)

## Approach

The API records the request; the state machine decides (R-1); every other service settles its own part when it hears `OrderCancelled`, whatever arrived first.

```text
UI --POST /orders/{id}/cancellation--> Order API --OrderCancellationRequested--> state machine
   <--202 Accepted                                     | AwaitingStock / AwaitingPayment: cancel (CustomerRequested)
                                                       | Confirmed / Cancelled: ignore (too late / already done)
                                                       v
                                                 OrderCancelled --> Inventory: release Held; undecided -> Released (never hold)
                                                                --> Payment: charged -> refund + PaymentRefunded; undecided -> void (never charge)
                                                                --> Notification: "cancelled at your request"
                                                                --> Order hub: push (spec 005)
PaymentRefunded --> Notification: "Your payment of 37.00 was refunded"
```

| Request finds the order | API answers | Then |
| --- | --- | --- |
| Pending | 202 Accepted, the order as it is | `OrderCancellationRequested`; outcome pushed to the page |
| Cancelled | 200 OK, the order | nothing |
| Confirmed | 409 Conflict, problem "already confirmed" | nothing |
| unknown | 404 | nothing |

AC-6: the API can see Pending and accept while `PaymentSucceeded` is already queued. The state machine handles the two messages one at a time (pessimistic lock on the saga row in PostgreSQL), so the first one wins and the other is ignored by the final state. If confirmation wins, the request is ignored and the page receives Confirmed. If cancellation wins, the late `PaymentSucceeded` is ignored and Payment refunds the charge when it hears `OrderCancelled`.

## Services affected

| Service | Change |
| --- | --- |
| Order | `POST /orders/{orderId}/cancellation`; `OrderCancellationRequested` handled in AwaitingStock and AwaitingPayment (`CancelAtCustomerRequestActivity`), ignored in Confirmed and Cancelled, faults (and is retried) when the order is not tracked yet |
| Inventory | Release of an undecided order records Released (in memory and PostgreSQL); the consumer saves even when nothing was released |
| Payment | `FakePaymentGateway.Settle(orderId)`: refund a charge once, or void an undecided payment; `OrderCancelledConsumer` publishes `PaymentRefunded`; test-only `PUT /test/stalls/{customerId}` |
| Notification | Text for `CustomerRequested`; `PaymentRefundedConsumer` |
| Web | "Cancel order" button while Pending; "Cancelling..." until the push; conflict message; reason text "you cancelled it" |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `order.openapi.yaml` | `cancelOrder` (202, 200, 404, 409); `Problem` schema; reason `CustomerRequested` | Additive (the UI's generated union gains a value; `CANCELLATION_TEXT` is a `Record` over it, so the build fails until the UI handles it) |
| `schemas/order-cancellation-requested.v1.json` | orderId, requestedAt | New |
| `schemas/payment-refunded.v1.json` | orderId, customerId, paymentId, amount, refundedAt | New |
| `schemas/order-cancelled.v1.json` | reason adds `CustomerRequested` | Additive (Q3): consumers must handle unknown reasons generically |
| `asyncapi.yaml` | Channels `order-cancellation-requested`, `payment-refunded`; Payment receives `order-cancelled` | Additive |
| `payment.openapi.yaml` | `setStall` (test only) | New |
| `pacts/` | Cancel a pending order (202); cancel a confirmed order (409) | New interactions |

## Test plan (criterion to layer)

| Criterion | Unit | Component | Contract | Integration | Web | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AC-1 Cancel a pending order | | x | x | | | x | `customer-cancel.feature` (awaiting stock, awaiting payment, not yet tracked); Pact 202 |
| AC-2 Too late once confirmed | | x | x | | x | | `customer-cancel.feature`; Pact 409; `App.test.tsx` |
| AC-3 Already cancelled | | x | | | | | `customer-cancel.feature` |
| AC-4 Stock goes back | x | x | | x | | x | `CatalogTests`; `release-stock.feature` "cancelled before placed"; `InventoryServiceTests.Orders_cancelled_while_being_placed_end_up_holding_nothing`; E2E stock check |
| AC-5 No money kept | | x | x | | | | `settle-payment.feature` (void, refund, refund once, declined, stall); PaymentRefunded schema |
| AC-6 Race | | x | | x | | | `customer-cancel.feature` "payment succeeds after"; `OrderServiceTests.Cancellation_and_payment_racing_end_one_way` |
| AC-7 Customer told | | x | | | | x | `order-cancelled-notification.feature`, `payment-refunded-notification.feature`; E2E inbox |
| AC-8 In the UI | | | | | x | x | `App.test.tsx`; `features/e2e/customer-cancel.feature` |

## Constitution check

- [x] Spec first; [x] contracts in the same change; [x] every AC automated; [x] events past tense and versioned (Q3 for the enum value); [x] consumers idempotent (void and refund remembered per order); [x] no sleeps; [x] no banned packages

## Risks and decisions

- The fake gateway's memory of voids and refunds is in memory, like its charges (Payment has no database yet).
- A stalled customer's payments stay undecided forever; the switch is test-only, behind `Payment:EnableTestEndpoints`.
- **Found: a stock leak (AC-4).** Before this spec, Inventory ignored a cancellation for an order it had not decided, so a placement arriving after it was held forever. Red first (`CatalogTests`, `release-stock.feature`), then fixed in both stores; the consumer now saves even when nothing was released. On PostgreSQL, a placement and a cancellation inserting the same decision row at once conflict on the primary key, and the retry (23505, spec 004) settles it.
- **Found: the race test only raced one way.** The first version of the integration test sent each payment together with the request: 20 of 20 orders confirmed in every run, because the request goes through the database and the outbox before it reaches the broker. Half the payments now wait until the request is on the broker; every run since ends 10 confirmed, 10 cancelled, each order exactly one way.
- **Found: a silent lost race in the UI.** An accepted request whose order is then confirmed showed Confirmed with no explanation. The page now says it was too late (test red first).
- **The contract caught the UI.** Regenerating the types from `order.openapi.yaml` broke `tsc` (`CustomerRequested` missing from `CANCELLATION_TEXT`) until the UI had words for the new reason, as planned.
- Mutation check: answering 200 instead of 202 for a pending order fails the Pact provider verification.
