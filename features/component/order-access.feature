# Spec: specs/012-login-and-ownership/spec.md (AC-1, AC-2, AC-3, AC-5, R-1, R-2)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + in-memory bus; logins signed by a test key)
@component @spec-012
Feature: Who may place, read and cancel orders (Order service)
  Orders need a login, belong to whoever placed them, and are invisible to everyone else.

  Scenario: An order is recorded for the logged-in customer
    When customer "alice" places an order for 1 x "SKU-001"
    Then the order is accepted with status "Pending"
    And the order is recorded for customer "alice"

  Scenario Outline: Nothing without a login
    When someone who is not logged in tries to <action>
    Then the answer is 401

    Examples:
      | action          |
      | place an order  |
      | read an order   |
      | cancel an order |

  Scenario: Another customer's order looks like no order at all
    Given customer "alice" has placed an order for 1 x "SKU-001"
    When customer "mallory" reads that order
    Then the answer is 404, exactly as for an order that does not exist

  Scenario: Another customer cannot cancel the order
    Given customer "alice" has placed an order for 1 x "SKU-001"
    When customer "mallory" asks to cancel that order
    Then the answer is 404
    And no cancellation request is published

  Scenario Outline: Logins that do not count
    When customer "alice" places an order with a token that is <problem>
    Then the answer is 401
    And no OrderPlaced event is published

    Examples:
      | problem               |
      | expired               |
      | signed by another key |
      | from another issuer   |
      | for another audience  |
