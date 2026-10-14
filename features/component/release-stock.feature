# Spec: specs/003-payment-failure/spec.md (AC-3, R-1, R-2), specs/006-customer-cancel/spec.md (AC-4), specs/016-event-versions/spec.md (AC-3, AC-4)
# Runs in: tests/OrderFlow.Inventory.ComponentTests (Inventory service + in-memory bus)
@component @spec-003 @spec-016
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

  Scenario: A cancellation published as the old version (v1) still releases the stock
    Given an order holds 3 of "TEST-A"
    When the order is cancelled with the old version of the event (v1)
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

  @spec-006
  Scenario: An order cancelled before its placement arrives is never held
    When an order that held nothing is cancelled
    And an order for 3 of "TEST-A" is placed
    Then no StockReserved is published
    And no StockReleased is published
    And the available stock is:
      | sku    | available |
      | TEST-A | 10        |
