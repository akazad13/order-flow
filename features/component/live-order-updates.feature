# Spec: specs/005-live-order-status/spec.md (AC-1, AC-2)
# Runs in: tests/OrderFlow.Order.ComponentTests (Order service + SignalR hub over the test server + in-memory bus)
@component @spec-005
Feature: Live order updates (Order service)
  A client watching an order is sent the order when it becomes Confirmed or Cancelled,
  and nothing about other orders.

  Background:
    Given customer "customer-1" has placed an order for 2 x "SKU-001"
    And a client is watching that order

  Scenario: The watcher is sent the confirmed order
    When stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    Then the client is sent that order with status "Confirmed"
    And the update is exactly what reading the order returns

  Scenario: The watcher is sent the cancelled order
    When stock is rejected for that order because "SKU-001" has 1 available
    Then the client is sent that order with status "Cancelled"
    And the update is exactly what reading the order returns

  Scenario: The watcher is sent nothing about other orders
    Given customer "customer-2" has placed another order for 1 x "SKU-002"
    When that other order is confirmed
    And stock is reserved for that order with a total of 37.00
    And payment succeeds for that order
    Then the client is sent that order with status "Confirmed"
    And the client was sent nothing else
