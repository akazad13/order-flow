# 0007. Quality gates in CI

- **Status:** Accepted
- **Date:** 2026-10-14
- **Spec:** [009](../../specs/009-quality-gates/spec.md)

## Context

The constitution's rules (every criterion automated, tests named in plans, no banned packages, no hidden faults) were enforced by people. By week 8 they had drifted: plans named tests that never existed and one criterion had no test. A message can also fault into an error queue while every E2E scenario still passes.

## Decision

- `npm run check:specs` (`scripts/check-specs.mjs`) runs in the contracts job and fails on: a Done spec's criterion with no test-plan row or no layer; a test, feature or file named in a test plan that does not exist; a feature file whose `# Spec:` header names unknown specs or criteria, or lacks the `@spec-NNN` tag; an open or unexplained deferred task in a Done spec; a Moq or AutoMapper reference.
- After E2E, CI fails if any RabbitMQ `_error` or `_skipped` queue holds a message, read live with `rabbitmqctl` (`e2e/scripts/check-error-queues.mjs`).
- A nightly workflow runs E2E ten times over and the integration tests, with the same fault check. CI itself never retries.
- The gate scripts have their own tests (`npm run test:scripts`), also in CI.

## Consequences

- Plans must name tests exactly; renaming a test means updating its plan in the same change.
- A deferred task needs a written reason: `- [-] Tn ... (deferred: reason)`.
- Markdown conventions (criterion IDs, `x` for a layer, backticked names) are now load-bearing; the templates describe them.
