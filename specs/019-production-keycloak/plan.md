# Plan 019: A production-ready Keycloak

Spec: [spec.md](spec.md)

## Approach

**One realm, two uses (Q2).** `deploy/keycloak/orderflow-realm.json` becomes the production realm: roles, the web client and its mappers, no users, no test client, no secrets; the web client's addresses are `${ORDERFLOW_WEB_URL}`, which Keycloak resolves from its environment on import. `deploy/keycloak/dev-overlay.json` holds what only development needs: the demo users, the E2E client, the local addresses. `scripts/build-dev-realm.mjs` merges them into `deploy/keycloak/dev/orderflow-realm.json` (committed), which the development stack imports; `npm run check:realm` fails if that file is stale or the production realm gains a user, another client, a secret or a plain-HTTP address.

**The production profile.** `deploy/prod/docker-compose.prod.yml`, on top of `docker-compose.yml`:

| Part | Development | Production profile |
| --- | --- | --- |
| Keycloak | `start-dev`, H2 in the container, HTTP 8180 | optimized build (`deploy/keycloak/Dockerfile`), `start --optimized`, PostgreSQL `keycloak-db`, HTTPS 8443 only |
| Realm | generated development realm | the production realm, web address from `ORDERFLOW_WEB_URL` |
| Administrator | `admin` / `admin` | from `deploy/prod/.env` (git-ignored; `make-env.mjs` makes random passwords; `.env.example` committed) |
| Certificate | none | local CA and server certificate from `make-certs.sh` (git-ignored) |
| Services | `RequireHttpsMetadata=false`, HTTP metadata | `RequireHttpsMetadata=true`, metadata over HTTPS, the local CA trusted through `SSL_CERT_FILE` |
| Web | HTTP 5180 | HTTPS 5443 (`web/nginx.prod.conf`), Keycloak's HTTPS address built in (`VITE_AUTH_AUTHORITY`) |
| Payment test switches | on | off |

**Proof.** `e2e/prod/smoke.spec.ts` (`npm run test:prod`), against the running production profile; a CI job `prod-profile` makes certificates and secrets, starts the profile, waits for it over HTTPS (`wait-for-stack.mjs --prod`, which verifies the certificate against the local CA) and runs it.

## Services affected

| Area | Change |
| --- | --- |
| Keycloak | Production realm, development overlay, generated development realm; production image |
| Compose | Development imports the generated realm; production profile override |
| Web | `nginx.prod.conf`; `VITE_AUTH_AUTHORITY` build argument |
| Scripts | `build-dev-realm.mjs` and tests; `make-certs.sh`, `make-env.mjs`; `wait-for-stack.mjs --prod` |
| E2E | `playwright.prod.config.ts`, `prod/smoke.spec.ts` |
| CI | `check:realm` in the contracts job; the `prod-profile` job |
| Services | No code change: the same settings, given production values |

## Contract changes

None.

## Test plan (criterion to layer)

| Criterion | Unit | Smoke | E2E | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 One realm, two uses | x | x | | `build-dev-realm.test.mjs`; `npm run check:realm`; `smoke.spec.ts` "The production realm has no demo users and no test client" |
| AC-2 Production mode on a real database | | x | | `smoke.spec.ts` "Keycloak keeps its realm and users in its own PostgreSQL, across a restart" |
| AC-3 HTTPS only | | x | | `smoke.spec.ts` "Keycloak answers only over HTTPS"; "A customer logs in through the real page over HTTPS" |
| AC-4 Secrets stay out of git | x | x | | `smoke.spec.ts` "Keycloak's administrator comes from the secrets file"; `build-dev-realm.test.mjs` (no secret in the production realm) |
| AC-5 The services insist on HTTPS | | x | | `smoke.spec.ts` "A customer logs in through the real page over HTTPS and an order is confirmed" |
| AC-6 Nothing test-only | | x | | `smoke.spec.ts` "Nothing test-only is switched on" |
| AC-7 Development and CI unchanged | | | x | All E2E scenarios with `docker-compose.copies.yml` and the generated realm, 100 runs |
| AC-8 Proven in CI | | x | | `.github/workflows/ci.yml` job `prod-profile` |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] no sleeps (polling with a deadline); [x] no banned packages; [x] no secret committed (R-2)

## Risks and decisions

- **Red first.** The repository check failed before the split: the realm had three demo users, the E2E client and its secret.
- **First run, green; then a mutation.** The smoke test passed 6 of 6 on its first run (and Keycloak's log showed "Profile prod activated", listening on HTTPS only). With Order restarted to trust only the system's certificate authorities, it could no longer fetch Keycloak's keys and the order test failed; restored, 6 of 6 again.
- **Found: restarting services under a running web server.** Back on the development stack, the first E2E run passed only 6 of 10 (then 47 of 100): half the requests to the Order and Inventory APIs got 404. The services had been restarted after the web server (to pick up Keycloak's new keys, spec 017), their addresses moved, and nginx, which looks them up once (spec 013, non-goal), sent some requests to the wrong service. Restarting the web server fixed it: 10 of 10, then 100 of 100. Order of restarts: Keycloak, the services, then the web server.
- **The generated development realm equals the old realm** (compared key by key, apart from the web client's display name), so development behaves exactly as before.
