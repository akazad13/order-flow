# Spec 017: Admins manage stock

- **Status:** Done
- **Milestone:** After week 8, eighth follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 001](../001-place-order/spec.md) (catalog and stock, Q4), [Spec 012](../012-login-and-ownership/spec.md) (logins), [Spec 015](../015-staff-and-token-refresh/spec.md) (roles)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Nobody at the shop can put an item on sale or change how many are in stock. The only way is `PUT /stock/{sku}`, a test-only endpoint (spec 001, Q4) that anyone who can reach the Inventory service may call, with no login at all, whenever `Inventory:EnableTestEndpoints` is on (as it is in docker compose).

## User stories

- As an admin, I want to set an item's name, price and quantity in stock, and add new items, so that customers can order what we actually have.
- As the shop, I want only admins to change stock, so that nobody else can.

## Acceptance criteria

- **AC-1 Admins set stock.** Given a login with the role `admin`, when they set an item's name, unit price and quantity available, then the item has those values (a new SKU is added to the catalog).
- **AC-2 Only admins.** Setting stock without a login answers 401; with a login that is not an admin (a customer, staff, or another role), 403. Nothing changes.
- **AC-3 Always there, never open.** Setting stock is a real endpoint: available in every environment, with no test switch; looking at the catalog stays open to everyone.
- **AC-4 The stock page.** Admins see the catalog (SKU, name, price, available) with a way to change an item and to add one; customers and staff do not see it.
- **AC-5 Orders see the change.** On the real stack, an admin puts a new item on sale through the stock page, and a customer can order it; an order for more than the admin put in stock is rejected for a shortage.
- **AC-6 Invalid values are refused.** A missing name, a negative price or a negative quantity is refused with an error per field, as before (spec 001).

## Business rules

- **R-1** "Available" is what can still be held for new orders; an admin sets it outright. Stock already held for orders is not touched (spec 001).
- **R-2** Roles are separate: an admin is not staff, and staff are not admins (a login may carry both).

## Edge cases

- An admin sets an item while orders for it are being held: the service's existing concurrency checks decide (spec 007); the admin's value or the hold is retried, never lost.
- An admin sets an item to 0: it stays in the catalog and orders for it are rejected for a shortage.

## Non-goals

- Removing items from the catalog, price history, or an audit of who changed what.
- Adjusting stock relatively ("add 10"): the admin sets the number.
- Admins managing users or roles (that is Keycloak's own admin console).

## Open questions

Decided on the user's behalf (they asked for the admin role, 2026-10-15; "manage stock" was the admin option offered when they chose staff support). Revisit in review.

- [x] **Q1 What can admins do?** (answer: manage stock: set an item's name, price and quantity, and add items.)
- [x] **Q2 The test-only endpoint?** (answer: it becomes the real one, admin-only, and the `Inventory:EnableTestEndpoints` switch goes. Tests set stock as an admin.)
- [x] **Q3 How do tests get an admin login?** (answer: component and integration tests use test-key tokens with the role (spec 012); E2E uses a dev-realm client for tests, `orderflow-e2e`, that gets tokens with its own secret (client credentials) and holds the `admin` role. The realm file is dev and CI only (ADR 0008); a production realm would not have it.)
- [x] **Q4 Admins and staff?** (answer: separate roles (R-2); demo users `ada` / `ada` (admin) next to `sam` (staff) and `alice` (customer).)
