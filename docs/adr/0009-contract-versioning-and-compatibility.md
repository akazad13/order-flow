# 0009. Contracts follow semantic versioning, checked in CI

- **Status:** Accepted
- **Date:** 2026-10-15
- **Spec:** [014](../../specs/014-contract-compatibility/spec.md)

## Context

Contracts were linted (valid) but never compared with their last version (compatible). Versions went up for additions and breaking changes alike, and sometimes not at all, so a version number said nothing about whether a new contract was safe to adopt. Pact checks one client against one API; the other APIs, the events and the live-updates hub had no such check.

## Decision

- **Semantic versioning for every contract**, starting at 1.0.0: a breaking change raises the major version; any other change raises the minor or patch version; a change without a new version fails.
- **Events between services are never edited in a breaking way** (constitution IV): a new version of the event is published instead. Retiring an event needs a new major version.
- **A gate in CI** (`npm run check:contracts`) compares each contract with the pull request's base branch, or with the commit before a push: oasdiff (pinned Docker image) for OpenAPI, our own rules for AsyncAPI over parser-resolved documents. Allowed breaking changes are printed, so review sees them.

## Consequences

- A version number now says something: same major, safe to adopt.
- "Breaking" is the tools' judgment, made stricter on purpose: a new login requirement and a new allowed value in an event both count. A deliberate breaking change costs one version bump (or, for events, a new event version) and is visible in review.
- The gate needs Docker and the full git history in CI.
- Not covered: what is deployed, and serving two major versions side by side.
