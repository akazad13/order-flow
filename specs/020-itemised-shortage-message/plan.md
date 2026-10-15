# Plan 020: The inbox names the short items

Spec: [spec.md](spec.md)

## Approach

Notification's text for a shortage appends the cancellation's detail, which the Order service already writes per short item ("Only 1 of SKU-002 available, 3 requested.") and the order page already shows (R-1): `OrderCancelledConsumer.TextFor(reason, detail)`. Other reasons keep their words.

## Services affected

| Service | Change |
| --- | --- |
| Notification | Shortage message includes the detail |

## Contract changes

None: `OrderCancelled` v2 already carries the detail.

## Test plan (criterion to layer)

| Criterion | Component | E2E | Scenario / test |
| --- | --- | --- | --- |
| AC-1 Each short item is named | x | | `order-cancelled-notification.feature` "A shortage names every short item, as the order page does" (two items) |
| AC-2 Other reasons unchanged | x | | `order-cancelled-notification.feature` "The customer is told why the order was cancelled" |
| AC-3 End to end | | x | `stock-rejection.feature` (inbox, with the scenario's own SKU) |
| R-1 Same words as the order page | x | x | the scenarios above; `stock-rejection.feature` checks the page's reason and the inbox |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] no sleeps; [x] no banned packages

## Risks and decisions

- **Red first.** The new component scenario failed against the old text.
- **Found: a rebuild that did not replace the containers.** The first E2E run still showed the old message: the Notification containers had not been recreated by my first rebuild (its output was hidden, so why is unknown). Rebuilt with the output visible ("Recreated"), then the scenario passed, the suite 10 of 10, the stress run 100 of 100.
