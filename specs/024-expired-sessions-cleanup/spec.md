# Spec 024: Expired sessions are cleaned up

- **Status:** Done
- **Milestone:** After week 8, from the retro's "Still open" list ([retro](../../docs/retro.md))
- **Builds on:** [Spec 023](../023-tokens-on-the-server/spec.md) (the gateway's sessions, ADR 0012), [Spec 013](../013-several-copies/spec.md) (several copies)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Since spec 023 the gateway keeps every login session, tokens included, as a row in its `sessions` table. A session that expires (eight hours without use) can no longer be used, but its row stays, with its refresh token, for ever. The table grows with every login, and old tokens sit in the database long after they are of any use.

## User stories

- As the shop, I want expired sessions removed from the gateway's database, so that it does not grow without end and does not keep tokens nobody can use.

## Acceptance criteria

- **AC-1 Expired sessions are removed.** Every `Gateway:SessionCleanupEvery` (one hour by default), the gateway removes the sessions whose expiry has passed, in PostgreSQL and in memory.
- **AC-2 Sessions in use stay.** A session that has not expired is not removed, including one whose expiry moved forward because it was used (the cookie's sliding expiry); it keeps working.
- **AC-3 Copies and failures.** Several copies of the gateway clean up at the same time without error. A cleanup that fails (the database unreachable) is logged, the gateway keeps serving, and the next cleanup runs on time.

## Business rules

- **R-1** A session is removed only after its expiry has passed; until then it is never touched by the cleanup.

## Edge cases

- The gateway was stopped for a while: the first cleanup after it starts (one period later) removes what expired meanwhile.
- A session is used at the moment it is removed: it had already expired, so the call answers 401 either way (spec 023).

## Non-goals

- Ending sessions earlier than their expiry ("log out everywhere", idle limits other than the cookie's).
- Cleaning up the other tables kept on purpose (stock decisions, order trackers; see the retro).

## Open questions

Decided on the user's behalf (2026-10-15). Revisit in review.

- [x] **Q1 How often?** (answer: every hour, set by `Gateway:SessionCleanupEvery`. Sessions last eight hours; an expired row lingering up to an hour is harmless, since it can no longer be used.)
- [x] **Q2 Which copy cleans up?** (answer: every copy, without a lock: removing expired rows twice removes nothing the second time.)
- [x] **Q3 A grace period?** (answer: none. The gateway already treats an expired session as gone, so nothing could still need it.)
