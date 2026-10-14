# Spec: specs/006-customer-cancel/spec.md (AC-1, AC-4, AC-7, AC-8), specs/005-live-order-status/spec.md (AC-1)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose
@e2e @spec-006 @spec-005
Feature: Customer cancels a pending order

  Scenario: Customer cancels while the payment is undecided, and the stock goes back
    Given an item priced 10.00 with 5 in stock
    And a new customer is on the order page
    And their payments are left undecided
    When they order 2 of that item
    And they cancel the order
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "cancelled at your request"
    And the status was pushed to the page
    And the item has 5 in stock again
    And their inbox shows, in order:
      | message                                  |
      | We received your order                   |
      | Your order was cancelled at your request |
