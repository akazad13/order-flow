# 0008. Customers log in with Keycloak; services check JWTs

- **Status:** Accepted
- **Date:** 2026-10-14
- **Spec:** [012](../../specs/012-login-and-ownership/spec.md)

## Context

Nobody logged in: the order form took any customer ID, and any order or inbox could be read by anyone who knew its ID. The user chose a real OpenID Connect identity server over a home-made token issuer or ASP.NET Identity accounts.

## Decision

- **Keycloak** in docker compose (`quay.io/keycloak/keycloak:26.4.2`, `start-dev`), realm `orderflow` imported from `deploy/keycloak/orderflow-realm.json`.
- **The web app** is a public client, `orderflow-web`, using the authorization code flow with PKCE (`oidc-client-ts`). Access tokens carry the audience `orderflow-api`.
- **Services** validate JWTs themselves (`AddOrderFlowAuthentication` in ServiceDefaults): signature from the realm's keys, issuer, audience, lifetime. The customer is the token's `sub`; no API takes a customer ID from the client any more.
- **Two addresses for one issuer:** in compose, services fetch keys from `http://keycloak:8080`, while tokens name the issuer the browser used, `http://localhost:8180/realms/orderflow`. Both are configured (`Auth:MetadataAddress`, `Auth:Issuer`); issuer validation stays on.
- **Tests below E2E** swap the validation for a test signing key in the test host. Services have no setting that accepts test tokens, so there is no back door to switch on by mistake.
- **E2E** creates a user per scenario through Keycloak's admin API and logs in through the real login page.

## Consequences

- Placing an order no longer takes a customer ID: a breaking API change, made while the web UI is the only client and Pact checks it.
- One more container, and E2E depends on Keycloak's login page markup (labels "Username or email", "Password", button "Sign In").
- `start-dev` with `admin`/`admin` is for local and CI only; production would need a real Keycloak setup (HTTPS, a database, secrets).
- Service-to-service messages over RabbitMQ stay unauthenticated (non-goal).
