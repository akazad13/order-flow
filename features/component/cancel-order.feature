# Spec: specs/002-stock-rejection/spec.md (AC-2), specs/003-payment-failure/spec.md (AC-2)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + state machine + in-memory bus)
@component
Feature: Cancel an order (Order service)
  The Order service cancels an order when its stock is short or its payment fails.

  Background:
    Given customer "customer-1" has placed an order for 2 x "SKU-001"

  @spec-002
  Scenario: A shortage cancels the order
    When stock is rejected for that order because "SKU-001" has 1 available
    Then the order is cancelled with reason "OutOfStock" and detail "Only 1 of SKU-001 available, 2 requested."
    And OrderCancelled is published for that order with reason "OutOfStock"

  @spec-003
  Scenario: A declined payment cancels the order
    When stock is reserved for that order with a total of 37.00
    And payment fails for that order with reason "Card declined"
    Then the order is cancelled with reason "PaymentDeclined" and detail "Card declined"
    And OrderCancelled is published for that order with reason "PaymentDeclined"

  @spec-003
  Scenario: Payment failure news that overtakes the stock hold still cancels the order
    When payment fails before stock is reserved for that order
    Then the order is cancelled with reason "PaymentDeclined" and detail "Card declined"
