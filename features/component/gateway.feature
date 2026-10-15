# Spec: specs/023-tokens-on-the-server/spec.md (AC-1, AC-2, AC-3, AC-4, AC-5, R-1)
# Runs in: tests/OrderFlow.Gateway.ComponentTests (the gateway in memory; Keycloak's token endpoint and the services are fakes; a clock the test moves)
@component @spec-023
Feature: The gateway keeps the tokens (Gateway)
  The browser holds only a session cookie; the gateway adds the session's access token to the
  calls it forwards, renews it before it expires, and refuses calls another site could make.

  Scenario: Without a session, calls are refused and nothing is forwarded
    When the page calls the Order API without a session
    Then the answer is 401
    And nothing was forwarded

  Scenario: A call is forwarded with the session's access token, and without the browser's cookies
    Given "alice" is logged in, with an access token that expires in 15 minutes
    When the page calls the Order API
    Then the answer is 200
    And the Order service received the bearer token "access-1"
    And the Order service received no cookie

  Scenario: The page learns who is logged in, and never a token
    Given "sam" is logged in with the roles "staff", with an access token that expires in 15 minutes
    When the page asks who is logged in
    Then it is told "sam", staff but not admin
    And the answer contains no token

  Scenario: The session cookie cannot be read by scripts, nor sent by other sites
    Given "alice" is logged in, with an access token that expires in 15 minutes
    Then the session cookie is HttpOnly and SameSite=Lax, and not Secure-only
    And no token is in the session cookie

  Scenario: In production, the session cookie goes over HTTPS only
    Given the gateway sends its cookie over HTTPS only
    And "alice" is logged in, with an access token that expires in 15 minutes
    Then the session cookie is Secure-only

  Scenario: A token about to expire is renewed first, and kept for the next call
    Given "alice" is logged in, with an access token that expires in 15 minutes
    And Keycloak will renew it as "access-2"
    When 14 minutes and 30 seconds pass
    And the page calls the Order API
    And the page calls the Order API
    Then the Order service received the bearer tokens "access-2" and "access-2"
    And Keycloak was asked to renew once, with the refresh token "refresh-1"

  Scenario: A renewal that fails ends the session
    Given "alice" is logged in, with an access token that expires in 15 minutes
    And Keycloak will refuse to renew it
    When 15 minutes pass
    And the page calls the Order API
    Then the answer is 401
    And nothing was forwarded
    When the page asks who is logged in
    Then the answer is 401

  Scenario: A call without the page's header is refused, even with the session cookie
    Given "alice" is logged in, with an access token that expires in 15 minutes
    When another site makes the page call the Order API, without the X-CSRF header
    Then the answer is 403
    And nothing was forwarded

  Scenario Outline: The live connection only from the shop's own origin
    Given "alice" is logged in, with an access token that expires in 15 minutes
    When the page opens the live connection from "<origin>"
    Then the answer is <status>

    Examples:
      | origin                | status |
      | http://localhost:5180 | 200    |
      | https://evil.example  | 403    |

  Scenario: Logging in starts Keycloak's login, with PKCE, returning to the gateway
    When the page asks to log in
    Then it is sent to Keycloak's login with a PKCE challenge, returning to "/bff/signin-oidc"

  Scenario: Logging out ends the session and gives Keycloak's logout address
    Given "alice" is logged in, with an access token that expires in 15 minutes
    When the page logs out
    Then it is given Keycloak's logout address, with the ID token as a hint
    When the page asks who is logged in
    Then the answer is 401
