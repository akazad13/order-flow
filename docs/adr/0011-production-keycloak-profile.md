# 0011. Keycloak for production: one realm source, HTTPS, its own database, secrets outside git

- **Status:** Accepted
- **Date:** 2026-10-15
- **Spec:** [019](../../specs/019-production-keycloak/spec.md)

## Context

ADR 0008 ran Keycloak in development mode: HTTP, an in-container database, `admin` / `admin`, and a realm file with demo users and a test client's secret. Nothing showed what production needs, and the realm mixed what production must have with what only development may have.

## Decision

- **The production realm file is the source of truth** (`deploy/keycloak/orderflow-realm.json`): roles, the web client and its mappers; no users, no other client, no secret; the web client's address comes from `ORDERFLOW_WEB_URL` at import. Development adds demo users, the E2E client and local addresses from `dev-overlay.json`; `scripts/build-dev-realm.mjs` generates the development realm, and `npm run check:realm` (in CI) keeps it current and the production realm clean.
- **A production profile** (`deploy/prod/docker-compose.prod.yml`): Keycloak's optimized build (`start --optimized`) on its own PostgreSQL, HTTPS only, the administrator and database password from a git-ignored env file; services fetch Keycloak's keys over HTTPS only (`RequireHttpsMetadata=true`); the shop is served over HTTPS; test switches are off.
- **Certificates and secrets are made where they are used** (`make-certs.sh`, `make-env.mjs`), never committed. Locally a CA made on the machine signs the certificate and the services trust it through `SSL_CERT_FILE`; a real deployment uses a publicly trusted certificate and drops that setting.
- **Proof:** a smoke test (`e2e/prod/smoke.spec.ts`) runs against the profile in CI: HTTPS only, the secrets file's administrator, no demo users or test client, a real login and a confirmed order, nothing test-only, and the realm surviving a restart in PostgreSQL.

## Consequences

- Development and production cannot drift: development is production plus a checked overlay.
- The profile uses the development stack's host ports: one runs at a time locally.
- Not covered: a cloud deployment, a real domain, high availability, backups, TLS to RabbitMQ and PostgreSQL, and closing the services' own ports (the profile still publishes them, for the smoke test's checks).
