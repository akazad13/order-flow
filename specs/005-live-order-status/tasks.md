# Tasks 005: See order status change live

Plan: [plan.md](plan.md)

- [x] T1 Contract: `contracts/realtime/order-updates.asyncapi.yaml`; lint script.
- [x] T2 Component scenarios (red): `live-order-updates.feature`.
- [x] T3 Order: hub and `OrderUpdatesConsumer`; scenarios green.
- [x] T4 Web tests (red), then `live.ts` and App; green.
- [x] T5 nginx and Vite WebSocket proxy; E2E push and read count (AC-6); E2E green on compose; stress 100 of 100.
- [x] T6 README, spec 001 note; spec Done.
- [x] T7 (found) Positive harness waits poll (`tests/Shared/Eventually.cs`); `@types/node` in web.
