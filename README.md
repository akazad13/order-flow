# OrderFlow

A small event-driven order platform built to practice **spec-driven development**: every feature starts as a spec, every acceptance criterion ends as an automated test.

## Layout

| Path | What lives there |
| --- | --- |
| `.specify/` | Constitution (the rules) and spec/plan/tasks templates |
| `specs/NNN-name/` | One folder per feature: `spec.md`, `plan.md`, `tasks.md` |
| `contracts/` | OpenAPI (HTTP), AsyncAPI + JSON Schema (events), AsyncAPI for the live-updates hub (`realtime/`) |
| `pacts/` | Consumer-driven contract: what the web UI relies on from the Order API (Pact, ADR 0006) |
| `features/` | Gherkin: `component/` (Reqnroll) and `e2e/` (playwright-bdd) |
| `docs/adr/` | Architecture decisions |
| `docs/retro.md` | What eight weeks of SDD taught: which layer caught which bug, what is still open |
| `scripts/` | Spec gate (`check-specs.mjs`) and its tests |
| `src/` | .NET 10 services: Order, Inventory, Payment, Notification |
| `tests/` | Unit, component, contract and integration tests (.NET); `Shared/` holds helpers linked into each component test project |
| `web/` | React + TypeScript UI (types generated from OpenAPI) |
| `e2e/` | Playwright end-to-end tests |

## Prerequisites

.NET SDK 10, Node 22, Docker. Run `dotnet tool restore` once for `dotnet ef`.

## Run the tests

.NET unit, component and contract tests (fast, no Docker needed):

```bash
dotnet test OrderFlow.slnx --filter "Category!=Integration"
```

Integration tests against real PostgreSQL and RabbitMQ (Testcontainers starts them; needs Docker):

```bash
dotnet test OrderFlow.slnx --filter "Category=Integration"
```

UI tests, including the Pact consumer tests that rewrite `pacts/` (commit the change):

```bash
npm --prefix web ci && npm --prefix web test
```

The Order API verifies `pacts/` in `tests/OrderFlow.Order.PactTests`, part of the fast .NET run above.

Contract lint (OpenAPI with Spectral, AsyncAPI with the AsyncAPI CLI):

```bash
npm ci && npm run lint:contracts
```

Spec gate: specs, plans, feature files and tests agree, and no banned package is used (spec 009):

```bash
npm run check:specs
```

Contract gate: no contract change without a new version, and no breaking change without a new major version (spec 014, ADR 0009; needs Docker for oasdiff). It compares the working tree with the last commit; CI compares a pull request with its base branch:

```bash
npm run check:contracts
```

```bash
npm run check:contracts -- --base origin/main
```

The gate scripts' own tests:

```bash
npm run test:scripts
```

End-to-end, against the full stack:

```bash
docker compose up -d --build --force-recreate
```

```bash
cd e2e && npm ci && npx playwright install chromium && npm run wait && npm test
```

CI runs E2E with two copies of every service (spec 013). To do the same locally, start the stack with both compose files, and wait for the second copies too:

```bash
docker compose -f docker-compose.yml -f docker-compose.copies.yml up -d --build --force-recreate
```

```bash
cd e2e && npm run wait:copies && npm test
```

Then check that no message faulted quietly (CI does this after every E2E run):

```bash
docker compose exec -T rabbitmq rabbitmqctl list_queues name messages --formatter json | node e2e/scripts/check-error-queues.mjs -
```

A nightly workflow (`.github/workflows/nightly.yml`) repeats E2E ten times and runs the integration tests.

## Run the stack

`docker compose up -d --build --force-recreate` starts RabbitMQ and every service. Keep `--force-recreate`: without it Docker Desktop sometimes keeps old containers running after a rebuild.

| Service | URL |
| --- | --- |
| Web UI | http://localhost:5180 (log in as `alice` / `alice`, a demo customer, `sam` / `sam`, a demo member of staff who can look up and cancel any order (spec 015), or `ada` / `ada`, a demo admin who manages stock (spec 017)) |
| Keycloak (logins, spec 012) | http://localhost:8180 (admin console: admin / admin; realm `orderflow`) |
| Order API | http://localhost:5101 |
| Inventory (`GET /stock`; `PUT /stock/{sku}` for admins, spec 017) | http://localhost:5102 |
| Payment | http://localhost:5103 |
| Notification (fake inbox) | http://localhost:5104 |
| Gateway (logins and the page's API calls, spec 023) | http://localhost:5105 |
| Second copies, with `docker-compose.copies.yml` (spec 013) | http://localhost:5111 to 5114 (Order, Inventory, Payment, Notification) |
| RabbitMQ management | http://localhost:15672 (guest / guest) |
| PostgreSQL | localhost:5432 (orderflow / orderflow; databases `orders`, `inventory`, `payments`, `notifications`) |

Every service stores its state in PostgreSQL under compose (`Persistence__Provider=Postgres`) and in memory otherwise (ADR 0005, specs 007, 010, 011). After changing an EF model, add a migration:

```bash
dotnet ef migrations add <Name> -p src/OrderFlow.Order -s src/OrderFlow.Order -o Persistence/Migrations
```

For UI work, run `npm --prefix web run dev` (http://localhost:5173); it proxies `/bff` and the APIs to the gateway on 5105 (running in compose, or `dotnet run --project src/OrderFlow.Gateway`).

Customers log in through Keycloak (spec 012, ADR 0008), by way of the gateway, which keeps their tokens on the server; the page holds only an HttpOnly session cookie (spec 023, ADR 0012). The production realm (roles, the gateway's client; no users, no literal secrets) is `deploy/keycloak/orderflow-realm.json`; development adds demo users, the E2E client and local addresses from `deploy/keycloak/dev-overlay.json`, merged into `deploy/keycloak/dev/orderflow-realm.json` by `npm run build:realm` (CI checks it with `npm run check:realm`; spec 019, ADR 0011). The Order and Notification APIs and the live-updates hub need a login; tests below E2E use tokens signed by a test-only key (`tests/Shared/TestTokens.cs`), and each E2E scenario creates its own Keycloak user.

## Run the production profile

A production-ready Keycloak, run locally (spec 019, ADR 0011): its optimized build on its own PostgreSQL, HTTPS only, secrets from `deploy/prod/.env` (git-ignored), the production realm with no demo users or test client, services that fetch Keycloak's keys over HTTPS only. It uses the same ports as the development stack, so stop that first. Make a local certificate authority and certificate, and random secrets (neither is committed):

```bash
bash deploy/prod/make-certs.sh && node deploy/prod/make-env.mjs
```

```bash
docker compose -p orderflow-prod -f docker-compose.yml -f deploy/prod/docker-compose.prod.yml --env-file deploy/prod/.env up -d --build --wait
```

Only the shop (https://localhost:5443) and Keycloak (https://localhost:8443) are reachable from outside; the services, PostgreSQL and RabbitMQ only from inside, e.g. with `docker compose exec` (spec 021). The browser warns about the local certificate unless you trust `deploy/prod/certs/ca.crt`. There are no users: create one in Keycloak's admin console with the administrator from `deploy/prod/.env`. The smoke test CI runs:

```bash
cd e2e && node scripts/wait-for-stack.mjs --prod && npm run test:prod
```

## Working on a feature

1. Copy the templates from `.specify/templates/` into `specs/NNN-name/` and write `spec.md`. Resolve open questions.
2. Write `plan.md`, including the criterion-to-test-layer table.
3. Change the contracts and the Gherkin first; lint; watch the scenarios fail.
4. Work through `tasks.md`, test first. Name each test exactly in the plan's test table.
5. After changing `contracts/http`, run `npm --prefix web run gen:api` (CI fails if the types are stale).
6. Before marking the spec Done, run `npm run check:specs`.

See `specs/000-walking-skeleton/` for a complete example.
