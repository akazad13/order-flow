# OrderFlow

A small event-driven order platform built to practice **spec-driven development**: every feature starts as a spec, every acceptance criterion ends as an automated test.

## Layout

| Path | What lives there |
| --- | --- |
| `.specify/` | Constitution (the rules) and spec/plan/tasks templates |
| `specs/NNN-name/` | One folder per feature: `spec.md`, `plan.md`, `tasks.md` |
| `contracts/` | OpenAPI (HTTP) and AsyncAPI + JSON Schema (events) |
| `features/` | Gherkin: `component/` (Reqnroll) and `e2e/` (playwright-bdd) |
| `docs/adr/` | Architecture decisions |
| `src/` | .NET 10 services: Order, Inventory, Payment, Notification |
| `tests/` | Unit, component and contract tests (.NET) |
| `web/` | React + TypeScript UI (types generated from OpenAPI) |
| `e2e/` | Playwright end-to-end tests |

## Prerequisites

.NET SDK 10, Node 22, Docker.

## Run the tests

.NET unit, component and contract tests (no Docker needed):

```bash
dotnet test OrderFlow.slnx
```

UI tests:

```bash
npm --prefix web ci && npm --prefix web test
```

Contract lint (OpenAPI with Spectral, AsyncAPI with the AsyncAPI CLI):

```bash
npm ci && npm run lint:contracts
```

End-to-end, against the full stack:

```bash
docker compose up -d --build
```

```bash
cd e2e && npm ci && npx playwright install chromium && npm run wait && npm test
```

## Run the stack

`docker compose up -d --build` starts RabbitMQ and every service.

| Service | URL |
| --- | --- |
| Web UI | http://localhost:5180 |
| Order API | http://localhost:5101 |
| Inventory | http://localhost:5102 |
| Payment | http://localhost:5103 |
| Notification (fake inbox) | http://localhost:5104 |
| RabbitMQ management | http://localhost:15672 (guest / guest) |

For UI work, run `npm --prefix web run dev` (http://localhost:5173); it proxies to the services on the ports above.

## Working on a feature

1. Copy the templates from `.specify/templates/` into `specs/NNN-name/` and write `spec.md`. Resolve open questions.
2. Write `plan.md`, including the criterion-to-test-layer table.
3. Change the contracts and the Gherkin first; lint; watch the scenarios fail.
4. Work through `tasks.md`, test first.
5. After changing `contracts/http`, run `npm --prefix web run gen:api` (CI fails if the types are stale).

See `specs/000-walking-skeleton/` for a complete example.
