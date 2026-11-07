# Constraints & Quality Bar for OrderFlow

Last reviewed: 2026-11-07 by AI Agent Pair

This document establishes OrderFlow's non-negotiable quality bar and constraints for autonomous AI coding agents and human engineers. It enforces **Constraint-Driven Development (CDD)** to guarantee that rapid agentic iteration never silently lowers repository quality standards or strips assertions.

---

## 1. The Floor (Always Enforced mechanically)

Any violation of this floor fails the verification gate immediately:

- **No Suppression Comments**:
  - TypeScript: Zero `@ts-ignore` or `@ts-nocheck` comments.
  - C#: Zero `#pragma warning disable` outside auto-generated EF Core migrations (`*.Designer.cs`, `*ModelSnapshot.cs`) and Reqnroll feature wrappers (`*.feature.cs`).
- **No Unimplemented Stubs**:
  - C#: Zero `throw new NotImplementedException()` in production code under `src/`.
  - TypeScript: Zero `throw new Error("Not implemented")` in production code under `web/src/`.
- **No Skipped or Silenced Tests**:
  - C#: Zero `[Fact(Skip = "...")]` or `[Theory(Skip = "...")]`.
  - TypeScript: Zero `test.skip()`, `it.skip()`, `describe.skip()`, or `xit()`.
- **Package Invariants & Licensing (ADR-0002 / ADR-0013)**:
  - MassTransit in `Directory.Packages.props` MUST remain pinned to v8 (`8.5.11`).
  - Banned packages: `Moq` (use `NSubstitute`) and `AutoMapper` (use manual mapping / `Mapperly`).
- **Transactional Outbox Invariant (ADR-0005)**:
  - Dispatches to RabbitMQ within database transactions must use EF Core Transactional Outbox; ad-hoc direct un-outboxed publishes are forbidden.
- **Spec-Driven Governance (Constitution Article I)**:
  - No domain behavior changes without an approved specification in `specs/NNN-name/spec.md`.

---

## 2. Enforced Dimensions & Commands

| Dimension | Rule | Checked by | Phase |
| :--- | :--- | :--- | :--- |
| **Floor & Invariants** | Zero suppressions, zero skipped tests, zero stubs | `node scripts/check-constraints.mjs` | Every task |
| **Backend Types & Build** | 0 errors, 0 warnings (`TreatWarningsAsErrors`) | `dotnet build OrderFlow.slnx` | Every task |
| **Frontend Types & Build** | 0 type errors, clean Vite bundle | `npm --prefix web run build` | Every task |
| **Spec Tracing** | 100% of Acceptance Criteria mapped to named tests | `node scripts/check-specs.mjs` | Task end, CI |
| **Contract Compatibility** | Semantic versioning enforced, no breaking API changes | `node scripts/check-contracts.mjs` | Task end, CI |
| **Contract Linting** | OpenAPI Spectral & AsyncAPI CLI pass | `npm run lint:contracts` | Task end, CI |
| **Fault Queues** | 0 messages in `_error` or `_skipped` queues | `node e2e/scripts/check-error-queues.mjs` | Post E2E, CI |

---

## 3. Tiered Agent Execution Lifecycles

Autonomous agents operating on OrderFlow must adhere to these tiered commands to balance speed and rigor:

1. **Fast Edit Check (`check:fast`)** (Target: < 10 seconds):
   - Validates constraints, OpenAPI/AsyncAPI contracts, and spec alignment.
   ```bash
   npm run check:fast
   ```
2. **Task Completion Check (`check:task`)** (Target: < 60 seconds):
   - Fast edit checks + .NET unit/component tests + web tests.
   ```bash
   npm run check:task
   ```
3. **Full Quality Gate (`check:full`)** (Target: CI & Pre-Merge):
   - Complete verification including Testcontainers integration tests and contract compatibility against base branch.
   ```bash
   npm run check:full
   ```
