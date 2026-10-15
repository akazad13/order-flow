# Spec: specs/002-stock-rejection/spec.md (AC-4), specs/003-payment-failure/spec.md (AC-4), specs/006-customer-cancel/spec.md (AC-7, Q3), specs/016-event-versions/spec.md (AC-1, AC-3), specs/020-itemised-shortage-message/spec.md (AC-1, AC-2)
# Runs in: tests/OrderFlow.Notification.ComponentTests (Notification service + in-memory bus)
@component @spec-002 @spec-003 @spec-006 @spec-016 @spec-020
Feature: Order cancelled notification (Notification service)
  The customer is told their order was cancelled, and why, in plain words.

  Scenario Outline: The customer is told why the order was cancelled
    When an OrderCancelled event with reason "<reason>" arrives for customer "customer-3"
    Then the inbox of "customer-3" contains "<message>"

    Examples:
      | reason          | message                                               |
      | PaymentDeclined | Your order was cancelled: your payment was declined   |
      | CustomerRequested | Your order was cancelled at your request            |
      | StaffRequested    | Your order was cancelled by our support team at your request |
      | SomeFutureReason  | Your order was cancelled                            |

  Scenario: A shortage names every short item, as the order page does
    When an OrderCancelled event with reason "OutOfStock" and detail "Only 1 of SKU-002 available, 3 requested. Only 0 of SKU-009 available, 1 requested." arrives for customer "customer-5"
    Then the inbox of "customer-5" contains "Your order was cancelled: some items are out of stock. Only 1 of SKU-002 available, 3 requested. Only 0 of SKU-009 available, 1 requested."
