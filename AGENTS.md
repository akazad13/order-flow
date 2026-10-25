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

---

## 3. Commands Cheat Sheet

```bash
# Build .NET solution
dotnet build OrderFlow.slnx

# Run all tests (unit & integration)
dotnet test OrderFlow.slnx

# Run contract and spec verification gates
node scripts/check-contracts.mjs
node scripts/check-specs.mjs

# Start local infrastructure (Postgres, RabbitMQ, Keycloak, Redis)
docker compose up -d

# Run frontend web app
cd web && npm install && npm run dev
```

---

## 4. Verification Gates for AI Agents

Before submitting code changes or finishing a turn:
1. Run `dotnet build OrderFlow.slnx` — must succeed with **0 errors**.
2. Run `dotnet test OrderFlow.slnx` — all unit tests must pass.
3. Run `node scripts/check-contracts.mjs` — all contract and linting gates must pass.
