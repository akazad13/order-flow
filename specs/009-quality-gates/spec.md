# Spec 009: Quality gates in CI

- **Status:** Done
- **Milestone:** Week 8, CI gates and retro
- **Builds on:** the constitution (III, V, VI), [ADR 0006](../../docs/adr/0006-pact-contract-file-in-repo.md)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The constitution says every acceptance criterion is automated, contracts and code agree, and no fault is hidden. CI already proves the code works, but the links between specs, plans and tests are checked only by people, and they have drifted: spec 000 is Done with open tasks, spec 007's plan names test classes that never existed, and spec 006's plan names a feature file by its draft name. Nothing would notice a plan row with no test at all, or a message that failed quietly into an error queue while the E2E scenario still passed.

## User stories

- As a developer, I want CI to fail when a spec, its plan and its tests disagree, so that "Done" means the same thing every time.
- As a reviewer, I want to trust that a criterion's test exists without opening every file.
- As the team, I want slow-burning flakiness found on a schedule, not in someone's pull request.

## Acceptance criteria

- **AC-1 Every criterion has a test.** For a spec marked Done, every acceptance criterion has a row in its plan's test plan with at least one layer marked. Otherwise CI fails and names the spec and criterion.
- **AC-2 Named tests exist.** Every feature file, test class, test method and test file a test plan names exists in the repo. Otherwise CI fails and names the plan and the missing reference.
- **AC-3 Features point at real criteria.** Every feature file's `# Spec:` header names existing specs and criteria, and the file carries the matching `@spec-NNN` tag. Otherwise CI fails.
- **AC-4 Done means done.** A spec marked Done has no open task. A task may be marked deferred (`- [-]`) only with a reason.
- **AC-5 Banned packages.** A reference to a banned package (Moq, AutoMapper) anywhere in the .NET build fails CI.
- **AC-6 No hidden faults.** After the E2E run, any message in a RabbitMQ error or skipped queue fails CI and names the queue.
- **AC-7 Stability on a schedule.** Every night CI runs the E2E suite 10 times over and the integration tests once, with the same no-hidden-faults check, and keeps the reports.
- **AC-8 The gates are tested.** Each rule above that runs as a script has a test showing it passes a clean repo and fails the broken case.

## Non-goals

- Detecting breaking changes between contract versions (e.g. oasdiff): noted for later in the retro.
- Code coverage thresholds: the constitution measures criteria covered, not lines.
- Branch protection settings: they live in GitHub, which this repo is not pushed to yet.

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 Script language?** (answer: Node, no dependencies, `node:test` for its tests. Node is already required for contracts, web and E2E.)
- [x] **Q2 How strict is "a reference exists"?** (answer: backticked names in test-plan rows: `*.feature` by file name under `features/`; `Class` or `Class.Method` in the .NET tests; `*.ts`/`*.tsx` files under `web/` or `e2e/`. Prose is not checked.)
- [x] **Q3 What about tasks that cannot be done here (pushing to GitHub)?** (answer: deferred with a reason, `- [-] T15 ... (deferred: ...)`, which AC-4 allows.)
