# Spec: specs/000-walking-skeleton/spec.md (AC-1, AC-2)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + in-memory bus)
@component @spec-000
Feature: Place an order (Order service)
  The Order service accepts valid orders and announces them with an OrderPlaced event.

  Scenario: A valid order is accepted and announced
    When customer "customer-1" places an order for 2 x "SKU-001"
    Then the order is accepted with status "Pending"
    And an OrderPlaced event is published for that order

  Scenario: An order with a zero quantity is rejected
    When customer "customer-1" places an order for 0 x "SKU-001"
    Then the order is rejected with an error for "lines[0].quantity"
    And no OrderPlaced event is published
