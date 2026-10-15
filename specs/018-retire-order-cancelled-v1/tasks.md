# Tasks 018: Retire OrderCancelled v1

Plan: [plan.md](plan.md)

- [x] T1 Check ADR 0010's condition on the running stack; record it.
- [x] T2 Stray-v1 integration test (red: Inventory still consumed v1).
- [x] T3 Remove v1: record, upcast, readers' v1 interfaces, contract (3.0.0), v1 scenarios and steps; tests on v2; all .NET tests green on a clean build.
- [x] T4 E2E with two copies 10 of 10; stress 100 of 100, no faulted messages.
- [x] T5 ADR 0010 and retro notes; spec Done; gates.
