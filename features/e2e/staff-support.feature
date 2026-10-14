# Spec: specs/015-staff-and-token-refresh/spec.md (AC-2, AC-5, AC-8), specs/016-event-versions/spec.md (AC-1, AC-6)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose, two browsers, real Keycloak logins
@e2e @spec-015 @spec-016
Feature: Staff cancel a customer's order for them

  Scenario: Support cancels a customer's pending order at their request
    Given a new customer is on the order page
    And their payments are left undecided
    When they order 1 of "SKU-001"
    And a member of staff looks up that order
    And the member of staff cancels it for the customer
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "our support team cancelled it at your request"
    And they see "Cancelled by our support team at the customer's request."
    And their inbox shows "Your order was cancelled by our support team at your request"
