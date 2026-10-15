# Spec: specs/022-cancel-confirmed-orders/spec.md (AC-1, AC-2, AC-3, AC-4, R-1)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + state machine + in-memory bus; a clock the test moves)
@component @spec-022
Feature: Cancel a confirmed order within the window (Order service)
  A confirmed order can still be cancelled for a while (24 hours by default), then it is final.

  Background:
    Given customer "customer-1" has placed an order for 2 x "SKU-001"

  Scenario: A confirmed order says until when it can be cancelled
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    Then the order has status "Confirmed" and a total of 37.00
    And the order can be cancelled until 24 hours after it was confirmed

  Scenario: Cancelling within the window
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And 23 hours pass
    And the customer asks to cancel that order
    Then the request is accepted
    And the order is cancelled with reason "CustomerRequested" and detail "Cancelled at the customer's request."
    And OrderCancelled is published for that order with reason "CustomerRequested"

  Scenario: Staff cancel a confirmed order for the customer within the window
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And 1 hours pass
    And staff member "sam" asks to cancel that order
    Then the request is accepted
    And the order is cancelled with reason "StaffRequested" and detail "Cancelled by our support team at the customer's request."

  Scenario: Too late once the window has passed
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And 25 hours pass
    And the customer asks to cancel that order
    Then the request is refused as too late
    And the order has status "Confirmed" and a total of 37.00
    And no cancellation request is published

  Scenario: A request made in time counts, even if it is handled late
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And a cancellation asked for 23 hours after confirmation is handled 25 hours after it
    Then the order is cancelled with reason "CustomerRequested" and detail "Cancelled at the customer's request."
