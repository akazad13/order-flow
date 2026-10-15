# Spec: specs/024-expired-sessions-cleanup/spec.md (AC-1, AC-2, R-1)
# Runs in: tests/OrderFlow.Gateway.ComponentTests (the gateway in memory, sessions in memory; a clock the test moves)
@component @spec-024
Feature: Expired sessions are cleaned up (Gateway)
  Every hour the gateway removes the sessions whose expiry has passed. A session lasts eight hours,
  and each use moves its expiry forward once half of it has gone.

  Scenario: An expired session is removed at the next cleanup
    Given "alice" is logged in, with an access token that expires in 15 minutes
    When 9 hours pass, and the session cleanup runs
    Then the gateway holds 0 sessions
    And the page asks who is logged in
    And the answer is 401

  Scenario: A session in use stays
    Given "alice" is logged in, with an access token that expires in 15 minutes
    When 5 hours pass, and the session cleanup runs
    And the page asks who is logged in
    And 4 hours pass, and the session cleanup runs
    Then the gateway holds 1 session
    And the page asks who is logged in
    And the answer is 200
