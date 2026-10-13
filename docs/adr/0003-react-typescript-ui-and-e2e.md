# 0003. UI and E2E in React + TypeScript

- **Status:** Accepted
- **Date:** 2026-10-14

## Context

The UI exists to make customer journeys testable end to end. We want typed API calls that cannot drift from the OpenAPI contract, and E2E tests driven by the same Gherkin as the specs.

## Decision

- UI: React + TypeScript on Vite. API types generated from `contracts/http/*.openapi.yaml` with openapi-typescript; calls via openapi-fetch.
- UI unit tests: Vitest + React Testing Library.
- E2E: Playwright Test (TypeScript) with playwright-bdd, reading `features/e2e/*.feature`.
- TypeScript is pinned to 5.x: openapi-typescript needs the JS compiler API, which TypeScript 7 (native port) does not ship.

## Consequences

- Two toolchains (.NET and Node) in CI.
- Contract changes surface as TypeScript compile errors in the UI after `npm run gen:api`.
- The UI and E2E reach services through the same path prefixes (`/order-api`, `/notification-api`) in Vite dev and in nginx.
