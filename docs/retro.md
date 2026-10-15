# Retro: eight weeks of spec-driven development

- **Date:** 2026-10-14
- **Scope:** specs 000 to 009, weeks 1 to 8
- **Closes:** spec 000 task T17 ("what was unclear? update the templates")

## Where we ended

| What | Count |
| --- | --- |
| Specs (all Done) | 10 (000 to 009) |
| Acceptance criteria / business rules | 59 / 17, each with a test-plan row and a named test (checked by CI, spec 009) |
| Open questions decided on the user's behalf | 24, each marked "Revisit in review" in its spec |
| ADRs | 7 |
| Unit / component / contract tests (.NET) | 32 / 50 scenarios / 15 |
| Pact interactions (UI and Order API) | 8, plus an automated "breaking rename" check |
| Integration tests (PostgreSQL + RabbitMQ in Testcontainers) | 8 |
| Web tests (Vitest, including Pact) | 19 |
| E2E scenarios (Playwright + playwright-bdd) | 5; stress runs of 100 green after every week since week 2 |
| Gate script tests | 20 |

## What found the bugs

The useful question for a test strategy is not "how many tests" but "which layer caught what". Every real defect of the eight weeks, by what caught it:

| Week | Defect | Caught by |
| --- | --- | --- |
| 2 | `PaymentSucceeded` overtook `StockReserved` (different publishers); orders stuck Pending, 1 run in 40 | E2E stress run |
| 2 | A partitioner made it worse (7 in 100): it keeps arrival order, so it cannot help | E2E stress run |
| 2 | Inbox messages shown out of order | E2E stress run |
| 4 | Event for an order not yet tracked was silently discarded (MassTransit default), 2 in 100 | E2E stress run, then RabbitMQ counters |
| 5 | PostgreSQL serialization failures (40001) under concurrent saga updates | Integration tests |
| 6 | Generated migration defaulted `Status` to `""`: every existing hold would never be released | Reading the migration; checked on the running database |
| 6 | `GetOrAdd` race: two simultaneous copies could both charge | Code review of my own change |
| 7 | Harness waits (`Any`, `SelectAsync`) gave up when the bus looked idle: up to 4 failures a run under load | Running the suite 10 times in a row |
| 7 | Web Docker image had not built since week 6 (`node:path` types found only in the root `node_modules`) | E2E against a rebuilt stack |
| 7 | Stock held forever when a cancellation overtook the placement | Writing the spec's edge cases, then a red test |
| 7 | The race test only ever raced one way (20 of 20 confirmed) | Printing the test's own outcome split |
| 7 | UI showed Confirmed with no word when an accepted cancel lost the race | Reviewing the UI against AC-8 |
| 8 | 16 disagreements between specs, plans and tests (names of tests that never existed, a criterion with no test, a Done spec with open tasks) | The spec gate's first run |

What the table says:

1. **Message ordering is where the bugs live.** Six of the thirteen are "this arrives before that". Every spec after week 4 has edge cases phrased as orderings ("arrives twice", "arrives before", "at the same moment"), and those lines, not the happy paths, produced the tests that mattered.
2. **Component tests proved the design; repeated E2E and integration runs found the defects.** No single green run found any of the week 2, 4 or 7 races. "100 in a row" became part of Done.
3. **A test is only evidence once you have seen it fail for the right reason.** Red first, `git stash` re-runs (week 6), mutation checks (weeks 6 to 8) and printing a race's outcome split (week 7) each exposed a test that proved less than it claimed.
4. **Plans drift silently.** Plans are written before the code, and names change during the build. Without a gate, three plans named tests that never existed. Now CI reads them.

## What worked

- **Spec, plan, tasks, in that order.** Writing the criteria first made each week's scope obvious and kept the UI, API and events in step. Numbered criteria gave every test a reason to exist.
- **Contracts as code.** Generated UI types turned a new cancellation reason into a compile error until the UI had words for it (week 7). Pact caught a renamed field and a changed status code; JSON Schema caught MassTransit's decimals-as-strings in week 2.
- **Choreography with one state machine as the single writer of order status.** Every new flow (cancel, refund, live updates) fitted without changing the others' code paths.
- **Test-only switches inside real services** (stock levels, declines, stalls), per customer so parallel tests never interfere.

## What was hard or unclear

- **Spec 000's template had no place for orderings.** "Edge cases" was a free list, and the early ones were about input validation. The template now prompts for message orderings explicitly.
- **"Done" was not defined for tasks we could not do** (pushing to GitHub). Now: `- [-] Tn ... (deferred: reason)`.
- **Test plan names were free text.** Now: exact `Class.Method`, `file.feature` or file name, checked by `npm run check:specs`.
- **MassTransit test-harness semantics** (idle detection, ignored events not recorded by the saga harness) cost more time than any production bug. Positive waits now poll (`tests/Shared/Eventually.cs`).
- **Deciding open questions without the user.** It kept the pace, but 24 product decisions are waiting for a human. They are the first thing to review.

## Changes made because of this retro

- Templates: spec edge cases prompt for orderings; plan test table names exact tests; tasks allow deferral with a reason and end with the gate.
- ADR 0007 and constitution: the gates in CI (spec 009).
- Follow-ups done since: Payment's decisions ([spec 010](../specs/010-durable-payments/spec.md)) and Notification's inbox ([spec 011](../specs/011-durable-inbox/spec.md)) moved to PostgreSQL. Every service now survives a restart, and a repeat after a restart neither charges nor tells the customer again.
- Customers log in with Keycloak and see only their own orders and inbox ([spec 012](../specs/012-login-and-ownership/spec.md), ADR 0008).
- Every service runs as two copies in E2E: live updates reach browsers on either copy, Payment's test switches are shared, and copies starting together migrate one at a time ([spec 013](../specs/013-several-copies/spec.md)).
- Contracts follow semantic versioning from 1.0.0, and CI fails a breaking change without a new major version ([spec 014](../specs/014-contract-compatibility/spec.md), ADR 0009). Run over history, the gate found that spec 006 changed two contracts without a new version and edited a published event.
- Staff can look up and cancel any order on the customer's behalf, and logins renew themselves before the token expires ([spec 015](../specs/015-staff-and-token-refresh/spec.md)).
- A new version of an event is a new message that readers learn before it is published (ADR 0010); `OrderCancelled` v2 gives staff cancellations their own reason ([spec 016](../specs/016-event-versions/spec.md)).
- Admins manage stock on a stock page; setting stock is no longer a test-only endpoint ([spec 017](../specs/017-admin-manages-stock/spec.md)).
- `OrderCancelled` v1 is retired, the full ADR 0010 cycle done once; a stray v1 would land in a skipped queue, not vanish ([spec 018](../specs/018-retire-order-cancelled-v1/spec.md)).
- The decisions taken on the user's behalf were reviewed (2026-10-15): the user kept all but four, now in "Still open".
- The inbox names the short items when stock is short ([spec 020](../specs/020-itemised-shortage-message/spec.md)).
- In the production profile only the shop and Keycloak are reachable from outside ([spec 021](../specs/021-closed-ports-in-production/spec.md)).
- A production profile for Keycloak (optimized build, its own PostgreSQL, HTTPS, secrets outside git, a clean production realm), smoke-tested in CI ([spec 019](../specs/019-production-keycloak/spec.md), ADR 0011).

## Still open

| Item | Why it matters | Where |
| --- | --- | --- |
| Push to GitHub, see CI green, protect `main` with the CI jobs | Every gate here only bites in CI | Spec 000, T15 (deferred) |
| Cancel confirmed orders (with a refund), not only Pending ones | Reviewed 2026-10-15: the user wants it (was spec 006's rule) | Spec 006 |
| Keep tokens on the server (a backend for the web app); the browser holds only a secure cookie | Reviewed 2026-10-15: the user wants it (was spec 015, Q4: refresh token in session storage) | Spec 015 |
| Stock decisions (`reservations`) and finished order trackers (`OrderState`) are never deleted | Kept on purpose: they are what makes a late repeat harmless (spec 004). Removing them needs a retention window longer than any redelivery. Outbox and inbox tables are not a problem: MassTransit deletes delivered messages and its `InboxCleanupService` removes processed ones after the duplicate-detection window | Spec 004 non-goal |
| Deploying the production profile somewhere real (a domain, a trusted certificate, backups), staff reading inboxes | The production profile runs and is smoke-tested locally and in CI since spec 019 | Specs 015 and 019 non-goals, ADR 0011 |
| Starting and stopping copies automatically; the web server noticing copies started after it | Two copies of each service run since spec 013, but a fixed number, set in compose | Spec 013 non-goals |
