# OrderFlow constitution

The non-negotiable rules for this repo. Every spec, plan and PR is checked against them. Changing a rule needs an ADR.

## I. Spec first

No production code without a spec under `specs/NNN-name/`. A change in behavior starts as a change to `spec.md`. If the code and the spec disagree, the spec wins until it is amended.

## II. Contracts are code

HTTP APIs are defined in `contracts/http/*.openapi.yaml`; events in `contracts/events/asyncapi.yaml` with one JSON Schema per event. Contracts change in the same PR as the code, and CI lints them and tests the code against them. UI types are generated from the contracts, never written by hand.

## III. Every acceptance criterion is automated

Each acceptance criterion maps to at least one Gherkin scenario under `features/`, automated at the lowest layer that can prove it:

| Layer | Folder | Proves |
| --- | --- | --- |
| Unit | `tests/*.UnitTests` | Domain rules |
| Component | `features/component` + `tests/*.ComponentTests` | One service meets its criteria |
| Contract | `tests/OrderFlow.ContractTests` | Code matches the contract files |
| E2E | `features/e2e` + `e2e/` | A customer journey works on the real stack |

`plan.md` records which layer covers each criterion.

## IV. Events are facts

Events are named in the past tense, are immutable once published, and are versioned (`orderflow:<name>:v1`). A breaking change is a new version, not an edit. Consumers must be idempotent.

## V. Deterministic tests

No fixed sleeps. Async outcomes are awaited with a timeout (test harness, `expect(...).toContainText`, polling helpers). Each test owns its data (unique IDs). A flaky test is fixed or quarantined the day it is seen; CI never retries.

## VI. Dependencies

Banned NuGet packages (organization policy): Moq, AutoMapper. Use NSubstitute for test doubles and hand-written mappers (or Mapperly). MassTransit stays on v8 (see `docs/adr/0002`).
