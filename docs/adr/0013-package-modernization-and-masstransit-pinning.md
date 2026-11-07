# 0013. Package Modernization and MassTransit Pinning

- **Status:** Accepted
- **Date:** 2026-11-07

## Context

During the workspace-wide dependency update cycle, all .NET packages were reviewed and updated across the solution. `OrderFlow` uses Central Package Management (`Directory.Packages.props`) targeting .NET 10.

During the upgrade, MassTransit was evaluated. MassTransit v9 introduced major breaking API changes and licensing transitions away from the open-source license model preserved in v8. Upgrading to v9 violated existing architectural invariants recorded in [ADR-0002](0002-messaging-rabbitmq-masstransit-v8.md) and introduced compilation errors across saga state machines and outbox handlers under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

Additionally, other infrastructure packages (PostgreSQL Npgsql, Testcontainers, Microsoft.Extensions, Aspire) have been successfully upgraded to their latest stable releases compatible with .NET 10.

## Decision

1. **Reaffirm MassTransit v8 Pinning**: MassTransit packages remain strictly pinned to `8.5.11` in `Directory.Packages.props`. MassTransit v9+ must not be introduced into the repository without an explicit architectural review and licensing clearance.
2. **Modernize Non-Breaking Dependencies**: All compatible .NET 10 and testing libraries (Npgsql, Testcontainers, Microsoft.AspNetCore, FluentAssertions) are upgraded to their latest supported versions.
3. **CI & Spec Verification**: All contract tests, Pact verifications, and architectural gates (`check-contracts.mjs`, `check-specs.mjs`) continue to enforce these boundaries.

## Consequences

- The system remains fully compliant with open-source licensing without commercial subscription requirements.
- Existing saga state machine definitions and EF Core transactional outbox implementations remain stable and fully functional without code refactoring.
- Future automated dependency updates must exclude MassTransit packages from major version bumps.
