# Spec: specs/004-duplicate-events/spec.md (AC-5)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus)
@component @spec-004
Feature: Repeated events for the customer's inbox (Notification service)
  The customer sees each update about an order once.

  Scenario: A repeated confirmation is shown once
    When an OrderConfirmed event for customer "customer-4" arrives twice
    Then the inbox of "customer-4" lists, in order:
      | message                 |
      | Your order is confirmed |
