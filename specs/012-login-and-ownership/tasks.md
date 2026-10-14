# Tasks 012: Log in, and see only your own orders

Plan: [plan.md](plan.md)

- [x] T1 ADR 0008; contracts (OpenAPI, AsyncAPI); lint.
- [x] T2 Test tokens (`tests/Shared/TestTokens.cs`); component scenarios (red).
- [x] T3 ServiceDefaults auth; Order endpoints and hub; Notification inbox; component tests green; existing tests updated to log in.
- [x] T4 Integration tests log in; green.
- [x] T5 Web: tests (red), login with oidc-client-ts, token on calls and hub; Pact regenerated; provider verification green.
- [x] T6 Keycloak in compose, realm file; E2E users and login; `login.feature`; E2E green; stress 102 of 102, no faulted messages.
- [x] T7 README, retro; spec Done; `npm run check:specs`.
