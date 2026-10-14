# Spec: specs/012-login-and-ownership/spec.md (AC-1, AC-4)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus; logins signed by a test key)
@component @spec-012
Feature: Who may read an inbox (Notification service)
  A customer reads their own inbox, and nobody else's.

  Scenario: A customer reads their own inbox
    When an OrderPlaced event arrives for customer "alice"
    Then the inbox of "alice" contains "We received your order"

  Scenario: Not without a login
    When someone who is not logged in reads the inbox of "alice"
    Then the answer is 401

  Scenario: Not someone else's inbox
    When customer "mallory" reads the inbox of "alice"
    Then the answer is 403
