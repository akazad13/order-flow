# Spec 013: Run several copies of each service

- **Status:** Done
- **Milestone:** After week 8, fourth follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 005](../005-live-order-status/spec.md) (live updates), [Spec 007](../007-durable-state/spec.md) (durable state, ADR 0005), [Spec 010](../010-durable-payments/spec.md) (Payment decisions shared through PostgreSQL)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Every service keeps its business state in PostgreSQL and every consumer is idempotent, so in principle the shop can run two copies of each service for capacity or to survive one copy failing. The retro found three things that break when it does:

1. Live updates are handled through one shared queue, so only one copy hears that an order was confirmed. A customer whose browser is connected to the other copy never sees the change.
2. Payment's test switches (decline or stall a customer's payments) live in one copy's memory. A switch set through one copy is ignored when the other copy takes the payment.
3. Every copy migrates its database when it starts. Copies starting at the same moment race to create the database and its tables.

## User stories

- As the shop, I want to run more than one copy of each service, so that one busy or failed copy does not stop customers ordering.
- As a customer, I want to see my order's status change live whichever copy my browser happens to be connected to.

## Acceptance criteria

- **AC-1 Live updates reach watchers on every copy.** Given two copies of the Order service and a customer watching an order on each copy, when the order is confirmed or cancelled, then both watchers see the final status, whichever copy handled the news.
- **AC-2 Test switches apply on every copy.** Given two copies of the Payment service, when a customer's payments are set to decline (or stall) through one copy, then the other copy declines (or leaves undecided) that customer's payments too.
- **AC-3 Copies can start together.** Given a database that does not exist yet, when three copies of a service start at the same moment, then all three start and the database is migrated once.
- **AC-4 The live connection works behind a load balancer.** Given the browser's requests are spread across copies, when it opens its live connection, then the connection is made and updates arrive (no step of the connection depends on reaching the same copy twice).
- **AC-5 Everything still works end to end.** With two copies of every service, the full E2E suite passes 100 runs in a row with no faulted messages.

## Business rules

- **R-1** Each piece of work is done once whichever copy takes it: an order is held, charged, refunded and announced once (specs 004, 007, 010, 011 already guarantee this; this spec runs it with real copies).
- **R-2** Live updates are a notice, not a record: a copy that is not running when an order changes misses that push, and the browser reads the order again when it reconnects (spec 005, AC-3).

## Edge cases

- A copy stops: its live-updates queue goes with it (nothing piles up for a copy that is gone); its browsers reconnect, to any copy, and read the order again.
- A copy starts while another is migrating: it waits for the migration, then finds nothing left to do.
- Two copies take copies of the same message at the same moment: the existing database checks decide (primary keys, row versions, the consumer outbox), as in specs 007 to 011.

## Non-goals

- Starting and stopping copies automatically (autoscaling), health-based routing, or sticky sessions.
- A SignalR backplane (such as Redis): not needed while each copy hears every update itself (Q1).
- Several copies of PostgreSQL, RabbitMQ or Keycloak.
- The web server noticing copies that start after it (it looks the copies up once, when it starts).

## Open questions

Decided on the user's behalf (they asked to start this follow-up, 2026-10-14). Revisit in review.

- [x] **Q1 How does every copy hear about order changes?** (answer: each Order copy gets a live-updates queue of its own, deleted when the copy stops, so RabbitMQ delivers each change to every copy. No new infrastructure; a SignalR backplane would add Redis.)
- [x] **Q2 Migrations: a separate step, or at startup made safe?** (answer: at startup, made safe: copies take turns through a PostgreSQL lock, so `docker compose up --scale` and the integration tests need no extra step. A separate migration step remains the better choice when the services lose the right to change the schema, e.g. in production.)
- [x] **Q3 Where are the test switches kept?** (answer: in Payment's database, in a table of their own, so every copy reads the same switches and they also survive a restart. In memory for component tests and `dotnet run`, as before.)
- [x] **Q4 How many copies in E2E?** (answer: two of every service, in CI and the stress run; `docker compose up` still starts one of each for everyday work.)
