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
| Web UI | http://localhost:5180 (log in as `alice` / `alice`, a demo customer, or `sam` / `sam`, a demo member of staff who can look up and cancel any order; spec 015) |
| Keycloak (logins, spec 012) | http://localhost:8180 (admin console: admin / admin; realm `orderflow`) |
| Order API | http://localhost:5101 |
| Inventory (`GET /stock`; `PUT /stock/{sku}` for tests) | http://localhost:5102 |
| Payment | http://localhost:5103 |
| Notification (fake inbox) | http://localhost:5104 |
| Second copies, with `docker-compose.copies.yml` (spec 013) | http://localhost:5111 to 5114 (Order, Inventory, Payment, Notification) |
| RabbitMQ management | http://localhost:15672 (guest / guest) |
| PostgreSQL | localhost:5432 (orderflow / orderflow; databases `orders`, `inventory`, `payments`, `notifications`) |

Every service stores its state in PostgreSQL under compose (`Persistence__Provider=Postgres`) and in memory otherwise (ADR 0005, specs 007, 010, 011). After changing an EF model, add a migration:

```bash
dotnet ef migrations add <Name> -p src/OrderFlow.Order -s src/OrderFlow.Order -o Persistence/Migrations
```

For UI work, run `npm --prefix web run dev` (http://localhost:5173); it proxies to the services on the ports above.

Customers log in through Keycloak (spec 012, ADR 0008); the realm, its web client and the demo user are in `deploy/keycloak/orderflow-realm.json`. The Order and Notification APIs and the live-updates hub need a login; tests below E2E use tokens signed by a test-only key (`tests/Shared/TestTokens.cs`), and each E2E scenario creates its own Keycloak user.

## Working on a feature

1. Copy the templates from `.specify/templates/` into `specs/NNN-name/` and write `spec.md`. Resolve open questions.
2. Write `plan.md`, including the criterion-to-test-layer table.
3. Change the contracts and the Gherkin first; lint; watch the scenarios fail.
4. Work through `tasks.md`, test first. Name each test exactly in the plan's test table.
5. After changing `contracts/http`, run `npm --prefix web run gen:api` (CI fails if the types are stale).
6. Before marking the spec Done, run `npm run check:specs`.

See `specs/000-walking-skeleton/` for a complete example.
