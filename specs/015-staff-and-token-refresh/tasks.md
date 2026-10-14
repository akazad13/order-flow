# Tasks 015: Staff support orders, and logins renew themselves

Plan: [plan.md](plan.md)

- [x] T1 Component scenarios for staff (red: four "staff can" scenarios failed).
- [x] T2 Role claim, `FindAllowedAsync`, `requestedBy`, staff detail; contracts 1.1.0; component and contract tests green; mutation checks.
- [x] T3 Realm: `staff` role, roles mapper, demo user `sam`.
- [x] T4 Web: token renewal and ended logins in `auth.ts`; `StaffLookup`; unit tests.
- [x] T5 E2E: staff scenario and renewal scenarios (red with the old web page: the renewed token was stored but the old one sent; an ended session still showed "Logged in"; no lookup), then green; full E2E with two copies 9 of 9; stress 99 of 99, no faulted messages.
- [x] T6 README, retro, ADR 0008 note; spec Done; gates.
