# Plan NNN: <feature name>

Spec: [spec.md](spec.md)

## Approach

<Two or three sentences: what changes and why this way.>

## Services affected

| Service | Change |
| --- | --- |
| Order | ... |

## Contract changes

| Contract | Change | Breaking? |
| --- | --- | --- |
| `contracts/http/order.openapi.yaml` | ... | No |
| `contracts/events/asyncapi.yaml` | ... | No |

## Data model changes

<Entities, fields, migrations. "None" is a valid answer.>

## Test plan (criterion to layer)

One row per criterion (the first cell starts with its ID); mark at least one layer with `x`.
Name tests exactly, in backticks: `file.feature`, `path/to/file`, `ClassTests` or
`ClassTests.Method`, `File.test.tsx`. `npm run check:specs` fails on a missing row, an unmarked
row or a name that does not exist (spec 009).

| Criterion | Unit | Component | Contract | E2E | Scenario / test |
| --- | --- | --- | --- | --- | --- |
| AC-1 | | x | | | `features/component/....feature` |

## Constitution check

- [ ] Spec first (I)
- [ ] Contracts updated in this PR (II)
- [ ] Every AC automated (III)
- [ ] Events versioned and consumers idempotent (IV)
- [ ] No sleeps, tests own their data (V)
- [ ] No banned packages (VI)

## Risks and decisions

<Anything worth an ADR goes in docs/adr/.>
