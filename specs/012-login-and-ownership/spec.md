# Spec 012: Log in, and see only your own orders

- **Status:** Done
- **Milestone:** After week 8, third follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 000](../000-walking-skeleton/spec.md) (amends AC-2), [Spec 005](../005-live-order-status/spec.md), [Spec 006](../006-customer-cancel/spec.md)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Nobody logs in. A customer types any customer ID into the order form, and anyone who knows or guesses an order ID can read that order, watch it, or cancel it; anyone can read any customer's inbox. The shop cannot tell customers apart, so "my orders" means nothing.

## User stories

- As a customer, I want to log in, so that my orders and messages are mine.
- As a customer, I want nobody else to see, watch or cancel my orders, or read my messages.

## Acceptance criteria

- **AC-1 Log in first.** Placing, reading, watching and cancelling orders, and reading an inbox, need a valid login; without one the answer is 401. Health checks and the catalog stay open; test-only endpoints are unchanged (they exist only when their switch is on).
- **AC-2 Orders belong to whoever placed them.** An order is recorded for the logged-in customer. The request does not name a customer, so nobody can order as someone else.
- **AC-3 Only the owner sees an order.** Reading or cancelling another customer's order answers 404, exactly as for an order that does not exist; watching it is refused the same way.
- **AC-4 Only your own inbox.** Reading another customer's inbox answers 403.
- **AC-5 Logins are checked properly.** A token that is expired, signed by anyone else, issued by anyone else, or meant for another audience is rejected with 401.
- **AC-6 Log in and out in the UI.** A visitor sees "Log in" and no order form. Logging in goes through the shop's login page and back; the page then shows who is logged in and the order form, without a customer ID field. "Log out" ends the session.
- **AC-7 The real login, end to end.** Every E2E scenario logs in as its own new customer through the real login page; all scenarios pass, 100 runs in a row.

## Business rules

- **R-1** The customer is whoever the login says (the token's subject), never a value the client sends.
- **R-2** Not yours looks like not there for orders (404), so order IDs reveal nothing.

## Edge cases

- The login expires while an order is being watched: the page's next request fails; the customer logs in again (no silent refresh in this spec).
- An order ID guessed by another customer: 404 for read and cancel; the hub refuses to watch it.

## Non-goals

- Registration, password reset, social logins: the identity server can do these later; tests create users directly.
- Roles (staff, admin), and protecting Inventory's catalog reads.
- Silent token refresh.
- Service-to-service authentication over RabbitMQ.

## Open questions

Q1 was answered by the user (2026-10-14). The rest were decided on the user's behalf. Revisit in review.

- [x] **Q1 Who logs customers in?** (answer, from the user: Keycloak, an OpenID Connect identity server, in docker compose, with the realm committed in the repo.)
- [x] **Q2 Who is the customer?** (answer: the token's subject. `customerId` leaves the place-order request: a breaking change to the Order API, made now while the web UI is its only client and Pact checks it. Spec 000's AC-2 case "missing customer ID" no longer exists.)
- [x] **Q3 Someone else's order or inbox?** (answer: 404 for orders, so IDs reveal nothing; 403 for an inbox, whose existence is no secret.)
- [x] **Q4 How do tests below E2E log in?** (answer: test hosts swap the token check for one that trusts a test-only signing key. The services have no switch for it, so no back door exists outside tests.)
- [x] **Q5 E2E users?** (answer: each scenario creates its own user through Keycloak's admin API, then logs in through the real login page; fresh data per test, constitution V.)
