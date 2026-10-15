# Plan 023: Tokens stay on the server

Spec: [spec.md](spec.md)

## Approach

A new service, `OrderFlow.Gateway` (ADR 0012), between the web server and the APIs:

| Concern | How |
| --- | --- |
| Login (AC-2) | ASP.NET's OpenID Connect handler: code flow with PKCE, confidential client `orderflow-gateway`, back to `/bff/signin-oidc` with a GET (`response_mode=query`). `GET /bff/login`, `POST /bff/logout` (returns Keycloak's logout address), `GET /bff/user` (name, staff, admin; never a token) |
| The browser's share (AC-1) | One cookie, `orderflow.session`: HttpOnly, SameSite=Lax, path `/`, Secure in production. It holds a session key; the ticket with the tokens is in the session store |
| Sessions (AC-6) | `ITicketStore`: PostgreSQL (`sessions`) in compose, in memory otherwise (kept serialized, like PostgreSQL); the cookie's data protection keys in the same database |
| Forwarding (AC-3) | YARP routes `/order-api`, `/notification-api`, `/inventory-api` (and the live connection) to the services; a transform adds `Authorization: Bearer` with the session's token and removes the browser's cookies |
| Renewal (AC-4) | `TokenRefresher` before each forwarded call: renew with the refresh token at Keycloak's token endpoint when the token expires within `Gateway:RefreshBefore` (1 minute; in development 20 minutes, so every call renews); store the new tokens; if Keycloak refuses, end the session and answer 401 |
| Cross-site (AC-5) | `X-CSRF: 1` required on API calls; the live connection's Origin must be one of `Gateway:AllowedOrigins` |

nginx still serves the page and now proxies `/bff` and the three API prefixes to the gateway (same origin; Q4). The web app drops `oidc-client-ts`: it asks `/bff/user`, sends `X-CSRF: 1`, treats 401 as "the login ended", and opens the live connection without a token. The services are unchanged (R-2).

## Services affected

| Area | Change |
| --- | --- |
| Gateway (new) | `src/OrderFlow.Gateway`: options, BFF endpoints, cross-site guard, session tokens and renewal, ticket stores, `GatewayDbContext` and migration |
| Keycloak realm | `orderflow-gateway` (confidential, secret `${ORDERFLOW_GATEWAY_SECRET}`) replaces `orderflow-web`; the dev overlay gives a fixed dev secret; the realm check allows a secret only as such a placeholder |
| Web | `auth.ts` without tokens; `X-CSRF` and 401 handling in the API clients; live connection without a token; nginx (dev and production) and Vite proxy to the gateway; `oidc-client-ts` removed |
| Compose | `gateway` (5105) and `gateway-2` (5115); production: HTTPS metadata, secret from the env file, Secure cookie, no published port, health check |
| E2E | Login scenarios: no token in the page; an ended session asks to log in; warm-ups before the suite (Risks) |
| CI | Migrations check for the gateway |

## Contract changes

None for the services' APIs. The gateway's `/bff` endpoints are the page's own (one client, same origin) and are covered by its component scenarios.

## Test plan (criterion to layer)

| Criterion | Unit | Component | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 No tokens in the browser | x | x | x | x | `gateway.feature` "The page learns who is logged in, and never a token", "The session cookie cannot be read by scripts, nor sent by other sites"; `GatewaySessionTests.Any_copy_serves_any_session_and_the_tokens_stay_in_the_database`; `clients.test.ts`; `login.feature` "The page holds no token, only a cookie its scripts cannot read" |
| AC-2 Logging in and out | | x | | x | `gateway.feature` "Logging in starts Keycloak's login, with PKCE", "Logging out ends the session"; `login.feature` "A visitor logs in through the shop's login page, then logs out" |
| AC-3 Calls carry the token, added on the server | | x | | x | `gateway.feature` "A call is forwarded with the session's access token", "Without a session, calls are refused"; every E2E scenario |
| AC-4 The gateway renews | x | x | | x | `gateway.feature` "A token about to expire is renewed first", "A renewal that fails ends the session"; `clients.test.ts`; `login.feature` "A login that ended at Keycloak asks to log in again"; every E2E scenario (renewal on every call in development) |
| AC-5 Cross-site requests are refused | x | x | | x | `gateway.feature` "A call without the page's header is refused", "The live connection only from the shop's own origin"; `clients.test.ts`; `smoke.spec.ts` (403 without the header) |
| AC-6 Several copies | | | x | x | `GatewaySessionTests.Any_copy_serves_any_session_and_the_tokens_stay_in_the_database`; E2E with `gateway-2` |
| AC-7 Production | | x | | x | `gateway.feature` "In production, the session cookie goes over HTTPS only"; `smoke.spec.ts` (login through the gateway, no token, Secure cookie, the client's secret from the env file) |
| AC-8 Everything else unchanged | | | | x | All E2E scenarios with `docker-compose.copies.yml`, 99 runs |
| R-1 The browser never receives a token | x | x | x | x | as AC-1 |
| R-2 The services do not change | | | x | x | All existing service tests unchanged and green |

## Constitution check

- [x] Spec first; [x] the services' contracts unchanged; [x] every AC automated; [x] no sleeps (warm-ups wait on conditions); [x] no banned packages (YARP and the ASP.NET packages are Microsoft's); [x] no secret committed (placeholder in the production realm, dev overlay only for development)

## Risks and decisions

- **Mutation checks** (the gateway was written before its scenarios ran): without the cross-site guard, the two cross-site scenarios fail; without stripping the browser's cookie, the forwarding scenario fails; without the GET return from Keycloak, the login scenario fails; without the database-shared cookie keys, the integration test fails.
- **Found: a session store that hid a bug.** Not saving a renewed session still passed at first: the in-memory store returned the very object it was given, so changing the tokens in place looked saved. It now keeps sessions serialized, like PostgreSQL, and the scenario catches it.
- **Found: the integration test passed for the wrong reason.** Without shared keys, two copies in one process still read each other's cookies, because ASP.NET's default key store is the developer's user profile; separate containers would not. The test gives each copy private keys unless the gateway shares them.
- **Found: a test-only cookie path.** The test sign-in under `app.Map` put `path=/test/sign-in` on the cookie; the gateway now sets path `/` itself, so it never depends on where a login ends.
- **Found: nginx lost the WebSocket upgrade.** The regex location for the three APIs won over the hub's prefix location, so the live connection reached the gateway as a plain GET (400 from SignalR, 373 times in one run). `location ^~ /order-api/hubs/` fixes the precedence.
- **Found: Keycloak returned with a cross-site POST** (`form_post`, ASP.NET's default). It worked only because Keycloak and the shop share `localhost`; with separate domains, the Lax login cookies would not travel. Now a GET.
- **Found: the first runs after a stack start failed** (first logins and first calls slow; 2 to 6 scenarios). Two warm-ups now run before the suite: `wait-for-stack.mjs` pushes one order through the services, and Playwright's global setup logs in once through the gateway (a first version of it failed to create its user: my bug). Three cold-start cycles: 11 of 11 each.
- **Found: a spec 019 smoke test racing Playwright's 30-second test timeout** during a Keycloak restart; that test now has three minutes. Five runs, 7 of 7.
- **Renewal on every call in development** costs one Keycloak request per call; it makes every E2E scenario exercise the real renewal. Production renews a minute before expiry.
- **Sessions are not cleaned up** when they expire (rows stay in `sessions`); a cleanup job is a follow-up (done in [spec 024](../024-expired-sessions-cleanup/spec.md)).
