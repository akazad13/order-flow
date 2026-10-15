# Spec: specs/015-staff-and-token-refresh/spec.md (AC-1, AC-2, AC-3, AC-4, R-1, R-2), specs/016-event-versions/spec.md (AC-1, AC-2)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + state machine + in-memory bus; logins signed by a test key)
@component @spec-015 @spec-016
Feature: Staff support customers' orders (Order service)
  Staff, a role the identity server gives, can read, watch and cancel any customer's order.
  They act for the customer: the order stays the customer's, and a cancellation is at their request.

  Background:
    Given customer "alice" has placed an order for 1 x "SKU-001"

  Scenario: Staff read any customer's order
    When staff member "sam" reads that order
    Then the answer is that order, placed by customer "alice"

  Scenario: Staff cancel a pending order on the customer's behalf
    When staff member "sam" asks to cancel that order
    Then the request is accepted
    And the order is cancelled with reason "StaffRequested" and detail "Cancelled by our support team at the customer's request."
    And OrderCancelled is published for that order with reason "StaffRequested"

  Scenario: Too late for staff too, once the cancellation window has passed
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    And 25 hours pass
    And staff member "sam" asks to cancel that order
    Then the request is refused as too late
    And no cancellation request is published

  Scenario: Staff watch any customer's order
    Given staff member "sam" is watching that order
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    Then the client is sent that order with status "Confirmed"

  Scenario Outline: Only the staff role makes staff
    When customer "mallory" with the role "<role>" reads that order
    Then the answer is 404, exactly as for an order that does not exist

    Examples:
      | role         |
      | admin        |
      | Staff        |
      | staff-member |

  Scenario: A staff member's own order is theirs
    When staff member "sam" places an order for 1 x "SKU-002"
    Then the order is recorded for customer "sam"
