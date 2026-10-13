# Spec: specs/003-payment-failure/spec.md (AC-3, R-1, R-2)
# Runs in: tests/OrderFlow.Inventory.ComponentTests (Inventory service + in-memory bus)
@component @spec-003
Feature: Release stock for a cancelled order (Inventory service)
  Stock held for an order goes back on sale when the order is cancelled, exactly once.

  Background:
    Given the catalog has:
      | sku    | name        | unitPrice | available |
      | TEST-A | Test item A | 18.50     | 10        |

  Scenario: Held stock is released when the order is cancelled
    Given an order holds 3 of "TEST-A"
    When the order is cancelled
    Then StockReleased is published for that order with 3 of "TEST-A"
    And the available stock is:
      | sku    | available |
      | TEST-A | 10        |

  Scenario: Cancelling twice releases the stock only once
    Given an order holds 3 of "TEST-A"
    When the order is cancelled twice
    Then StockReleased is published once for that order
    And the available stock is:
      | sku    | available |
      | TEST-A | 10        |

  Scenario: An order that held nothing releases nothing
    When an order that held nothing is cancelled
    Then no StockReleased is published
    And the available stock is:
      | sku    | available |
      | TEST-A | 10        |
