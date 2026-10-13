# Spec: specs/002-stock-rejection/spec.md (AC-4), specs/003-payment-failure/spec.md (AC-4)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus)
@component @spec-002 @spec-003
Feature: Order cancelled notification (Notification service)
  The customer is told their order was cancelled, and why, in plain words.

  Scenario Outline: The customer is told why the order was cancelled
    When an OrderCancelled event with reason "<reason>" arrives for customer "customer-3"
    Then the inbox of "customer-3" contains "<message>"

    Examples:
      | reason          | message                                               |
      | OutOfStock      | Your order was cancelled: some items are out of stock |
      | PaymentDeclined | Your order was cancelled: your payment was declined   |
