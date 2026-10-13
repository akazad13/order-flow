# 0002. Messaging: RabbitMQ with MassTransit v8

- **Status:** Accepted
- **Date:** 2026-10-14

## Context

Services communicate only through events. We need a broker that runs locally in Docker, and a .NET library that handles serialization, retries, sagas, the outbox and an in-memory test harness. MassTransit v9 moved to a commercial license; v8 remains open source.

The proposal suggested starting on the in-memory bus. The in-memory transport only works inside one process, so it cannot connect separately running services.

## Decision

- Use RabbitMQ (docker compose) for every running environment from week 1.
- Use MassTransit **v8** (pinned in `Directory.Packages.props`). Do not upgrade to v9.
- Use the MassTransit in-memory **test harness** in component tests, so they need no Docker.
- Event exchanges are named by the contract (`[EntityName("order-placed")]`); queues are prefixed with the service name (`notification-order-placed`).

## Consequences

- `docker compose up` is needed to run the stack, but not to run unit, component or contract tests.
- Real-broker behavior (durability, redelivery) is tested from week 5 with Testcontainers.
- If MassTransit v8 becomes a problem, the fallback is the raw RabbitMQ client behind our own interface; the contracts do not change.
