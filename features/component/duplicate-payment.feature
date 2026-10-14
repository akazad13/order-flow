# Spec: specs/004-duplicate-events/spec.md (AC-1)
# Runs in: tests/OrderFlow.Payment.ComponentTests (Payment service + in-memory bus)
@component @spec-004
Feature: Repeated stock-held news (Payment service)
  An order's payment is decided once; repeats never charge again.

  Scenario: Repeated news charges the customer once
    When stock is reserved twice for an order by customer "customer-1" with a total of 62.00
    Then the customer is charged 62.00 exactly once for that order
    And PaymentSucceeded is published once for that order

  Scenario: Repeated news for a declined customer is declined once
    Given payments for customer "customer-9" are declined with "Card declined"
    When stock is reserved twice for an order by customer "customer-9" with a total of 62.00
    Then PaymentFailed is published once for that order
    And the customer is not charged for that order
