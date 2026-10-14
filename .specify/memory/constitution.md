# OrderFlow constitution

The non-negotiable rules for this repo. Every spec, plan and PR is checked against them. Changing a rule needs an ADR.

## I. Spec first

No production code without a spec under `specs/NNN-name/`. A change in behavior starts as a change to `spec.md`. If the code and the spec disagree, the spec wins until it is amended.

## II. Contracts are code

HTTP APIs are defined in `contracts/http/*.openapi.yaml`; events in `contracts/events/asyncapi.yaml` with one JSON Schema per event. Contracts change in the same PR as the code, and CI lints them and tests the code against them. Contracts follow semantic versioning: CI fails a change without a new version, and a breaking change without a new major version (`npm run check:contracts`, ADR 0009). UI types are generated from the contracts, never written by hand.

## III. Every acceptance criterion is automated

Each acceptance criterion maps to at least one Gherkin scenario under `features/`, automated at the lowest layer that can prove it:

| Layer | Folder | Proves |
| --- | --- | --- |
| Unit | `tests/*.UnitTests` | Domain rules |
| Component | `features/component` + `tests/*.ComponentTests` | One service meets its criteria |
| Contract | `tests/OrderFlow.ContractTests`, `web/src/api/*.pact.test.ts` + `tests/OrderFlow.Order.PactTests` | Code matches the contract files; the Order API honours what the UI relies on (Pact) |
| Integration | `tests/*.IntegrationTests` | Storage, outbox and broker behavior on real PostgreSQL and RabbitMQ (Testcontainers) |
| E2E | `features/e2e` + `e2e/` | A customer journey works on the real stack |

`plan.md` records which layer covers each criterion, naming each test exactly. CI checks it (`npm run check:specs`, ADR 0007).

## IV. Events are facts

Events are named in the past tense, are immutable once published, and are versioned (`orderflow:<name>:v1`). A breaking change is a new version, not an edit: readers move first, then the publisher switches, then the old version is retired (ADR 0010). Consumers must be idempotent.

## V. Deterministic tests

No fixed sleeps. Async outcomes are awaited with a timeout (test harness, `expect(...).toContainText`, polling helpers). Each test owns its data (unique IDs). A flaky test is fixed or quarantined the day it is seen; CI never retries. No message may fault quietly: after E2E, CI fails if any error or skipped queue holds a message, and a nightly run repeats E2E ten times (ADR 0007).

## VI. Dependencies

Banned NuGet packages (organization policy): Moq, AutoMapper. Use NSubstitute for test doubles and hand-written mappers (or Mapperly). MassTransit stays on v8 (see `docs/adr/0002`). EF Core packages share one version (pinned in `Directory.Packages.props`).
