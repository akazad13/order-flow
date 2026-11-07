# 0014. Testing Paradigm and Agentic Verification

- **Status:** Accepted
- **Date:** 2026-11-07

## Context

As `OrderFlow` incorporates autonomous AI coding agents (Antigravity, Claude Code, GitHub Copilot, Cursor) alongside human engineers, rapid generation of code introduces distinct verification failure modes:
1. **Mocking Anti-Patterns & Brittle Assertions**: Agents frequently test internal implementation details (e.g. verifying that a private method was called 3 times) rather than state outcomes. This makes tests brittle across refactorings without catching behavioral regressions.
2. **Premature Test DRYing**: Overly DRY test suites with deeply nested setup fixtures obscure test intent and failure context, making root-cause diagnosis harder.
3. **Execution Latency in Feedback Loops**: Running full-stack Docker Compose or slow multi-container integration suites on every small change destroys agent responsiveness and developer velocity.
4. **Banned Dependencies**: Certain legacy packages (`Moq`, `AutoMapper`) carry supply chain or architectural risks and are strictly banned by workspace governance.

We need an explicit, architectural standard for testing that codifies the testing pyramid distribution, assertion philosophy, and fast agentic feedback loops.

## Decision

We establish the following testing paradigm and agentic verification rules across `OrderFlow`:

### 1. The Test Pyramid Resource Model
Testing effort and test distribution across the repository follow a strict resource allocation:
- **~80% Unit & In-Memory Component Tests (Small / Milliseconds)**:
  - Fast, in-process execution with **zero Docker requirements**.
  - Domain unit tests (`OrderFlow.*.UnitTests`) test pure aggregate logic, state machine guards, and pricing invariants with `xUnit` and `Shouldly`.
  - Service component tests (`OrderFlow.*.ComponentTests`) use the **MassTransit In-Memory Test Harness** (`AddMassTransitTestHarness`) and `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) to test sagas, consumer retry policies, and outbox publishing deterministically without external brokers.
- **~15% Integration Tests (Medium / Seconds)**:
  - Backed by **Testcontainers.NET** (`PostgreSqlContainer` and `RabbitMqContainer`) in `tests/OrderFlow.*.IntegrationTests`.
  - Verifies database migrations, relational schema constraints, and atomic MassTransit EF Core Transactional Outbox persistence against real PostgreSQL 17 and RabbitMQ 4.1 AMQP topologies.
- **~5% End-to-End & Consumer Contract Tests (Large / Minutes)**:
  - **Pact Consumer Contracts**: Fast HTTP contracts between the React frontend (`web/src/api/*.pact.test.ts`) and the backend provider (`OrderFlow.Order.PactTests`).
  - **Schema & Spec Linting**: Spectral for OpenAPI specifications and AsyncAPI CLI for event schemas.
  - **Playwright E2E**: End-to-end customer and staff journeys driving real browser flows against the multi-instance Docker Compose stack, verified with `check-error-queues.mjs` (0 messages in `_error` or `_skipped` queues).

### 2. State Over Interactions
- Tests must assert on **observable state transitions, domain outcomes, and outbox events**, never on internal method invocations or private implementation mechanics.
- Do not mock repositories or internal collaborators when in-memory test doubles or real domain aggregates can execute in milliseconds.
- Verify messaging behavior by asserting that the expected event was published to the in-memory test harness (e.g., `harness.Published.Select<OrderSubmitted>().Any()`), rather than checking if an internal dispatch method was invoked.

### 3. DAMP Over DRY in Test Suites
- Production code prioritizes DRY (Don't Repeat Yourself); test code prioritizes **DAMP (Descriptive And Meaningful Phrases)**.
- Each test scenario must tell a complete, self-contained story that can be read top-to-bottom without tracing through multiple layers of inheritance or shared setup fixtures.
- Duplicate scenario arrangements are acceptable when they preserve readability and make failure diagnostics immediate.

### 4. Banned Packages & Tooling Standard
- **Moq is Prohibited**: Use `NSubstitute` for test doubles. NSubstitute provides clean, strongly typed stubs without telemetry or historical supply chain concerns.
- **AutoMapper is Prohibited**: Use explicit mapping methods or source-generated compile-time mappers (e.g., `Mapperly`). Domain models must not rely on reflection-heavy runtime mapping.

### 5. Fast Agentic Verification Loop
Agents working in `OrderFlow` must follow a disciplined, tiered verification loop:
- **Red-Green-Refactor (TDD)**:
  1. Author a failing test first (**RED**).
  2. Implement the minimal code required to pass (**GREEN**).
  3. Refactor with confidence under passing test coverage (**REFACTOR**).
- **The Prove-It Pattern for Bug Fixes**:
  - Never patch production code for a reported defect without first writing a reproducing test that fails against the current codebase.
- **Focused Execution vs Full Regression**:
  - During development, agents run focused single tests or filtered unit suites:
    ```bash
    dotnet test OrderFlow.slnx --filter "FullyQualifiedName~OrderTests.Cannot_Cancel_Shipped_Order"
    dotnet test OrderFlow.slnx --filter "FullyQualifiedName!~Integration"
    ```
  - Before task completion, agents run `npm run check:fast` (< 10s) and `npm run check:task` (< 60s).
  - Integration and E2E suites run in CI or before full merges via `npm run check:full`.

## Consequences

- Test suites remain resilient to refactoring; changing internal class or method structures will not break behavioral test assertions.
- Agents receive near-instantaneous feedback (< 10s) without incurring container startup overhead during the core TDD cycle.
- Contract drift between frontend and backend is prevented early in memory before triggering expensive E2E runs.
- Consistency across all microservice test projects with clear boundaries between unit, component, integration, and E2E layers.
