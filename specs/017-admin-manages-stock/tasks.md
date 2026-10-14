# Tasks 017: Admins manage stock

Plan: [plan.md](plan.md)

- [x] T1 Component scenarios (red: anyone could set stock, without a login).
- [x] T2 Inventory logins; admin-only `PUT /stock/{sku}`; switch removed; tests set stock as admins; mutation check.
- [x] T3 Contract 2.0.0 (gate passes it); realm: `admin`, `ada`, `orderflow-e2e`.
- [x] T4 Web: proxy, generated types, stock page, tests.
- [x] T5 E2E: admin scenario; full E2E with two copies 10 of 10; stress 100 of 100, no faulted messages.
- [x] T6 README, ADR 0008 note, spec 001 note, retro; spec Done; gates.
