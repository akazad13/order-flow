# Spec: specs/001-place-order/spec.md (AC-4, AC-5, AC-6), specs/005-live-order-status/spec.md (AC-6)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose
@e2e @spec-001 @spec-005
Feature: Place an order and see it confirmed
  The happy path across every service: stock held, payment taken, order confirmed, customer told.

  Scenario: Customer sees the order confirmed and is told
    Given an item priced 18.50 with 10 in stock
    And a new customer is on the order page
    When they order 2 of that item
    Then they see their order with status "Confirmed" within 10 seconds
    And they see a total of 37.00
    And the page read the order at most once after placing it
    And their inbox shows, in order:
      | message                 |
      | We received your order  |
      | Your order is confirmed |
