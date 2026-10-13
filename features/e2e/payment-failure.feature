# Spec: specs/003-payment-failure/spec.md (AC-3, AC-4, AC-5)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose
@e2e @spec-003
Feature: Cancel the order and release stock when payment fails

  Scenario: Customer sees the order cancelled for a declined payment, and the stock goes back
    Given an item priced 10.00 with 5 in stock
    And a new customer is on the order page
    And their payments will be declined
    When they order 2 of that item
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "your payment was declined"
    And the item has 5 in stock again
    And their inbox shows, in order:
      | message                                             |
      | We received your order                              |
      | Your order was cancelled: your payment was declined |
