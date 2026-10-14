# Tasks 013: Run several copies of each service

Plan: [plan.md](plan.md)

- [x] T1 Integration tests with two or three copies (red: watchers on one copy missed pushes; a switch set through one copy was ignored by the other; copies starting together failed on Inventory).
- [x] T2 Live-updates queue per Order copy; AC-1 green.
- [x] T3 Test switches in Payment's database; AC-2 green.
- [x] T4 Migrations and seeding one copy at a time; AC-3 green.
- [x] T5 Web live connection without negotiation; `WaitUntilStarted`.
- [x] T6 `docker-compose.copies.yml`, CI and nightly; E2E green with two copies (red first: the cancel scenario failed 5 of 30 with the old web page); stress 102 of 102, no faulted messages.
- [x] T7 Retro, README, ADR 0005 note; spec Done; `npm run check:specs`.
