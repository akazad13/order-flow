# Tasks 019: A production-ready Keycloak

Plan: [plan.md](plan.md)

- [x] T1 Realm builder and its checks (red: the realm had demo users, the E2E client and its secret).
- [x] T2 Split the realm: production file, development overlay, generated development realm (identical to the old realm).
- [x] T3 Production image, certificate and secrets scripts, production compose profile, HTTPS web server.
- [x] T4 Smoke test (6 of 6); mutation: without the local CA, Order cannot validate logins and the order test fails.
- [x] T5 Development stack on the generated realm: full E2E with two copies 10 of 10; stress 100 of 100, no faulted messages (after restarting the web server, see plan).
- [x] T6 CI (`check:realm`, `prod-profile` job), ADR 0011, README, retro; spec Done; gates.
