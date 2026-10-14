# Spec: specs/017-admin-manages-stock/spec.md (AC-4, AC-5)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose, two browsers, real Keycloak logins
@e2e @spec-017
Feature: Admins put items on sale

  Scenario: An admin puts a new item on sale, and customers can order what is in stock
    Given a new customer is on the order page
    And an admin puts a new item on sale priced 4.00 with 2 in stock
    When they order 3 of that item
    Then they see their order with status "Cancelled" within 10 seconds
    And they see the reason "some items are out of stock"
    When they order 2 of that item
    Then they see their order with status "Confirmed" within 10 seconds
    And they see a total of 8.00
    And the admin's stock page shows 0 of that item available
