# 0010. A new version of an event is a new message, read before it is published

- **Status:** Accepted
- **Date:** 2026-10-15
- **Spec:** [016](../../specs/016-event-versions/spec.md)

## Context

Constitution IV says a breaking change to an event is a new version, not an edit, and since spec 014 the contract gate refuses the edit. RabbitMQ delivers by exchange: a message published to an exchange no queue is bound to is dropped, and during a deploy old and new copies of a service run side by side (spec 013). A new version therefore has to be named, routed and rolled out so that no reader misses an event and none handles one twice.

## Decision

**Names.** Version N of event `Name` (N ≥ 2):

| Thing | Version 1 | Version N |
| --- | --- | --- |
| C# record | `Name` | `NameVN` (v1 gets `ToVN()`, the upcast) |
| Message URN | `orderflow:name:v1` | `orderflow:name:vN` |
| Exchange (`EntityName`) | `name` | `name-vN` |
| Schema file | `name.v1.json` | `name.vN.json` |
| AsyncAPI channel, message | `name`, `Name` | `nameVN`, `NameVN` |
| AsyncAPI operations | `publishName`, `<service>ReceivesName` | `publishNameVN`, `<service>ReceivesNameVN` |

**Readers.** A service keeps one consumer class for the event and adds the new version to it (`IConsumer<NameVN>, IConsumer<Name>`), so its one queue is bound to both exchanges. The old version is upcast to the new shape and handled by the same code.

**Releases**, in this order:

1. **Readers first.** Every service that reads the old version also reads the new one. The events contract gains the new message (minor version).
2. **Switch the publisher.** It publishes the new version instead of the old one: never both, or readers of both act twice. The contract loses `publishName` (major version: anyone reading only the old version would stop hearing about the event).
3. **Retire the old version**, once every copy of the publisher runs step 2 and each reader's queue has been empty at least once since (nothing old can be left). Readers drop the old interface and the upcast; the contract loses the old channel and message (major version).

The gate enforces step 1 before step 2: while `publishNameVN` exists, every `<service>ReceivesName` needs a `<service>ReceivesNameVN`. It already refuses breaking edits to a published version (spec 014) and needs a major version for step 2 and step 3.

## Consequences

- One queue per reader keeps working through the change; only exchanges and bindings are added.
- A version change touches every reader, even those that ignore the changed field (Inventory and Payment, for `OrderCancelled` v2): otherwise they would stop receiving the event.
- When all services are deployed together (compose, CI), steps 1 and 2 can ship in one release, because every reader is listening before anything is published. Deployed piecemeal, they are separate releases.
- Live notices (spec 013, R-2) need not read the old version: a missed push is recovered by reading the order again.
- Retiring is a release of its own, with a condition someone must check; until then each reader carries a few lines for the old version.
