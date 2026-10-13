# Spec: specs/002-stock-rejection/spec.md (AC-1, R-1, R-2)
# Runs in: tests/OrderFlow.Inventory.ComponentTests (Inventory service + in-memory bus)
@component @spec-002
Feature: Reject an order when stock is short (Inventory service)
  Inventory holds nothing when any line is short, and lists every short line.

  Background:
    Given the catalog has:
      | sku    | name        | unitPrice | available |
      | TEST-A | Test item A | 18.50     | 10        |
      | TEST-B | Test item B | 6.25      | 1         |

  Scenario: A short line rejects the whole order
    When an order is placed with:
      | sku    | quantity |
      | TEST-A | 2        |
      | TEST-B | 3        |
    Then StockRejected is published for that order with shortages:
      | sku    | requested | available |
      | TEST-B | 3         | 1         |
    And no StockReserved is published
    And the available stock is:
      | sku    | available |
      | TEST-A | 10        |
      | TEST-B | 1         |

  Scenario: An unknown item counts as a shortage with nothing available
    When an order is placed with:
      | sku     | quantity |
      | NO-SUCH | 1        |
      | TEST-B  | 5        |
    Then StockRejected is published for that order with shortages:
      | sku     | requested | available |
      | NO-SUCH | 1         | 0         |
      | TEST-B  | 5         | 1         |
