# Spec: specs/006-customer-cancel/spec.md (AC-5), Q4 (test switch)
# Runs in: tests/OrderFlow.Payment.ComponentTests (Payment service + in-memory bus)
@component @spec-006
Feature: Settle the payment of a cancelled order (Payment service)
  A charge already taken is refunded once; a payment not yet decided is never taken.

  Scenario: A charged order that is cancelled is refunded
    When stock is reserved for an order by customer "customer-1" with a total of 37.00
    And that order is cancelled at the customer's request
    Then PaymentRefunded is published for that order with an amount of 37.00
    And the customer is refunded 37.00 exactly once for that order

  Scenario: A repeated cancellation refunds once
    When stock is reserved for an order by customer "customer-1" with a total of 37.00
    And that order is cancelled at the customer's request twice
    Then PaymentRefunded is published once for that order
    And the customer is refunded 37.00 exactly once for that order

  Scenario: An order cancelled before it is charged is never charged
    When that order is cancelled at the customer's request
    And stock is reserved for an order by customer "customer-1" with a total of 37.00
    Then the customer is not charged for that order
    And no PaymentSucceeded is published
    And no PaymentRefunded is published

  Scenario: A declined payment has nothing to refund
    Given payments for customer "customer-9" are declined with "Card declined"
    When stock is reserved for an order by customer "customer-9" with a total of 37.00
    And that order is cancelled because the payment was declined
    Then no PaymentRefunded is published

  Scenario: A stalled customer's payment stays undecided
    Given payments for customer "customer-5" are stalled
    When stock is reserved for an order by customer "customer-5" with a total of 37.00
    Then no PaymentSucceeded is published
    And no PaymentFailed is published
    And the customer is not charged for that order
