# Spec: specs/001-place-order/spec.md (AC-1, AC-6)
# Runs in: tests/OrderFlow.Inventory.ComponentTests (Inventory service + in-memory bus)
@component @spec-001
Feature: Hold stock for an order (Inventory service)
  Inventory holds the stock for every line of a new order, prices it, and announces it.

  Background:
    Given the catalog has:
      | sku    | name        | unitPrice | available |
      | TEST-A | Test item A | 18.50     | 10        |
      | TEST-B | Test item B | 6.25      | 100       |

  Scenario: Stock is held and the order is priced
    When an order is placed with:
      | sku    | quantity |
      | TEST-A | 2        |
      | TEST-B | 4        |
    Then StockReserved is published for that order with a total of 62.00
    And the reserved lines are:
      | sku    | quantity | unitPrice |
      | TEST-A | 2        | 18.50     |
      | TEST-B | 4        | 6.25      |
    And the available stock is:
      | sku    | available |
      | TEST-A | 8         |
      | TEST-B | 96        |
