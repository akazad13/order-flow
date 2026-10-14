# 0006. Consumer-driven contracts with Pact, contract file committed

- **Status:** Accepted
- **Date:** 2026-10-14
- **Spec:** [008](../../specs/008-consumer-driven-contracts/spec.md)

## Context

OpenAPI describes what the Order API offers; generated UI types keep the UI aligned with that file. Nothing checks that the running API behaves as the UI expects. Pact records the consumer's expectations and replays them against the provider.

## Decision

- Pact V4 with `@pact-foundation/pact` (web, consumer) and `PactNet` 5 (Order API, provider).
- **No Pact Broker.** The contract file lives in `pacts/` and is committed. The web job fails if its tests would change the file without it being committed; the .NET job verifies the committed file.
- Provider states via a test-only endpoint added by the provider test host, never by the service itself.
- Scope: the web UI and the Order API only. Events are covered by JSON Schema contract tests.

## Consequences

- A breaking API change fails the .NET build in the same pull request.
- Contract changes are visible in code review as a diff of `pacts/*.json`.
- Moving to separate repos or independent deploys would need a broker (and `can-i-deploy`); revisit then.
