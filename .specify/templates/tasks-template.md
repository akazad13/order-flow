# Tasks NNN: <feature name>

Plan: [plan.md](plan.md)

Each task is small (under 2 hours) and ends with a test going green. Write the failing scenario or test first.
A task that cannot be done here is marked `- [-] Tn ... (deferred: reason)`; a Done spec has no `- [ ]` left.

## 1. Contracts

- [ ] T1 Update the contract file(s) and lint them (`npm run lint:contracts`).
- [ ] T2 Add or update the Gherkin scenarios; run them and watch them fail.

## 2. Build

- [ ] T3 ...

## 3. Verify

- [ ] Tn All layers green locally (`dotnet test`, `npm test` in web/, E2E against docker compose).
- [ ] Tn+1 Spec status set to Done; `npm run check:specs` passes.
