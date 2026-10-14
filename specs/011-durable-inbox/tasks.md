# Tasks 011: The customer's inbox survives a restart

Plan: [plan.md](plan.md)

- [x] T1 Move the handled-message counter to `OrderFlow.IntegrationTesting`; Payment tests still green.
- [x] T2 Notification integration tests (red against the in-memory inbox).
- [x] T3 Async inbox, `EfFakeInbox`, migration, retry; component and integration tests green.
- [x] T4 Compose and CI; E2E green; stress 100; no faulted messages.
- [x] T5 Retro, README; spec Done; `npm run check:specs`.
