# Tasks 008: Consumer-driven contract between the web UI and the Order API

Plan: [plan.md](plan.md)

- [x] T1 ADR 0006.
- [x] T2 Web: `createOrderApi` factory; pact test; contract file written to `pacts/`.
- [x] T3 .NET: `OrderFlow.Order.PactTests` with Kestrel host and provider states; verification green.
- [x] T4 Mutation check (AC-5): renamed `Status` to `State` in `OrderResponse`; verification failed with "Actual map is missing the following keys: status"; reverted.
- [x] T5 CI: pact diff in the web job; verification in the .NET job.
- [x] T6 README, constitution (contract layer mentions Pact); spec Done.
