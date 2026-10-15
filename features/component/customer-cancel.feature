# Spec: specs/006-customer-cancel/spec.md (AC-1, AC-2, AC-3, AC-6), specs/022-cancel-confirmed-orders/spec.md (AC-2)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + state machine + in-memory bus)
@component @spec-006 @spec-022
Feature: Customer cancels an order (Order service)
  The API records the request; the state machine decides. A Pending order is cancelled,
  a Confirmed one stays Confirmed, and a Cancelled one is left as it is.

  Background:
    Given customer "customer-1" has placed an order for 2 x "SKU-001"

  Scenario: Cancelling while the stock is being checked
    When the customer asks to cancel that order
    Then the request is accepted
    And the order is cancelled with reason "CustomerRequested" and detail "Cancelled at the customer's request."
    And OrderCancelled is published for that order with reason "CustomerRequested"

  Scenario: Cancelling while the payment is being taken
    When stock is reserved for that order with a total of 37.00
    And the customer asks to cancel that order
    Then the request is accepted
    And the order is cancelled with reason "CustomerRequested" and detail "Cancelled at the customer's request."
    And OrderCancelled is published for that order with reason "CustomerRequested"

  Scenario: A payment that succeeds after the cancellation does not confirm the order
    When stock is reserved for that order with a total of 37.00
    And the customer has cancelled that order
    And payment succeeds for that order
    Then the order is cancelled with reason "CustomerRequested" and detail "Cancelled at the customer's request."
    And no OrderConfirmed is published

  Scenario: Too late once the order has been confirmed for longer than the window (spec 022)
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And 25 hours pass
    And the customer asks to cancel that order
    Then the request is refused as too late
    And the order has status "Confirmed" and a total of 37.00
    And no cancellation request is published

  Scenario: Asking to cancel a cancelled order changes nothing
    When stock is rejected for that order because "SKU-001" has 1 available
    And the customer asks to cancel that order
    Then the order is returned as already cancelled
    And no cancellation request is published
    And the order is cancelled with reason "OutOfStock" and detail "Only 1 of SKU-001 available, 2 requested."

  Scenario: A request that arrives before the order is tracked waits its turn
    Given an order for 2 x "SKU-001" is stored but not yet tracked
    When the customer asks to cancel that order
    And the order placement is processed
    Then the order is cancelled with reason "CustomerRequested" and detail "Cancelled at the customer's request."
    And OrderCancelled is published for that order with reason "CustomerRequested"

  Scenario: Asking for an unknown order
    When the customer asks to cancel an order that does not exist
    Then the request is refused as not found
