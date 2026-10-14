# Tasks 000: Walking skeleton

Plan: [plan.md](plan.md)

## 1. Foundations

- [x] T1 Repo layout, `global.json`, `Directory.Build.props`, central package versions.
- [x] T2 Constitution and spec/plan/tasks templates under `.specify/`.
- [x] T3 ADRs 0001 to 0003.

## 2. Contracts

- [x] T4 `order.openapi.yaml`, `notification.openapi.yaml`, `asyncapi.yaml`, `order-placed.v1.json`.
- [x] T5 Gherkin: `features/component/*.feature`, `features/e2e/walking-skeleton.feature`.

## 3. Build

- [x] T6 Order domain + unit tests (AC-1, AC-2).
- [x] T7 Order API + publish OrderPlaced; component tests green (AC-1, AC-2).
- [x] T8 Contract test: OrderPlaced matches its JSON Schema.
- [x] T9 Notification consumer + fake inbox; component test green (AC-3).
- [x] T10 Inventory and Payment placeholder services.
- [x] T11 React UI with types generated from OpenAPI; Vitest tests.
- [x] T12 docker compose for RabbitMQ, services and UI.
- [x] T13 Playwright + playwright-bdd E2E (AC-4).
- [x] T14 CI workflow: contract lint, .NET tests, web tests, E2E.

## 4. Verify

- [-] T15 Push to GitHub and see CI green. (deferred: no GitHub remote yet; it needs the user's account, see docs/retro.md)
- [x] T16 Run the E2E suite 20 times in a row locally without a failure (20/20, `--repeat-each=20`, 2026-10-14).
- [x] T17 Retro note: what was unclear in this spec? Update the template if needed. (Week 8: [docs/retro.md](../../docs/retro.md); templates updated.)
