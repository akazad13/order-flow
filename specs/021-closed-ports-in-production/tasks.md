# Tasks 021: Only the shop and Keycloak are reachable in production

Plan: [plan.md](plan.md)

- [x] T1 Smoke test: port check, test-only checks through the web server and from inside (red: Order answered on 5101).
- [x] T2 Close the ports; health checks (fixed to run under bash); `up --wait`; smoke 7 of 7 from scratch.
- [x] T3 CI, README, ADR 0011, retro; spec Done; gates.
