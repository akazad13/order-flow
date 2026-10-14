# Spec: specs/003-payment-failure/spec.md (AC-1), specs/002-stock-rejection/spec.md (AC-3)
# Runs in: tests/OrderFlow.Payment.ComponentTests (Payment service + in-memory bus)
@component
Feature: Decline a payment (Payment service)
  A declined payment is announced with its reason, and nothing is charged.

  @spec-003
  Scenario: A declined payment is announced and nothing is charged
    Given payments for customer "customer-9" are declined with "Card declined"
    When stock is reserved for an order by customer "customer-9" with a total of 62.00
    Then PaymentFailed is published for that order with reason "Card declined"
    And the customer is not charged for that order
    And no PaymentSucceeded is published

  @spec-002
  Scenario: A shortage is never charged
    When stock is rejected for an order
    Then no PaymentSucceeded is published
    And no PaymentFailed is published
    And the customer is not charged for that order
