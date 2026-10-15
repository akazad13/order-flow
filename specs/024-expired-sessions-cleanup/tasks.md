# Tasks 024: Expired sessions are cleaned up

Plan: [plan.md](plan.md)

- [x] T1 Component scenarios (2) and the failure test; the failure test first timed out (the timer was made after the clock moved).
- [x] T2 `IExpiredSessions` in both stores; `ExpiredSessionsCleanup` background service; option.
- [x] T3 Integration test against PostgreSQL with two copies; five mutation checks, each caught.
- [x] T4 ADR 0012, README, retro; spec Done; gates.
