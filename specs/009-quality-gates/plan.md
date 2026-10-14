# Plan 009: Quality gates in CI

Spec: [spec.md](spec.md)

## Approach

One dependency-free Node script, `scripts/check-specs.mjs`, reads `specs/`, `features/`, `tests/`, `web/`, `e2e/` and the .NET project files and reports every broken rule (AC-1 to AC-5) in one run. It exports `check(root)` so its tests build small fake repos in a temp folder. A second script, `e2e/scripts/check-error-queues.mjs`, asks the RabbitMQ management API for queues ending in `_error` or `_skipped` that hold messages (AC-6). A scheduled workflow runs the stability suite (AC-7).

## Services affected

None. Tooling and CI only.

## Contract changes

None.

## Test plan (criterion to layer)

| Criterion | Script test | CI | Scenario / test |
| --- | --- | --- | --- |
| AC-1 Every criterion has a test | x | x | `check-specs.test.mjs` (missing row, row with no layer) |
| AC-2 Named tests exist | x | x | `check-specs.test.mjs` (missing feature, class, method, file) |
| AC-3 Features point at real criteria | x | x | `check-specs.test.mjs` (unknown spec, unknown criterion, missing tag) |
| AC-4 Done means done | x | x | `check-specs.test.mjs` (open task, deferred without reason) |
| AC-5 Banned packages | x | x | `check-specs.test.mjs` (Moq in a project file) |
| AC-6 No hidden faults | x | x | `check-error-queues.test.mjs` (queue list with and without faults) |
| AC-7 Stability on a schedule | | x | `.github/workflows/nightly.yml` |
| AC-8 The gates are tested | x | x | `npm run test:scripts` in CI |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated (AC-7 by the workflow itself); [x] no sleeps; [x] no banned packages

## Risks and decisions

- The checker parses Markdown tables by convention (first cell starts with the criterion ID, an `x` marks a layer). The plan template already uses that shape.
- **First run on the repo: 16 problems.** Plans 000, 006 and 007 named tests that never existed (class names from drafts, a renamed feature file); spec 008 had no layer columns; spec 002's AC-3 ("no payment") had no test, only an argument; spec 000 was Done with two open tasks. Fixed: names corrected; a Payment scenario "a shortage is never charged"; spec 008's AC-5 (a manual mutation once) is now an automated test that breaks the API on purpose (`OrderApiPactVerificationTests.Renaming_a_field_the_ui_reads_fails_verification`); T15 deferred with its reason, T17 closed by the retro.
- **The management API lags.** Its queue counts refresh every 5 seconds, so a fault near the end of a run could be missed. CI reads live counts with `rabbitmqctl` instead; checked by publishing a malformed message on the local stack (both error queues reported on the first read, clean right after purging).
