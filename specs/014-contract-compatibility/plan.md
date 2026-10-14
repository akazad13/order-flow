# Plan 014: Catch breaking contract changes

Spec: [spec.md](spec.md)

## Approach

A new gate, `scripts/check-contracts.mjs` (`npm run check:contracts`), next to the spec gate (spec 009). It writes `contracts/` as it was at the base revision (the merge base of `--base` and the head) into a temp folder, and compares each contract with the head (the working tree, or `--head <rev>`):

| Contract | How breaking changes are found |
| --- | --- |
| OpenAPI (`contracts/http/`) | `oasdiff changelog`, from the pinned Docker image `tufin/oasdiff:v1.15.3`. Warnings and errors are breaking; so are `api-global-security-added` and `api-security-added`, which oasdiff only reports as information (Q2; its severity override did not raise them). |
| AsyncAPI (`contracts/events/`, `contracts/realtime/`) | Our own rules over the documents as the AsyncAPI parser resolves them (schema files included): channels, messages and operations removed, addresses and actions changed, and payload fields removed, made required or optional, given another type, format, `const` or allowed values, or no longer allowing other fields; recursing into properties, array items and `oneOf`/`anyOf`/`allOf` (Q3). |

Then the version rules (R-1, AC-4): content changed (parsed, ignoring `info.version`) means the version must go up; it never goes down; breaking changes need a new major version, where below 1.0.0 the minor counts as the major. On top of that, constitution IV for events between services: a breaking edit to a payload in `contracts/events/` fails whatever the version says ("publish a new version of the event instead"); retiring a message only needs a new major version.

Problems fail the gate; breaking changes a new major version allowed are printed as notes, so review sees them.

## Services affected

| Area | Change |
| --- | --- |
| Gates | `scripts/check-contracts.mjs` and its tests; `npm run check:contracts`; `@asyncapi/parser` and `yaml` as direct dependencies (already installed through the AsyncAPI CLI) |
| Contracts | Every contract moves to 1.0.0 (Q1); no other change |
| CI | The contracts job fetches full history and runs the gate against the pull request's base branch, or the commit before a push |
| Docs | ADR 0009 (contract versioning); constitution II; README; retro |

## Contract changes

Versions only: `order.openapi.yaml`, `notification.openapi.yaml`, `inventory.openapi.yaml`, `payment.openapi.yaml`, `events/asyncapi.yaml` and `realtime/order-updates.asyncapi.yaml` are all 1.0.0. The gate passes this change (from 0.x, a new major version).

## Test plan (criterion to layer)

| Criterion | Unit | Repo run | History | Scenario / test |
| --- | --- | --- | --- | --- |
| AC-1 Breaking HTTP changes are caught | x | x | x | `check-contracts.test.mjs` ("httpBreaks", "fails a breaking HTTP change found by the real oasdiff"); spec 012 in history |
| AC-2 Breaking event and live-update changes are caught | x | x | x | `check-contracts.test.mjs` ("schemaBreaks", "asyncapiBreaks", "fails a breaking change to a published event", "allows a breaking change to other AsyncAPI contracts"); spec 006 in history |
| AC-3 Compatible changes pass | x | x | | `check-contracts.test.mjs` ("new optional field", "passes a compatible change with a new minor version") |
| AC-4 Every change gets a new version | x | x | x | `check-contracts.test.mjs` ("versions", "fails a schema file change when the document keeps its version"); spec 006 in history |
| AC-5 Runs where changes are made | | x | | `check-contracts.test.mjs` ("compares committed revisions, from their merge base", "a base that is not a commit"); `.github/workflows/ci.yml` |
| AC-6 It would have caught the past | | | x | `npm run check:contracts -- --base <rev> --head <rev>` over each contract-changing commit (Risks below) |
| AC-7 The gate is tested | x | x | | `check-contracts.test.mjs`, in `npm run test:scripts` |

## Constitution check

- [x] Spec first; [x] contracts change only in version; [x] every AC automated; [x] no sleeps; [x] no banned packages; [x] constitution II now names the gate

## Risks and decisions

- **`asyncapi diff` cannot classify AsyncAPI 3 payloads.** Removing a required field from OrderPlaced came back "unclassified", like the parser's own bookkeeping changes, so the CLI we already had could not do the job (Q3).
- **oasdiff under-rates a new login requirement.** It reports `api-global-security-added` as information, and `--severity-levels` brings it into the breaking report but leaves it at that level. The gate uses the changelog and decides itself.
- **The gate needs Docker** (for oasdiff), like the integration tests and E2E. GitHub's Ubuntu runners have it.
- **History, run with the final rules** (AC-6):
  - Spec 012 (`a1b75c1` to `a394d54`): `customerId` removed and a login required on the Order API, and a login required on the Notification API: breaking, allowed by the 0.x bumps.
  - Spec 006 (`51a3c32` to `371691a`) fails three ways. The Payment API changed without a new version. The live-updates contract changed without a new version, and the change was breaking (a new cancellation reason). And the published `OrderCancelled` v1 event gained the value `CustomerRequested`, an edit constitution IV forbids. Nobody noticed any of these at the time.
  - Weeks 1 and 4: new order statuses (Confirmed, Cancelled) in responses are breaking for HTTP clients by oasdiff's rules; allowed by their bumps.
- **Red first for the gate's tests:** with the merge base replaced by the base branch's tip, the "compares committed revisions" test fails (the feature looks like it lowered its version); with the rule for published events removed, the constitution IV test fails.
- **What a new event version looks like is not settled here.** The gate refuses the edit; a spec that needs one decides how v1 and v2 live side by side (new message, its own schema file, consumers moved before v1 is retired).
