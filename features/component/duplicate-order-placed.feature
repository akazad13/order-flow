# Spec: specs/004-duplicate-events/spec.md (AC-2, AC-3)
# Runs in: tests/OrderFlow.Inventory.ComponentTests (Inventory service + in-memory bus)
@component @spec-004
Feature: Repeated order placements (Inventory service)
  The first stock decision for an order wins; repeats change nothing and announce nothing.

  Background:
    Given the catalog has:
      | sku    | name        | unitPrice | available |
      | TEST-A | Test item A | 18.50     | 10        |
      | TEST-B | Test item B | 6.25      | 1         |

  Scenario: The same order placement twice holds stock once
    When an order for 3 of "TEST-A" is placed twice
    Then StockReserved is published once for that order
    And the available stock is:
      | sku    | available |
      | TEST-A | 7         |

  Scenario: A rejected order stays rejected after the stock is refilled
    When an order for 2 of "TEST-B" is placed
    And the stock of "TEST-B" is refilled to 10
    And the same order placement arrives again
    Then StockRejected is published once for that order
    And no StockReserved is published
    And the available stock is:
      | sku    | available |
      | TEST-B | 10        |

  Scenario: A released order is not held again
    Given an order holds 3 of "TEST-A"
    When the order is cancelled
    And the same order placement arrives again
    Then StockReserved is published once for that order
    And the available stock is:
      | sku    | available |
      | TEST-A | 10        |
