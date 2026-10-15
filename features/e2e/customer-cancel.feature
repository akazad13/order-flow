# Spec: specs/006-customer-cancel/spec.md (AC-1, AC-4, AC-7, AC-8), specs/005-live-order-status/spec.md (AC-1), specs/022-cancel-confirmed-orders/spec.md (AC-1, AC-4, AC-6)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose
@e2e @spec-006 @spec-005 @spec-022
Feature: Customer cancels a pending order

  Scenario: Customer cancels while the payment is undecided, and the stock goes back
    Given an item priced 10.00 with 5 in stock
    And a new customer is on the order page
    And their payments are left undecided
    When they order 2 of that item
    And they cancel the order
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "you cancelled it"
    And the status was pushed to the page
    And the item has 5 in stock again
    And their inbox shows, in order:
      | message                                  |
      | We received your order                   |
      | Your order was cancelled at your request |

  Scenario: Customer cancels a confirmed order within the window, and is refunded
    Given an item priced 10.00 with 5 in stock
    And a new customer is on the order page
    When they order 2 of that item
    And they cancel their confirmed order
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "you cancelled it"
    And the item has 5 in stock again
    And their inbox shows, in order:
      | message                                  |
      | We received your order                   |
      | Your order is confirmed                  |
      | Your order was cancelled at your request |
      | Your payment of 20.00 was refunded       |
