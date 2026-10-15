# Spec 021: Only the shop and Keycloak are reachable in production

- **Status:** Done
- **Milestone:** After week 8, from the review of the decisions taken on the user's behalf ([retro](../../docs/retro.md))
- **Builds on:** [Spec 019](../019-production-keycloak/spec.md) (the production profile, ADR 0011)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The production profile still publishes every service's own port (5101 to 5104), PostgreSQL (5432) and RabbitMQ (5672, and its management UI on 15672), as the development stack does. Anyone who can reach the machine can call the services directly, around the web server, read the databases' port, or open RabbitMQ's management UI. Spec 019 kept them for its smoke test; reviewing that, the user wants them closed.

## User stories

- As the shop, I want only the shop's web server and Keycloak reachable from outside in production, so that the services, databases and broker can only be reached through them, or from inside.

## Acceptance criteria

- **AC-1 Two doors.** In the production profile, from outside, the shop (HTTPS 5443) and Keycloak (HTTPS 8443) answer; the services' ports, PostgreSQL, RabbitMQ and its management UI refuse connections.
- **AC-2 The shop still works through its door.** A person logs in and places an order that is confirmed, through the web server only (as in spec 019).
- **AC-3 The checks move inside.** The smoke test still shows nothing test-only is on: setting stock without an admin is refused through the web server, and the payment test switches answer 404 from inside the profile's network.
- **AC-4 Ready means healthy.** The profile's services report healthy to docker compose (they have no published port to poll any more), and starting the profile waits for them.

## Business rules

- **R-1** In production, everything except the web server and Keycloak is reachable only from inside the profile's network.

## Edge cases

- An operator needs RabbitMQ's management UI or a database: from inside (e.g. `docker compose exec`), not through a published port.

## Non-goals

- Closing ports in the development stack (tests and developers use them).
- A firewall, or TLS inside the network (spec 019 non-goal).

## Open questions

- [x] **Q1 Close the ports?** (answered by the user, 2026-10-15, reviewing spec 019: yes.)

Decided on the user's behalf (2026-10-15). Revisit in review.

- [x] **Q2 How does the smoke test check from inside?** (answer: a plain HTTP request over bash's `/dev/tcp`, run in one of the services' containers with `docker exec`; no extra image.)
- [x] **Q3 PostgreSQL and RabbitMQ too?** (answer: yes, with RabbitMQ's management UI (R-1); the user's review named the services, and the same reasoning covers the rest.)
