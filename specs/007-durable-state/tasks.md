# Tasks 007: Durable state and reliable announcements

Plan: [plan.md](plan.md)

- [x] T1 ADR 0005 (storage, outbox, two modes, migrations at startup). Constitution III adds the Integration layer.
- [x] T2 Integration test projects with Testcontainers; write the five tests first; watch them fail. (Order: 2 of 3 red as expected. Inventory: checked afterwards by pointing them at in-memory storage: the restart test fails.)
- [x] T3 ServiceDefaults: `RabbitMq:Port`.
- [x] T4 Order: `OrderDbContext`, EF repository, saga map, bus + consumer outbox; migration; AC-1, AC-3, AC-5 green.
- [x] T5 Inventory: `ICatalogStore`, `EfCatalogStore`, `InventoryDbContext`, outbox, retry on conflicts; migration; AC-2, AC-4 green.
- [x] T6 Component and unit tests still green in InMemory mode.
- [x] T7 docker compose with PostgreSQL; E2E 100 runs green (AC-6). Also restarted the real containers: order and stock identical.
- [x] T8 CI and README: integration layer, fast filter.
- [x] T9 Spec Done; plan matches reality.
