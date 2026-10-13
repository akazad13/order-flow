# Spec: specs/000-walking-skeleton/spec.md (AC-3)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus)
@component @spec-000
Feature: Order received notification (Notification service)
  The customer is told when their order has been received.

  Scenario: The customer is told the order was received
    When an OrderPlaced event arrives for customer "customer-1"
    Then the inbox of "customer-1" contains "We received your order"
