# Spec: specs/006-customer-cancel/spec.md (AC-7)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus)
@component @spec-006
Feature: Payment refunded notification (Notification service)
  The customer is told when the payment for a cancelled order was refunded.

  Scenario: The customer is told the amount refunded
    When a PaymentRefunded event of 37.00 arrives for customer "customer-4"
    Then the inbox of "customer-4" contains "Your payment of 37.00 was refunded"
