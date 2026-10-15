# Spec 015: Staff support orders, and logins renew themselves

- **Status:** Done
- **Milestone:** After week 8, sixth follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 006](../006-customer-cancel/spec.md) (cancel a pending order), [Spec 012](../012-login-and-ownership/spec.md) (logins, ownership; ADR 0008), [Spec 014](../014-contract-compatibility/spec.md) (contract versions)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Since spec 012 everyone who logs in is a customer, and a customer sees only their own orders. When a customer calls the shop ("please cancel my order"), nobody at the shop can look the order up or act on it. And a login lasts only as long as its access token: when the token expires, the page keeps sending it and every call fails, though the customer's session at the identity server is still good.

## User stories

- As a member of staff, I want to look up any order by its ID and cancel it on the customer's behalf, so that I can help a customer who calls.
- As a customer, I want to see that support cancelled my order at my request, so that I know what happened.
- As a customer, I want to stay logged in while I use the shop, without logging in again every few minutes.

## Acceptance criteria

- **AC-1 Staff read any order.** Given an order placed by a customer, when a member of staff reads it, then they get the order (customers still get 404 for someone else's order, spec 012).
- **AC-2 Staff cancel on the customer's behalf.** Given a Pending order, when a member of staff cancels it, then it is cancelled for the reason "at the customer's request", with the detail "Cancelled by our support team at the customer's request.", and the customer is told as for any cancellation. A Confirmed order answers 409, as for the customer.
- **AC-3 Staff watch any order.** A member of staff can watch any order's live updates.
- **AC-4 Only the identity server makes staff.** A login without the `staff` role, including one with some other role, is a customer: someone else's order still looks like no order at all.
- **AC-5 The staff page.** Staff see an order lookup (by order ID) with the order's status and a cancel button; customers do not.
- **AC-6 Logins renew themselves.** Given a logged-in customer, when their access token is about to expire, then the page gets a new one without asking them, and goes on working with it (calls and live updates).
- **AC-7 An ended session asks to log in again.** Given the customer's session has ended at the identity server, when the page next needs a new token, then it shows the login prompt instead of failing calls.
- **AC-8 End to end.** On the real stack, a member of staff logs in, looks up a customer's pending order and cancels it, and the customer's page shows it cancelled with the support team's words.

## Business rules

- **R-1** Staff act for customers; they do not become them. A staff cancellation is still "at the customer's request" (the only reason a staff member cancels, Q3), and the order still belongs to the customer.
- **R-2** A member of staff who places an order is its customer, like anyone else.

## Edge cases

- Staff cancel an order the customer is watching: the customer's page shows the cancellation as soon as it happens (spec 005).
- Staff and the customer cancel at the same moment: one request wins (spec 006, R-1); the detail says who.
- A cancellation request already on the broker during the deploy has no "requested by": it is the customer's.
- The token is renewed while a live connection is open: the connection keeps working; a reconnect uses the new token.

## Non-goals

- Staff reading a customer's inbox, an admin role, managing stock.
- Recording which member of staff acted (the customer is told "our support team"; the services log it).
- A production Keycloak (HTTPS, a database, secrets; ADR 0008).

## Open questions

- [x] **Q1 What can staff do?** (answered by the user, 2026-10-15: support orders: look up any order and cancel it on the customer's behalf.)

Decided on the user's behalf (they asked to start this follow-up, 2026-10-15). Revisit in review.

- [x] **Q2 Where does the role come from?** (answer: a Keycloak realm role, `staff`, put in the tokens as a flat `roles` claim by a mapper on the web client; the services read `roles` as the role claim.)
- [x] **Q3 A new cancellation reason for staff?** (answer: no. A new value in `OrderCancelled.reason` is a breaking edit of a published event (constitution IV, spec 014) and would need a v2 of the event. The reason stays "customer requested"; the detail names the support team; `OrderCancellationRequested` gains an optional `requestedBy`, a compatible change.)
- [x] **Q4 How are tokens renewed?** (answer: with the refresh token Keycloak already issues, by `oidc-client-ts`'s automatic silent renew, a minute before expiry. No hidden iframe, which browsers increasingly block for third-party cookies.) (Changed by [spec 023](../023-tokens-on-the-server/spec.md), after the user's review: the gateway keeps and renews the tokens on the server; the page holds none.)
