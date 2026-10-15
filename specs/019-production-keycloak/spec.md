# Spec 019: A production-ready Keycloak

- **Status:** Done
- **Milestone:** After week 8, tenth follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 012](../012-login-and-ownership/spec.md) (Keycloak, ADR 0008), [Specs 015](../015-staff-and-token-refresh/spec.md) and [017](../017-admin-manages-stock/spec.md) (roles, the E2E client)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

Keycloak runs in development mode (`start-dev`): plain HTTP, an in-container H2 database that forgets everything when the container is recreated, `admin` / `admin` as its administrator, and a realm file that carries demo users with known passwords and a test client with its secret. The services are told not to insist on HTTPS. None of that may reach production, and today nothing shows what production would look like, or proves it works.

## User stories

- As the shop, I want logins served by a Keycloak set up for production, so that passwords and tokens travel only over HTTPS and nothing test-only is exposed.
- As a developer, I want the same realm in development and production, plus only what development needs, so that the two cannot drift apart.

## Acceptance criteria

- **AC-1 One realm, two uses.** The production realm has no users, no client but the web app, and no secrets; the development realm is the production realm plus the development additions (demo users, the E2E client, local addresses), generated from the two, and a check fails if the generated file is out of date or the production realm gains a user, another client or a secret.
- **AC-2 Production mode on a real database.** In the production profile, Keycloak runs its optimized production build (`start`, not `start-dev`) on a PostgreSQL database of its own; users and the realm survive a restart.
- **AC-3 HTTPS only.** Keycloak answers only over HTTPS, and so does the shop; tokens name the HTTPS address as their issuer.
- **AC-4 Secrets stay out of git.** Keycloak's administrator and database passwords come from an environment file that git ignores (an example is committed); the development `admin` / `admin` does not work.
- **AC-5 The services insist on HTTPS.** In the production profile the services fetch Keycloak's keys over HTTPS only and trust its certificate; a person logs in through Keycloak's real page and places an order, which is confirmed.
- **AC-6 Nothing test-only.** In the production profile the payment test switches are off, and setting stock without an admin login is refused.
- **AC-7 Development and CI unchanged.** The development stack, with the generated realm, passes the full E2E suite 100 runs in a row with two copies of every service.
- **AC-8 Proven in CI.** A CI job starts the production profile and runs its smoke test.

## Business rules

- **R-1** The production realm file is the single source of truth for clients, roles and mappers; development may only add to it.
- **R-2** Certificates and secrets are made where they are used and never committed.

## Edge cases

- The certificate locally is signed by a certificate authority made on this machine: browsers and services trust it only because they are told to. A real deployment uses a publicly trusted certificate, and the services need no extra trust.
- Recreating Keycloak in production keeps its signing keys (they are in its database), unlike development (ADR 0008).

## Non-goals

- Deploying to a cloud, a real domain, high availability or backups (the user chose a production profile run locally, Q1).
- Keycloak themes, password policies, multi-factor authentication or email.
- TLS between the services and RabbitMQ or PostgreSQL.

## Open questions

- [x] **Q1 How far?** (answered by the user, 2026-10-15: production-ready, run locally: a production compose profile proven by a smoke test; no cloud.)

Decided on the user's behalf (2026-10-15). Revisit in review.

- [x] **Q2 How do the two realms stay one?** (answer: the production realm file is the source; a development overlay adds users, the E2E client and local addresses; a script merges them into the development realm, committed, and a check keeps it current (R-1). The web client's addresses in the production realm come from an environment variable that Keycloak resolves on import.)
- [x] **Q3 Certificates?** (answer: a script makes a local certificate authority and a server certificate for `localhost` and `keycloak` with OpenSSL, outside git (R-2). The services trust it through `SSL_CERT_FILE`; the smoke test's browser is told to accept it.)
- [x] **Q4 Keycloak's database?** (answer: a PostgreSQL container of its own, `keycloak-db`, separate from the shop's data.)
- [x] **Q5 The shop over HTTPS?** (answer: yes: the web server serves the app and proxies the APIs over HTTPS with the same certificate, so the whole login runs over HTTPS.)
