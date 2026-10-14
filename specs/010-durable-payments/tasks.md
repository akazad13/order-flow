# Tasks 010: Payment decisions survive a restart

Plan: [plan.md](plan.md)

- [x] T1 Integration test project; restart tests (red against today's in-memory Payment).
- [x] T2 Split provider and ledger; in-memory ledger; component tests green.
- [x] T3 `PaymentDbContext`, `EfPaymentLedger`, migration, outbox and retry; integration tests green.
- [x] T4 Concurrency test (AC-3, AC-4) green.
- [x] T5 Compose and CI; E2E green; stress 100 of 100 (after fixing the spec 005 push check), no faulted messages.
- [x] T6 Retro "Still open" row, README; spec Done; `npm run check:specs`.
