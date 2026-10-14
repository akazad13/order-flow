# Tasks 016: A new version of an event, next to the old one

Plan: [plan.md](plan.md)

- [x] T1 Gate rule `readersFollow` and its tests.
- [x] T2 Contract: `OrderCancelledV2` published without readers (red: the gate named three readers), then with readers (green); order API and live updates 2.0.0.
- [x] T3 .NET: `OrderCancelledV2`, upcast, readers of both versions, Order publishes v2 with `StaffRequested`; component scenarios for v2 and v1; mutation checks.
- [x] T4 Web: words for `StaffRequested`; tests.
- [x] T5 E2E with two copies 9 of 9 (each reader's one queue bound to both exchanges, checked with rabbitmqctl); stress 108 of 108, no faulted messages.
- [x] T6 ADR 0010, constitution IV, spec 006 note, retro; spec Done; gates.
