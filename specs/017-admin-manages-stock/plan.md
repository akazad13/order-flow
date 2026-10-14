# Plan 017: Admins manage stock

Spec: [spec.md](spec.md)

## Approach

`PUT /stock/{sku}` stops being a test-only endpoint behind `Inventory:EnableTestEndpoints` and becomes the real one, mapped always and protected by a role: `RequireAuthorization(policy => policy.RequireRole("admin"))`, so no login answers 401 and any other login 403. Inventory gains the same JWT validation as Order and Notification (`AddOrderFlowAuthentication`, ADR 0008); the catalog (`GET /stock`, `GET /stock/{sku}`) stays open. Keycloak gets the realm role `admin`, written into tokens by the existing `roles` mapper (spec 015).

The web app shows admins a stock page (`StockAdmin`): the catalog as a table, an "Edit" button per item that fills a form, and the same form to add an item; it calls Inventory through a new `/inventory-api` proxy (nginx and Vite), with types generated from `inventory.openapi.yaml`.

Tests set stock as an admin: component and integration tests with test-key tokens carrying the role (`TestTokens.Admin`); E2E through the API with a token from `orderflow-e2e`, a dev-realm client that gets tokens with its own secret (client credentials) and holds `admin` (Q3), and through the stock page with a fresh admin user.

## Services affected

| Area | Change |
| --- | --- |
| Keycloak realm | Role `admin`; demo user `ada` / `ada`; client `orderflow-e2e` with its service account in `admin` (dev and CI only) |
| ServiceDefaults | `AdminRole` |
| Inventory | Logins; `PUT /stock/{sku}` always mapped, admin-only; `Inventory:EnableTestEndpoints` removed (settings, compose) |
| Web | `/inventory-api` proxy; generated Inventory types; `listStock`, `setStock`; `isAdmin`; `StockAdmin` |
| Tests | Admin logins wherever stock is set; E2E fixture `shopPage` (was `staffPage`) for anyone working at the shop |

## Contract changes

| Contract | Change | Version |
| --- | --- | --- |
| `http/inventory.openapi.yaml` | `PUT /stock/{sku}` needs a bearer token with the role `admin`; 401 and 403; no longer "test only" | 1.0.0 to 2.0.0 (the contract gate: a login newly required is breaking) |

## Test plan (criterion to layer)

| Criterion | Unit | Component | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Admins set stock | x | x | x | `manage-stock.feature` "An admin changes an item", "An admin adds an item"; `StockAdmin.test.tsx`; `admin-stock.feature` |
| AC-2 Only admins | | x | | `manage-stock.feature` "Only admins set stock" (no login 401; customer, staff, role "Admin" 403) |
| AC-3 Always there, never open | | x | | `manage-stock.feature` (the test host has no switch; the catalog is read without a login); `hold-stock.feature` (stock set as an admin) |
| AC-4 The stock page | x | | x | `StockAdmin.test.tsx`; `App.test.tsx` "shows the stock page to admins only"; `admin-stock.feature` |
| AC-5 Orders see the change | | | x | `admin-stock.feature` (3 of 2 rejected for a shortage, 2 of 2 confirmed, the page then shows 0) |
| AC-6 Invalid values are refused | x | x | | `manage-stock.feature` "Invalid values are refused"; `StockAdmin.test.tsx` "shows the errors the service gives" |
| R-1 Available is set outright | | x | x | `manage-stock.feature`; `admin-stock.feature` |
| R-2 Roles are separate | x | x | | `manage-stock.feature` (staff get 403); `App.test.tsx` (staff do not see the stock page) |

## Constitution check

- [x] Spec first; [x] contract changed with a new major version (spec 014 gate); [x] every AC automated; [x] no sleeps; [x] no banned packages; [x] no test-only switch left in Inventory

## Risks and decisions

- **Red first.** With the test switch still on (Development settings), the four "Only admins" examples failed: anyone could set stock, without a login.
- **Mutation check:** with the policy asking for `staff` instead of `admin`, all 18 Inventory scenarios fail (setting up the catalog needs an admin).
- **A secret in the realm file.** `orderflow-e2e` has a fixed secret, like the dev admin's password (ADR 0008): the realm file is for dev and CI only.
- **Found: recreating Keycloak changes its signing keys.** The first E2E run after recreating Keycloak (for the new role) got 401 from Order and Notification, which had kept the old keys; JWT validation fetches new keys on an unknown key at most once per refresh interval (5 minutes by default). Inventory, just restarted, was fine. Dev only (a real Keycloak rotates keys with overlap); after recreating Keycloak, restart the services or wait. Then 100 of 100.
- **Seen once: `Twenty_orders_for_ten_items_hold_exactly_ten` timed out** in a full .NET run while Docker images were building on the same machine (80 s instead of 10 s; its wait is 60 s). Three repeats of the project without that load: 5 of 5 each. Not changed; it would show up in the nightly run if it were more than load.
