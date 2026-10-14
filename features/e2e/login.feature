# Spec: specs/012-login-and-ownership/spec.md (AC-1, AC-6, AC-7)
# Runs in: e2e/ (Playwright + playwright-bdd) against docker compose, with the real Keycloak login page
@e2e @spec-012
Feature: Log in before ordering

  Scenario: A visitor logs in through the shop's login page, then logs out
    Given a visitor is on the order page
    Then they are asked to log in and see no order form
    When they log in as a new customer
    Then they see they are logged in
    When they log out
    Then they are asked to log in and see no order form
