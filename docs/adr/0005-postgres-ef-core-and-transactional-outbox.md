# 0005. PostgreSQL with EF Core and the MassTransit transactional outbox

- **Status:** Accepted
- **Date:** 2026-10-14
- **Spec:** [007](../../specs/007-durable-state/spec.md)

## Context

Order and Inventory state must survive restarts, and "save the change" and "announce it" must not be able to diverge (spec 007, R-1, R-2). Fast tests must keep running without Docker (spec 007, Q1).

## Decision

- **PostgreSQL**, one database per service on one server. **EF Core** with the Npgsql provider.
- **MassTransit Entity Framework outbox**: the bus outbox for HTTP endpoints, the consumer outbox (with inbox) for message endpoints. State changes and outgoing messages commit in one transaction; a background delivery service sends them.
- **Saga state** in the Order database (EF saga repository, pessimistic row locks).
- **Inventory concurrency** via optimistic concurrency on PostgreSQL's `xmin`; a conflict retries the message with a fresh scope.
- **Two storage modes** selected by `Persistence:Provider`: `InMemory` (unit, component, `dotnet run`) and `Postgres` (compose, integration tests).
- **EF Core migrations**, applied at startup.

## Consequences

- An order can no longer be saved without OrderPlaced eventually being sent, even if the broker is down at the time.
- Two code paths for storage. Every Postgres-only behavior gets an integration test (Testcontainers).
- Applying migrations at startup is safe with one instance per service only; running several would need a separate migration step.
- The inbox drops a redelivered message with the same MessageId. Duplicates with new IDs remain spec 004's job.
