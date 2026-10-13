# Spec: specs/001-place-order/spec.md (AC-3, R-3)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + state machine + in-memory bus)
@component @spec-001
Feature: Confirm an order (Order service)
  The Order service confirms an order only after its stock is held and its payment succeeded.

  Background:
    Given customer "customer-1" has placed an order for 2 x "SKU-001"

  Scenario: The order is confirmed after stock is held and payment succeeds
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    Then the order has status "Confirmed" and a total of 37.00
    And OrderConfirmed is published for that order with a total of 37.00

  Scenario: Holding stock alone does not confirm the order
    When stock is reserved for that order with a total of 37.00
    Then the order has status "Pending" and a total of 37.00
    And no OrderConfirmed is published

  Scenario: Payment news that overtakes the stock hold still confirms the order
    When payment succeeds before stock is reserved for that order, with a total of 37.00
    Then the order has status "Confirmed" and a total of 37.00
    And OrderConfirmed is published for that order with a total of 37.00

  Scenario: Payment news that arrives before the order is being tracked still confirms it
    Given an order for 2 x "SKU-002" is stored but not yet tracked
    When payment succeeds, then stock is reserved, then the order placement is processed, with a total of 90.00
    Then the order has status "Confirmed" and a total of 90.00
