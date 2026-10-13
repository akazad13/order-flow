# Spec: specs/001-place-order/spec.md (AC-2, R-2)
# Runs in: tests/OrderFlow.Payment.ComponentTests (Payment service + in-memory bus)
@component @spec-001
Feature: Take payment for a held order (Payment service)
  Payment charges the order total once the stock for the order is held, and only then.

  Scenario: The order total is charged once
    When stock is reserved for an order with a total of 62.00
    Then the customer is charged 62.00 exactly once for that order
    And PaymentSucceeded is published for that order with an amount of 62.00
