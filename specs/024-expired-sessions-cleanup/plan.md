# Plan 024: Expired sessions are cleaned up

Spec: [spec.md](spec.md)

## Approach

| Concern | How |
| --- | --- |
| What expired (AC-1, R-1) | Both session stores implement `IExpiredSessions.RemoveExpiredAsync(now)`: PostgreSQL deletes the rows with `expires_at <= now` in one statement (the column is indexed since spec 023); memory removes the tickets whose expiry has passed |
| When (AC-1) | `ExpiredSessionsCleanup`, a background service: a `PeriodicTimer` on the gateway's `TimeProvider`, every `Gateway:SessionCleanupEvery` (one hour). The timer is made when the service starts, so the schedule starts with the gateway (in .NET 10, `ExecuteAsync` starts on another thread) |
| Sessions in use (AC-2) | Nothing new: the cookie's sliding expiry already stores the later expiry (`RenewAsync` updates `expires_at`); the tests check that a used session survives the time an unused one would not |
| Copies (AC-3) | Every copy cleans up, no lock (Q2); a delete that finds nothing is harmless |
| Failures (AC-3) | Caught and logged as a warning; the loop goes on to the next tick. `NextCleanup` (a task completing with the count, or the failure) lets the tests wait for a cleanup instead of sleeping |

## Services affected

| Area | Change |
| --- | --- |
| Gateway | `IExpiredSessions` and `ExpiredSessionsCleanup`; both stores implement the removal; `Gateway:SessionCleanupEvery`; registration in `Program.cs`. No migration (spec 023 already indexed `expires_at`) |

## Contract changes

None.

## Test plan (criterion to layer)

| Criterion | Unit | Component | Integration | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 Expired sessions are removed | | x | x | | `gateway-session-cleanup.feature` "An expired session is removed at the next cleanup"; `GatewaySessionTests.Two_copies_remove_the_expired_sessions_and_keep_the_ones_in_use` |
| AC-2 Sessions in use stay | | x | x | | `gateway-session-cleanup.feature` "A session in use stays"; `GatewaySessionTests.Two_copies_remove_the_expired_sessions_and_keep_the_ones_in_use` |
| AC-3 Copies and failures | x | | x | | `ExpiredSessionsCleanupTests.A_failed_cleanup_is_logged_and_the_next_one_runs_on_time`; `GatewaySessionTests.Two_copies_remove_the_expired_sessions_and_keep_the_ones_in_use` |
| R-1 Only after expiry | | x | x | | as AC-2 |

No E2E: the cleanup runs once an hour in the stack, and nothing a browser does shows it; the integration test runs the real gateway against PostgreSQL with two copies.

## Constitution check

- [x] Spec first; [x] no contract change; [x] every AC automated; [x] no sleeps (tests wait on `NextCleanup`, with a fake clock); [x] no new packages in the gateway (`Microsoft.Extensions.TimeProvider.Testing` was already used by the component tests)

## Risks and decisions

- **Mutation checks:** removing every session with an expiry fails "A session in use stays" (memory) and the integration test (PostgreSQL); not removing fails "An expired session is removed"; a renewal that does not store the later expiry fails the integration test; letting a failure end the loop fails the failure test.
- **Found: the timer could miss the clock.** In .NET 10 a background service's `ExecuteAsync` starts on another thread, so a timer made there could be made after the test had moved the clock, and never fire (the failure test timed out). The timer is now made in `StartAsync`.
- **The clock in the integration test starts in 2030**, so a session expiry that followed the wall clock rather than the gateway's clock would show.
