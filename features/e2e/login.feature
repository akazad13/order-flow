# Spec: specs/012-login-and-ownership/spec.md (AC-1, AC-6, AC-7), specs/015-staff-and-token-refresh/spec.md (AC-6, AC-7)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose, with the real Keycloak login page
@e2e @spec-012 @spec-015
Feature: Log in before ordering

  Scenario: A visitor logs in through the shop's login page, then logs out
    Given a visitor is on the order page
    Then they are asked to log in and see no order form
    When they log in as a new customer
    Then they see they are logged in
    When they log out
    Then they are asked to log in and see no order form

  Scenario: The login renews itself before the token expires
    Given the page clock is under test control
    And a new customer is on the order page
    And the page has noted its access token
    When most of the access token's lifetime passes
    Then the page has a new access token
    When the rest of the first token's lifetime passes
    Then they see they are logged in
    And the shop is called with the new token

  Scenario: A login that ended at Keycloak asks to log in again
    Given the page clock is under test control
    And a new customer is on the order page
    When their session is ended at Keycloak
    And most of the access token's lifetime passes
    Then they are asked to log in and see no order form
