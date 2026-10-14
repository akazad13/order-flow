# Tasks 014: Catch breaking contract changes

Plan: [plan.md](plan.md)

- [x] T1 Try the tools on real history: oasdiff finds spec 012's change (with a login under-rated); `asyncapi diff` cannot classify payload changes.
- [x] T2 `scripts/check-contracts.mjs`: HTTP via oasdiff, AsyncAPI rules, version rules, constitution IV for events; tests.
- [x] T3 Run over the repository's history (AC-6).
- [x] T4 Every contract to 1.0.0; the gate passes it.
- [x] T5 CI: full history, gate against the base branch or previous commit.
- [x] T6 ADR 0009, constitution II, README, retro; spec Done; `npm run check:specs`, `npm run test:scripts`.
