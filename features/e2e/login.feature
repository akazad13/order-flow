# Spec: specs/012-login-and-ownership/spec.md (AC-1, AC-6, AC-7), specs/015-staff-and-token-refresh/spec.md (AC-7), specs/023-tokens-on-the-server/spec.md (AC-1, AC-2, AC-4)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose, with the real Keycloak login page
@e2e @spec-012 @spec-015 @spec-023
Feature: Log in before ordering

  Scenario: A visitor logs in through the shop's login page, then logs out
    Given a visitor is on the order page
    Then they are asked to log in and see no order form
    When they log in as a new customer
    Then they see they are logged in
    When they log out
    Then they are asked to log in and see no order form

  Scenario: The page holds no token, only a cookie its scripts cannot read
    Given a new customer is on the order page
    Then no token is in the page's storage or readable cookies
    And the session cookie is HttpOnly and SameSite=Lax

  Scenario: A login that ended at Keycloak asks to log in again
    Given a new customer is on the order page
    When their session is ended at Keycloak
    And they order 1 of "SKU-001"
    Then they are asked to log in and see no order form
