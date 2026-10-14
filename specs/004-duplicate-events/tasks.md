# Tasks 004: Handle duplicate events without side effects

Plan: [plan.md](plan.md)

- [x] T1 Gherkin: `duplicate-order-placed`, `duplicate-payment`, `duplicate-order-events`, `duplicate-notification`; integration test AC-6. Watch them fail.
- [x] T2 Payment remembers outcomes; scenario green.
- [x] T3 Inventory decisions (memory + PostgreSQL with migration); unit, component, integration green.
- [x] T4 Order `Ignore` rules; scenario green.
- [x] T5 Notification dedupe; unit + scenario green.
- [x] T6 ADR 0004 updated (retry kept, ignore rules added).
- [x] T7 Full suite and E2E stress green (100 of 100, stack rebuilt over existing data); spec Done.
