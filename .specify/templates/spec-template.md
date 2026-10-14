# Spec NNN: <feature name>

- **Status:** Draft | Clarifying | Ready | In progress | Done
- **Milestone:** <week / milestone>
- **Links:** plan.md, tasks.md, PRs

## Problem

<Who has the problem, and what happens today. No technology words.>

## User stories

- As a <role>, I want <capability>, so that <outcome>.

## Acceptance criteria

Number every criterion; tests and Gherkin scenarios reference these IDs.

- **AC-1** Given <context>, when <action>, then <observable outcome>.
- **AC-2** ...

## Edge cases

Most defects in this repo came from message orderings (docs/retro.md). For each event the
feature sends or receives, ask: what if it arrives twice, arrives before the event it follows,
arrives at the same moment as a competing one, or arrives before the receiver knows the order?

- <What happens when ...>

## Non-goals

- <Explicitly out of scope for this spec.>

## Open questions

Resolve every question before the status moves to Ready.

- [ ] <question> (answer: ...)
