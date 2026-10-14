# Spec: specs/017-admin-manages-stock/spec.md (AC-1, AC-2, AC-3, AC-6, R-2)
# Runs in: tests/OrderFlow.Inventory.ComponentTests (Inventory service + in-memory bus; logins signed by a test key)
@component @spec-017
Feature: Admins manage stock (Inventory service)
  Only admins set an item's name, price and quantity available; anyone may look at the catalog.

  Background:
    Given the catalog has:
      | sku    | name        | unitPrice | available |
      | TEST-A | Test item A | 18.50     | 10        |

  Scenario: An admin changes an item
    When admin "ada" sets "TEST-A" to name "Test item A+", unit price 19.00 and 25 available
    Then the answer is 200
    And anyone looking at the catalog sees "TEST-A" as "Test item A+" at 19.00 with 25 available

  Scenario: An admin adds an item
    When admin "ada" sets "TEST-NEW" to name "New item", unit price 3.50 and 7 available
    Then the answer is 200
    And anyone looking at the catalog sees "TEST-NEW" as "New item" at 3.50 with 7 available

  Scenario Outline: Only admins set stock
    When <who> tries to set "TEST-A" to 0 available
    Then the answer is <status>
    And anyone looking at the catalog sees "TEST-A" as "Test item A" at 18.50 with 10 available

    Examples:
      | who                           | status |
      | someone who is not logged in  | 401    |
      | customer "alice"              | 403    |
      | staff member "sam"            | 403    |
      | a login with the role "Admin" | 403    |

  Scenario: Invalid values are refused
    When admin "ada" sets "TEST-A" to name "", unit price -1.00 and -1 available
    Then the answer is 400 with an error for each of "name", "unitPrice" and "available"
    And anyone looking at the catalog sees "TEST-A" as "Test item A" at 18.50 with 10 available
