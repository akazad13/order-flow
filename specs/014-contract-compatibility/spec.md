# Spec 014: Catch breaking contract changes

- **Status:** Done
- **Milestone:** After week 8, fifth follow-up from the [retro](../../docs/retro.md)
- **Builds on:** [Spec 008](../008-consumer-driven-contracts/spec.md) (Pact for the web client), [Spec 009](../009-quality-gates/spec.md) (quality gates in CI, ADR 0007)
- **Links:** [plan.md](plan.md), [tasks.md](tasks.md)

## Problem

The contracts in `contracts/` are linted, so CI knows each one is valid. It does not know whether a change keeps working for the clients and services that use the last version. Pact covers one pair (the web client and the Order API); nothing covers the other HTTP APIs, the events between services, or the live-updates hub. Two past changes show the gap:

- Spec 012 removed `customerId` from the place-order request and required a login for the whole Order API. It was breaking; that was intended, and the only thing that said so was a sentence in the plan.
- Spec 006 changed the Payment API contract without giving it a new version.

## User stories

- As a developer, I want CI to tell me when my contract change can break a client, so that breaking changes are deliberate and visible in review.
- As a team using these contracts, I want each contract's version to tell me whether a new version is safe to adopt.

## Acceptance criteria

- **AC-1 Breaking HTTP changes are caught.** Given a change to an HTTP contract that can break a client (an endpoint, request field or success response removed, a field made required, a type narrowed, a login newly required), when the gate runs, then it fails and names the contract and each breaking change, unless the contract's major version went up.
- **AC-2 Breaking event and live-update changes are caught.** Given a change to an AsyncAPI contract that can break a publisher or a consumer (a channel, message or operation removed; a channel's address changed; a payload field removed, made required or optional, given another type or format, or given other allowed values), when the gate runs, then it fails and names each change, unless the contract's major version went up. Messages already on the broker during a deploy are read by the new version and written by the old one, so both directions count. For events between services (`contracts/events/`), a breaking payload change fails even with a new major version: a new version of the event is published instead (constitution IV); retiring an event needs a new major version.
- **AC-3 Compatible changes pass.** Adding an endpoint, an optional field, a channel, a message or an operation passes, once the version goes up (AC-4).
- **AC-4 Every change gets a new version.** Given a contract's content changed (including a schema file it refers to), when the gate runs, then it fails if the version did not go up; a version never goes down.
- **AC-5 Runs where changes are made.** CI runs the gate on every pull request against its base branch and on every push against the previous commit; locally it compares the working tree with the last commit, or any two revisions.
- **AC-6 It would have caught the past.** Run over this repository's history, the gate reports spec 012's Order API changes as breaking (allowed by that version bump), and fails spec 006's Payment API change for having no new version.
- **AC-7 The gate is tested.** The gate's own tests cover each kind of breaking change, a compatible change, the version rules, and a run against a small git repository.

## Business rules

- **R-1** Versions follow semantic versioning: a breaking change raises the major version; a compatible change raises the minor or patch version. Below 1.0.0, the minor version counts as the major one (the usual rule for 0.x).
- **R-2** The contracts as they stand are declared stable: every contract moves to 1.0.0 in this spec (Q1).

## Edge cases

- A contract that did not exist in the base revision is new: nothing to compare, nothing to fail.
- A contract deleted from the working tree is a breaking change of everything in it.
- A schema file changes but the AsyncAPI document that refers to it does not: the document's content changed, so its version must go up.
- A push that starts a branch has no previous commit: nothing to compare.

## Non-goals

- Comparing against what is deployed, or keeping several major versions served side by side.
- Checking that the code matches the contract (Pact and the contract tests do that, specs 000 and 008).
- Deciding whether a breaking change is wise: the gate makes it visible; review decides.

## Open questions

Decided on the user's behalf (they asked to start this follow-up, 2026-10-15). Revisit in review.

- [x] **Q1 Start every contract at 1.0.0?** (answer: yes. Below 1.0.0 any minor bump allows a breaking change (R-1), and minor bumps happen for every addition here, so the gate would protect almost nothing. The jump to 1.0.0 is itself a major bump, so it passes.)
- [x] **Q2 Which tool for HTTP contracts?** (answer: oasdiff, run from its pinned Docker image (`tufin/oasdiff:v1.15.3`), with "a login is newly required" raised to an error: oasdiff only reports it as information.)
- [x] **Q3 Which tool for AsyncAPI contracts?** (answer: our own check over the resolved documents, using the AsyncAPI parser. `asyncapi diff` reports every payload change in an AsyncAPI 3 document as "unclassified", even removing a required field.)
- [x] **Q4 Is a new allowed value (e.g. a new cancellation reason) breaking for events?** (answer: yes. Consumers decide what to do by the value, and one that does not know it may fail or do the wrong thing. For HTTP, oasdiff's classification stands.)
