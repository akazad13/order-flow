# Spec: specs/000-walking-skeleton/spec.md (AC-4)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose
@e2e @spec-000
Feature: Walking skeleton (UI to inbox)
  Proves the deployed stack is wired end to end: UI, Order API, broker, Notification.

  Scenario: Customer places an order and is notified
    Given a new customer is on the order page
    When they order 2 of "SKU-001"
    Then they see their order with status "Pending"
    And their inbox shows "We received your order"
