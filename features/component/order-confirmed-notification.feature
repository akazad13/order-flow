# Spec: specs/001-place-order/spec.md (AC-4)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus)
@component @spec-001
Feature: Order confirmed notification (Notification service)
  The customer is told when their order is confirmed.

  Scenario: The customer is told the order is confirmed
    When an OrderConfirmed event arrives for customer "customer-1"
    Then the inbox of "customer-1" contains "Your order is confirmed"

  Scenario: Messages are listed in the order things happened, not the order they arrived
    Given an order for customer "customer-2" was placed and then confirmed
    When the confirmation reaches the Notification service before the order placement
    Then the inbox of "customer-2" lists, in order:
      | message                 |
      | We received your order  |
      | Your order is confirmed |
