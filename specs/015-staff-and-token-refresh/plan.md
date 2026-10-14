# Plan 015: Staff support orders, and logins renew themselves

Spec: [spec.md](spec.md)

## Approach

**Staff.** Keycloak gives the realm role `staff`; a protocol mapper on the `orderflow-web` client writes realm roles into the access and ID tokens as a flat `roles` claim, and the services read `roles` as the role claim (`IsStaff()` in ServiceDefaults). In the Order service, `FindOwnAsync` becomes `FindAllowedAsync`: the order if it is the caller's, or any order for staff. Reading, cancelling and watching all go through it, so staff get all three and customers keep spec 012's 404. A cancellation request now says who asked (`requestedBy`: Customer or Staff; Staff only when the order is not the caller's own, R-2); the state machine cancels for the same reason either way, with the support team's words in the detail for staff.

**Token renewal.** `oidc-client-ts` renews with the refresh token a minute before the access token expires (`automaticSilentRenew`). The page used to copy the token once, at login, so it kept sending the old one after a renewal; now every renewal replaces it (`userLoaded`), and a renewal that fails or a token that expires ends the login (`silentRenewError`, `accessTokenExpired`), which the page shows as the login prompt (`onLoginChanged`). API calls and the hub read the token on every request and (re)connection.

**Staff page.** `StaffLookup`, shown to staff only: look up an order by ID, see its status and customer, cancel it for the customer; a Pending order is watched, like on the customer's page.

## Services affected

| Area | Change |
| --- | --- |
| Keycloak realm | Realm role `staff`; mapper "realm roles as roles" on `orderflow-web`; demo staff user `sam` / `sam` |
| ServiceDefaults | `RoleClaimType = "roles"`; `StaffRole`, `IsStaff()` |
| Order | `FindAllowedAsync` (endpoints, hub); `requestedBy` on cancellation requests; staff detail in `CancelAtCustomerRequestActivity` |
| Contracts (.NET) | `OrderCancellationRequested.RequestedBy` (optional), `CancellationRequesters` |
| Web | `auth.ts`: renewal, `isStaff`, `onLoginChanged`; `StaffLookup`; the customer's reason text becomes "cancelled at your request" (true whoever pressed the button) |
| E2E | Staff user creation with roles, ending a session, a second browser for staff, Playwright's page clock |

## Contract changes

| Contract | Change | Version |
| --- | --- | --- |
| `events/asyncapi.yaml` | `OrderCancellationRequested.requestedBy`, optional (`Customer`, `Staff`, or absent) | 1.0.0 to 1.1.0; the contract gate (spec 014) passes it |
| `http/order.openapi.yaml` | Descriptions: staff may read and cancel any order | 1.0.0 to 1.1.0 |
| `realtime/order-updates.asyncapi.yaml` | Descriptions: staff may watch any order | 1.0.0 to 1.1.0 |

## Test plan (criterion to layer)

| Criterion | Unit | Component | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Staff read any order | | x | | `staff-orders.feature` "Staff read any customer's order" |
| AC-2 Staff cancel on the customer's behalf | | x | x | `staff-orders.feature` "Staff cancel a pending order on the customer's behalf", "Too late for staff too"; `staff-support.feature`; `EventSchemaContractTests` |
| AC-3 Staff watch any order | | x | | `staff-orders.feature` "Staff watch any customer's order" |
| AC-4 Only the identity server makes staff | | x | | `staff-orders.feature` "Only the staff role makes staff" (admin, Staff, staff-member); `order-access.feature` (no role) |
| AC-5 The staff page | x | | x | `StaffLookup.test.tsx`; `App.test.tsx` "shows the order lookup to staff only"; `staff-support.feature` |
| AC-6 Logins renew themselves | | | x | `login.feature` "The login renews itself before the token expires" |
| AC-7 An ended session asks to log in again | x | | x | `App.test.tsx` "asks to log in again when the login ends on its own"; `login.feature` "A login that ended at Keycloak asks to log in again" |
| AC-8 End to end | | | x | `staff-support.feature` |
| R-1 Staff act for customers | | x | x | `staff-orders.feature` (order stays the customer's; reason CustomerRequested); `staff-support.feature` |
| R-2 A staff member's own order is theirs | x | x | | `staff-orders.feature` "A staff member's own order is theirs"; `App.test.tsx` |

## Constitution check

- [x] Spec first; [x] contracts changed compatibly, versions raised (spec 014 gate); [x] no published event edited (II, IV); [x] every AC automated; [x] no sleeps (the page clock is moved, not waited on); [x] no banned packages

## Risks and decisions

- **Red first.** Component: the four "staff can" scenarios failed (404, and no push for the watcher); the "only the staff role" ones already passed, as they should. E2E: see T5.
- **Mutation checks:** without the staff exception in `FindAllowedAsync`, the four staff scenarios fail; with the customer's detail for staff cancellations, the staff cancel scenario fails.
- **The page clock, not real time.** Tokens last 15 minutes. The renewal scenarios move the browser's clock (Playwright `page.clock`) to 14:10, where renewal is due but expiry is not (one jump fires each due timer once), then past the first token's expiry. Keycloak runs on real time, so these scenarios check what the page does (renews, uses the new token, or asks to log in); that the services refuse an expired token is spec 012's.
- **A realm change needs Keycloak recreated.** `--import-realm` skips a realm that exists; in dev mode its data lives in the container, so recreating it re-imports (and forgets users created since). CI always starts fresh.
- **Event schemas are closed** (`additionalProperties: false`), so an old reader validating strictly would reject `requestedBy`. Our consumers do not validate on read (System.Text.Json ignores unknown fields), which is what the contract gate's "an optional field is compatible" assumes.
