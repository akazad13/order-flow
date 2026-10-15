# Spec 023: Tokens stay on the server

- **Status:** Done
- **Milestone:** After week 8, from the review of the decisions taken on the user's behalf ([retro](../../docs/retro.md))
- **Builds on:** [Spec 012](../012-login-and-ownership/spec.md) (logins), [Spec 015](../015-staff-and-token-refresh/spec.md) (roles, token renewal; changes its Q4), [Spec 019](../019-production-keycloak/spec.md) (the production realm and profile)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The web page logs in itself (spec 012) and keeps the access token and the refresh token in the browser's session storage (spec 015). Any script that gets into the page, through a bug or a compromised dependency, can read them and use them elsewhere; the refresh token keeps working for the length of the session. Reviewing spec 015's Q4, the user wants the tokens kept on the server, with the browser holding only a cookie that scripts cannot read.

## User stories

- As a customer, I want my login to be unusable by any script on the page, so that a flaw in the page cannot leak it.
- As the shop, I want one place that holds the tokens and adds them to calls to the services, so that the services keep checking the same tokens as today.

## Acceptance criteria

- **AC-1 No tokens in the browser.** After logging in, no access, refresh or ID token is reachable by the page's scripts: not in storage, not in readable cookies, not in any response the page reads. The browser holds only a session cookie that scripts cannot read (HttpOnly), sent only to the shop (SameSite), and, in production, only over HTTPS (Secure).
- **AC-2 Logging in and out.** "Log in" goes through Keycloak's real page and back; the page then shows who is logged in (name, staff, admin). "Log out" ends the session at the shop and at Keycloak.
- **AC-3 Calls carry the token, added on the server.** The page's calls to the Order, Notification and Inventory APIs, and the live connection, go through the gateway, which adds the session's access token; the services check it as before (specs 012, 015, 017). Without a session, a call answers 401.
- **AC-4 The gateway renews.** Before the access token expires, the gateway renews it with the refresh token, and stores the new tokens in the session. If renewing fails (the session ended at Keycloak), the session ends, the call answers 401, and the page asks to log in again (as spec 015, AC-7).
- **AC-5 Cross-site requests are refused.** An API call through the gateway must carry the header the page sends (`X-CSRF: 1`); a live connection must come from the shop's own origin. Otherwise 403, even with the session cookie.
- **AC-6 Several copies.** Sessions are kept in PostgreSQL, and the keys that protect the cookie are shared, so any copy of the gateway serves any session (spec 013).
- **AC-7 Production.** In the production profile the gateway is a confidential client whose secret comes from the environment; the cookie is Secure; the smoke test logs in through the gateway and finds no token in the browser.
- **AC-8 Everything else unchanged.** The full E2E suite passes 100 runs in a row with two copies of every service, logging in through the gateway.

## Business rules

- **R-1** The browser never receives a token. Only the gateway talks to Keycloak's token endpoint.
- **R-2** The services do not change: they still check a bearer token on every call (ADR 0008).

## Edge cases

- Two calls need a renewal at the same moment: both may renew; the later tokens win, and Keycloak accepts either refresh while the session lasts.
- The session cookie is sent but the session is gone (expired, logged out on another tab, the gateway's database reset): 401, and the page asks to log in.
- The page is opened while logged in at Keycloak but without a session at the shop: it asks to log in; "Log in" returns at once without a password (Keycloak's single sign-on).

## Non-goals

- Logging out every session of a user from the shop ("log out everywhere"), or showing active sessions.
- Changing how the services check tokens.

## Open questions

- [x] **Q1 Keep tokens on the server?** (answered by the user, 2026-10-15, reviewing spec 015's Q4: yes.)
- [x] **Q2 How?** (answered by the user, 2026-10-15: a new .NET gateway (backend for the web app): OpenID Connect login, sessions in PostgreSQL, a secure HttpOnly cookie, YARP forwarding API calls and the live connection with the token added.)

Decided on the user's behalf (2026-10-15). Revisit in review.

- [x] **Q3 Which Keycloak client?** (answer: a new confidential client, `orderflow-gateway`, replaces the public `orderflow-web`; its secret is `${ORDERFLOW_GATEWAY_SECRET}` in the production realm and a fixed development secret in the development overlay. The realm check allows a secret only as such a placeholder.)
- [x] **Q4 How is the page served?** (answer: nginx still serves the page and proxies `/bff`, `/order-api`, `/notification-api` and `/inventory-api` to the gateway, so everything stays on one origin.)
- [x] **Q5 Cross-site protection?** (answer: SameSite=Lax cookies, plus a required `X-CSRF: 1` header on API calls (a cross-site form cannot set one); the live connection, which cannot carry custom headers, is checked by its Origin.)
