# Spec: specs/002-stock-rejection/spec.md (AC-4, AC-5)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose
@e2e @spec-002
Feature: Reject an order when stock is short

  Scenario: Customer sees the order cancelled for a shortage and is told
    Given an item priced 10.00 with 1 in stock
    And a new customer is on the order page
    When they order 2 of that item
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "some items are out of stock"
    And their inbox shows, in order:
      | message                                               |
      | We received your order                                |
      | Your order was cancelled: some items are out of stock |
