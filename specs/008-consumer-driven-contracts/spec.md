# Spec 008: Consumer-driven contract between the web UI and the Order API

- **Status:** Done
- **Milestone:** Week 6, Contracts and idempotency
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The UI's types are generated from `order.openapi.yaml`, so the UI cannot drift from the OpenAPI file. But nothing proves the Order API actually behaves like the file, for the requests the UI really makes: a renamed field, a changed status code or a missing `Location` header would only show up in E2E, or in production. OpenAPI says what the API *offers*; the team needs a test of what the UI *relies on*.

## User stories

- As the web developer, I want my expectations of the Order API recorded and checked against the real API on every build, so that a breaking API change fails before it ships.
- As the API developer, I want to know exactly which fields and responses the UI depends on, so that I can change everything else freely.

## Acceptance criteria

- **AC-1 Expectations recorded.** The web tests record, as a contract file in the repo, every Order API interaction the UI uses: placing a valid order (201 with the order and its `Location`), placing an invalid one (400 with errors by field), reading a pending, a confirmed and a cancelled order, and reading an unknown order (404).
- **AC-2 Only what is used.** The contract matches on types and formats (IDs are UUIDs, timestamps are ISO 8601), not on exact generated values, and lists only fields the UI reads.
- **AC-3 Verified against the real API.** A .NET test replays every recorded interaction against the real Order API (in memory, no broker) and fails if any response does not match.
- **AC-4 Stale contract caught.** CI fails if the contract file is out of date with the web tests, or if the API breaks it.
- **AC-5 Breaking change caught.** Renaming a field the UI reads (tried once on purpose) makes the verification fail with a message naming the field.

## Non-goals

- A Pact Broker, "can-i-deploy" and versioned contracts: one repo, one branch, so the contract file is committed instead.
- Pacts for the Notification and Inventory APIs (test-only or not used by the UI's logic), and message pacts for events (the JSON Schema tests cover events).

## Open questions

Decided on the user's behalf (they asked to proceed without stopping, 2026-10-14). Revisit in review.

- [x] **Q1 Broker or committed file?** (answer: committed file in `pacts/`; ADR 0006.)
- [x] **Q2 How does the provider test get orders in a given state?** (answer: provider states: the test host exposes a test-only endpoint that creates the order through the domain and returns its ID, which the contract injects into the request path.)
