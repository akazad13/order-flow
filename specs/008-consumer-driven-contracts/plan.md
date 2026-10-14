# Plan 008: Consumer-driven contract between the web UI and the Order API

Spec: [spec.md](spec.md)

## Approach

**Consumer side (web):** `@pact-foundation/pact` (V4) tests call the real UI API client (`createOrderApi`) against Pact's mock server and write `pacts/orderflow-web-orderflow-order-api.json`.

**Provider side (.NET):** `PactNet` replays the file against the real Order service started on Kestrel by `WebApplicationFactory` (in-memory storage, MassTransit test harness instead of RabbitMQ). Provider states are set up through a test-only `POST /provider-states` endpoint added by the test host (not the service), which creates orders through the domain and returns `{ "orderId": ... }` for injection into request paths.

```text
web (vitest + Pact mock server) --writes--> pacts/*.json --replayed by--> PactNet --> Order API (Kestrel, test host)
```

## Changes

| Area | Change |
| --- | --- |
| web | `createOrderApi(baseUrl)` factory in `src/api/clients.ts` (the app keeps using `/order-api`); `src/api/order-api.pact.test.ts`; `@pact-foundation/pact` |
| tests | New `OrderFlow.Order.PactTests` (PactNet, Kestrel host, provider states), trait `Category=Contract` |
| pacts/ | The committed contract file |
| CI | Web job: `git diff --exit-code pacts`; .NET job runs the verification (no Docker needed) |

## Contract changes

None to OpenAPI or events. New artifact: the pact file.

## Test plan

| Criterion | Test |
| --- | --- |
| AC-1, AC-2 | `order-api.pact.test.ts` (6 interactions, type/format matchers) |
| AC-3 | `OrderApiPactVerificationTests.Order_api_honours_the_web_ui_contract` |
| AC-4 | CI steps (pact diff, verification in the .NET job) |
| AC-5 | Manual mutation once: rename `status` in `OrderResponse`, run verification, see it fail; revert (recorded in tasks) |

## Risks and decisions

- Pact's native libraries (Rust FFI) on Windows and Linux: both packages bundle them.
- Provider-state injection (`fromProviderState`) needs the state endpoint to return the generated ID; verified by the confirmed/cancelled interactions.
