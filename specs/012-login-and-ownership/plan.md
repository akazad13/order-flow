# Plan 012: Log in, and see only your own orders

Spec: [spec.md](spec.md)

## Approach

Keycloak runs in compose (port 8180) with the `orderflow` realm imported from `deploy/keycloak/orderflow-realm.json`: a public client `orderflow-web` (authorization code + PKCE) whose access tokens carry the audience `orderflow-api`. The web app logs in with `oidc-client-ts` and sends the access token as a bearer token to the Order and Notification APIs, and to the hub (SignalR puts it in the `access_token` query string for WebSockets). See ADR 0008.

`OrderFlow.ServiceDefaults` gains `AddOrderFlowAuthentication()`: JWT bearer validation against the realm (signature, issuer, audience, lifetime), the subject kept as the `sub` claim, and the hub's query-string token accepted. `ClaimsPrincipal.CustomerId()` reads it (R-1).

| Request | No login | Someone else's | Own |
| --- | --- | --- | --- |
| `POST /orders` | 401 | n/a (customer from the token) | 201 |
| `GET /orders/{id}`, `POST /orders/{id}/cancellation` | 401 | 404 | as before |
| Hub connect / `WatchOrder` | 401 / refused | refused ("Order not found.") | as before |
| `GET /inbox/{customerId}` | 401 | 403 | 200 |

Inside compose the services fetch Keycloak's keys from `http://keycloak:8080`, while tokens name the issuer the browser saw, `http://localhost:8180/realms/orderflow`: `Auth:MetadataAddress` and `Auth:Issuer` are configured separately.

Tests below E2E (component, integration, Pact provider) replace the validation with a test signing key (`tests/Shared/TestTokens.cs`), and issue tokens for any customer, expired ones, or ones signed by the wrong key.

## Services affected

| Part | Change |
| --- | --- |
| ServiceDefaults | `AddOrderFlowAuthentication`, `CustomerId()` |
| Order | All `/orders` endpoints and the hub require a login; customer from the token; ownership checks (404); `PlaceOrderRequest` loses `customerId` |
| Notification | `GET /inbox/{customerId}` requires a login and the same customer (403) |
| Web | `oidc-client-ts` login, logout, who is logged in; bearer token on every API call and on the hub; customer ID field removed |
| Compose | `keycloak` service, realm import; services' auth settings |
| E2E | Users created per scenario via the admin API; login through Keycloak's page; `wait` waits for Keycloak |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `order.openapi.yaml` | Bearer security on every operation; 401; `PlaceOrderRequest` without `customerId`; 404 also for another customer's order | Yes (Q2): one client, changed in the same commit, checked by Pact |
| `notification.openapi.yaml` | Bearer security; 401, 403 | Yes (needs a login now) |
| `realtime/order-updates.asyncapi.yaml` | Connecting needs a login (`access_token`); `WatchOrder` refuses another customer's order | Yes |
| `pacts/` | Every interaction sends a bearer token; place-order body without `customerId` | Regenerated |

## Test plan (criterion to layer)

| Criterion | Component | Contract | Web | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Log in first | x | | | x | `order-access.feature`; `inbox-access.feature`; `live-order-updates.feature` "needs a login"; `features/e2e/login.feature` |
| AC-2 Orders belong to whoever placed them | x | x | | | `order-access.feature`; `order-api.pact.test.ts` (no customer in the request) |
| AC-3 Only the owner sees an order | x | | | | `order-access.feature` (read, cancel); `live-order-updates.feature` "another customer's order" |
| AC-4 Only your own inbox | x | | | | `inbox-access.feature` |
| AC-5 Logins are checked properly | x | | | | `order-access.feature` (expired, wrong key, wrong issuer, wrong audience) |
| AC-6 Log in and out in the UI | | | x | x | `App.test.tsx`; `features/e2e/login.feature` |
| AC-7 The real login, end to end | | | | x | every `features/e2e` scenario, 100 runs |
| R-1 Customer from the login | x | x | | | `order-access.feature`; Pact |
| R-2 Not yours looks like not there | x | | | | `order-access.feature` (same answer as an unknown order) |

## Constitution check

- [x] Spec first; [x] contracts in the same change (breaking, Q2); [x] every AC automated; [x] no new events; [x] no sleeps; [x] no banned packages; [x] ADR 0008 for the identity choice

## Risks and decisions

- Keycloak's dev mode (`start-dev`, admin `admin`/`admin` from the compose file) is for local and CI only.
- Two issuers problem (inside vs. outside compose) handled by configuration, not by turning issuer validation off. Checked on the running stack: inside compose the realm's metadata names the browser's issuer (`http://localhost:8180/...`) and the internal key URL (`http://keycloak:8080/...`).
- **Red first.** Before any service checked logins, the new Notification scenarios were the only failures there (2 of 12); on Order every scenario failed, as expected once placing an order needs a login.
- **Mutation checks:** without the ownership comparison, the three "another customer" scenarios fail (read, cancel, watch); without the inbox's same-customer check, "Not someone else's inbox" fails.
- **Found during build: the hub's token.** The .NET SignalR client sends its token as a header, and leaves the `access_token` query string to the WebSocket factory, so the hub scenarios first got 401. The test factory now puts the token in the query string, as browsers must, which exercises the server's query-string path.
- **On the real stack:** every E2E scenario creates its own Keycloak user and logs in through the real login page; 102 of 102 runs passed, with no faulted or skipped messages.
- **Found during build: the Pact test runs in Node.** The browser login library was created when `auth.ts` was imported, and `window` does not exist in Node, so the whole contract file failed to load (the run still reported the App tests as passing; CI would have failed on the suite error). The login manager is now created on first use.
