# AGENTS.md — AI Agent Guidance & Repository Rules

This document provides mandatory guidance for autonomous AI coding agents (Antigravity, Claude Code, GitHub Copilot, Cursor, Windsurf, Devin) operating in `akazad13/order-flow`.

---

## 1. System Identity & Architecture

**Product**: `OrderFlow` — Event-driven order processing platform built with .NET 10 microservices, RabbitMQ, PostgreSQL, Keycloak, and Spec-Driven Development (SDD).
- **Backend Services**: .NET 10, C# 13, MassTransit, EF Core Outbox, PostgreSQL 17, RabbitMQ AMQP.
- **Frontend**: React 19, TypeScript, Vite.
- **Security**: Keycloak OIDC, BFF session pattern, HttpOnly session cookies.
- **Contracts & Quality**: OpenAPI (Spectral), AsyncAPI, Consumer-Driven Contracts (Pact), Testcontainers.
- **Solution File**: `OrderFlow.slnx`.

### Project Layout:
- `src/`: Microservices (`OrderFlow.OrderService`, `OrderFlow.PaymentService`, `OrderFlow.InventoryService`, `OrderFlow.Gateway`).
- `contracts/`: Shared message contracts, OpenAPI specifications, and AsyncAPI event schemas.
- `docs/`: Architecture decision records (`docs/adr/`), testing paradigm (`docs/testing-paradigm.md`), and retrospectives.
- `specs/`: Formal specifications, Gherkin feature files, and architecture decision records.
- `tests/`: Unit tests, Pact contract tests, Testcontainers integration tests, and Playwright E2E suites.
- `web/`: React 19 single-page application.

---

## 2. Inviolable Architectural Invariants

1. **Spec-Driven Development (SDD) Protocol**:
   - Every domain change must be preceded by an updated contract/spec in `contracts/` or `specs/`.
   - Never commit code that breaks `node scripts/check-contracts.mjs` or `node scripts/check-specs.mjs`.
2. **Transactional Outbox Pattern**:
   - Messages dispatched to RabbitMQ must use MassTransit's Entity Framework Core Outbox to ensure atomicity with database transactions. Never perform ad-hoc direct un-outboxed message publishes within database transactions.
3. **Idempotent Consumers**:
   - All message consumers must be idempotent to handle network retries and duplicate message deliveries.
4. **Banned Packages**:
   - Follow workspace policy: Do not introduce `Moq` or `AutoMapper`.
5. **MassTransit Version Pinning (ADR-0002 / ADR-0013)**:
   - MassTransit packages in `Directory.Packages.props` MUST remain pinned to v8 (`8.5.11`). Do NOT upgrade to v9+ due to commercial licensing restrictions and breaking saga changes.
6. **Constraint-Driven Quality Floor (`CONSTRAINTS.md`)**:
   - Read `CONSTRAINTS.md` before writing code. Do not weaken it to make a change pass.
   - Never introduce `@ts-ignore`, `@ts-nocheck`, `#pragma warning disable` (outside migrations), `[Fact(Skip)]`, `test.skip()`, or `NotImplementedException` stubs.

---

## 3. Commands Cheat Sheet

```bash
# Fast agent checks (<10s: constraints, contracts, specs)
npm run check:fast

# Task completion checks (<60s: check:fast + unit/component + web tests)
npm run check:task

# Build .NET solution
dotnet build OrderFlow.slnx

# Run fast unit/component tests (in-memory, zero Docker)
dotnet test OrderFlow.slnx --filter "FullyQualifiedName!~Integration"

# Run a single focused test during TDD loop
dotnet test OrderFlow.slnx --filter "FullyQualifiedName~OrderTests.Cannot_Cancel_Shipped_Order"

# Run all tests (including Testcontainers integration tests)
dotnet test OrderFlow.slnx

# Run contract, spec, and constraint verification gates
npm run check:fast
node scripts/check-contracts.mjs

# Start local infrastructure (Postgres, RabbitMQ, Keycloak, Redis)
docker compose up -d

# Run frontend web app
cd web && npm install && npm run dev
```

---

## 4. Verification Gates for AI Agents

Before submitting code changes or finishing a turn:
1. Run `npm run check:fast` — all floor constraints, contract lints, and spec alignments must pass.
2. Run `dotnet build OrderFlow.slnx` — must succeed with **0 errors**.
3. Run `dotnet test OrderFlow.slnx --filter "FullyQualifiedName!~Integration"` — all fast unit and component tests must pass.
4. Run `node scripts/check-contracts.mjs` — all contract and linting gates must pass.

---

## 5. Testing Paradigm & Agentic Verification (ADR-0014)

Follow the core testing standards codified in [ADR-0014](docs/adr/0014-testing-paradigm-and-agentic-verification.md) and [docs/testing-paradigm.md](docs/testing-paradigm.md):

1. **Test Pyramid & Resource Allocation**:
   - **~80% Unit & Component Tests** (Small / Milliseconds): Pure domain aggregates (`xUnit`, `Shouldly`) and service boundaries using MassTransit In-Memory Test Harness and `FakeTimeProvider`. Zero Docker dependencies.
   - **~15% Integration Tests** (Medium / Seconds): Real PostgreSQL 17 and RabbitMQ 4.1 via `Testcontainers.NET`. Exercises migrations, concurrency, and EF Core Transactional Outbox persistence.
   - **~5% Contract & E2E Tests** (Large / Minutes): Pact HTTP consumer/provider contracts, Spectral OpenAPI lints, and Playwright full-stack browser journeys (with 0 messages in `_error` or `_skipped` queues).

2. **State Over Interactions**:
   - Assert on state transitions, return values, and outbox event emissions.
   - Do NOT mock repositories or verify internal private method call counts. Real domain aggregates and in-memory test harnesses are faster and refactor-safe.

3. **DAMP Over DRY**:
   - Tests favor **Descriptive And Meaningful Phrases** (DAMP).
   - Each test must tell an understandable, self-contained story without navigating complex, nested helper abstractions.

4. **Banned Dependencies**:
   - **Zero Moq**: Always use `NSubstitute` for test doubles.
   - **Zero AutoMapper**: Use explicit domain mapping or `Mapperly`.

5. **Agentic Verification Loop**:
   - **TDD (Red-Green-Refactor)**: Write a failing test first, make it pass with minimal code, then refactor.
   - **Prove-It Pattern for Bugs**: Reproduce every bug with a failing test before attempting any fix.
   - **Focused Test Execution**: During active editing, run targeted single tests (`--filter "FullyQualifiedName~<TestName>"`) to stay within seconds. Run `npm run check:task` before marking tasks complete.

