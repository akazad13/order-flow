# Spec: specs/004-duplicate-events/spec.md (AC-4)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + state machine + in-memory bus)
@component @spec-004
Feature: Repeated events for a finished order (Order service)
  Once an order is Confirmed or Cancelled, repeats of its earlier events change nothing,
  and are not treated as errors.

  Background:
    Given customer "customer-1" has placed an order for 2 x "SKU-001"

  Scenario: Repeats after confirmation
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And every event for that order arrives again
    Then the order has status "Confirmed" and a total of 37.00
    And OrderConfirmed is published once for that order
    And none of the repeats is treated as an error

  Scenario: Repeats after cancellation
    When stock is reserved for that order with a total of 37.00
    And payment fails for that order with reason "Card declined"
    And every event for that order arrives again
    Then the order is cancelled with reason "PaymentDeclined" and detail "Card declined"
    And OrderCancelled is published once for that order
    And none of the repeats is treated as an error
