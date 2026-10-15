# Tasks 023: Tokens stay on the server

Plan: [plan.md](plan.md)

- [x] T1 Gateway service: OpenID Connect login, cookie sessions, ticket stores, renewal, cross-site guard, YARP forwarding; migration.
- [x] T2 Component scenarios (12) with fake Keycloak, services and clock; mutation checks (one survived until the in-memory store kept sessions serialized).
- [x] T3 Integration test: any copy serves any session (keys isolated per copy unless shared).
- [x] T4 Realm: `orderflow-gateway` replaces `orderflow-web`; realm check allows placeholder secrets only.
- [x] T5 Web: no tokens, `X-CSRF`, 401 ends the login; nginx and Vite through the gateway; `oidc-client-ts` removed.
- [x] T6 Compose (dev, copies, production); E2E: no token, ended session; nginx precedence fixed; warm-ups; cold-start cycles 3 of 3; stress 99 of 99; production smoke 7 of 7 (five runs).
- [x] T7 ADR 0012, README, spec 015 note, retro; spec Done; gates.
