# 0012. A gateway keeps the tokens; the browser holds only a cookie

- **Status:** Accepted
- **Date:** 2026-10-15
- **Spec:** [023](../../specs/023-tokens-on-the-server/spec.md)
- **Changes:** [ADR 0008](0008-keycloak-login-and-jwt.md) (the web app no longer logs in itself)

## Context

Since spec 012 the web page logged in with Keycloak itself (`oidc-client-ts`) and kept the access and refresh tokens in session storage, where any script on the page could read them. Reviewing that choice, the user wanted the tokens on the server.

## Decision

- **A backend for the web app, `OrderFlow.Gateway`** (ASP.NET): the only Keycloak client (`orderflow-gateway`, confidential). It logs customers in (OpenID Connect code flow with PKCE), keeps the tokens in a server-side session (PostgreSQL), and gives the browser one cookie: HttpOnly, SameSite=Lax, Secure in production.
- **It forwards the page's API calls and live connection** (YARP), adding the session's access token and dropping the browser's cookies. The services still check bearer tokens exactly as before (ADR 0008).
- **It renews tokens** before they expire, with the refresh token; if Keycloak refuses, the session ends and the page is told 401.
- **Cross-site requests are refused:** API calls must carry `X-CSRF: 1`; the live connection's Origin must be the shop's.
- **Sessions and the cookie's keys are shared** through PostgreSQL, so copies of the gateway are interchangeable (spec 013).

## Consequences

- No script on the page can read or leak a token; a stolen cookie works only from the shop's own pages (SameSite, `X-CSRF`) and only until the session ends.
- One more service on the path of every call, and a database table of sessions (expired ones are removed every hour since [spec 024](../../specs/024-expired-sessions-cleanup/spec.md)).
- Logging out ends the session at the gateway and at Keycloak; Keycloak can end a session too, and the gateway notices at the next renewal.
- The services and their contracts do not change; the Pact contract still describes what the Order API receives, token included.
