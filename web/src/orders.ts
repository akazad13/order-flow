import type { Order } from './api/clients';

/**
 * Spec 022: an order can be cancelled while Pending, or Confirmed until its `cancellableUntil`;
 * a Cancelled order never.
 */
export function canCancel(order: Order, now = Date.now()): boolean {
  if (order.status === 'Pending') return true;
  return order.status === 'Confirmed' && !!order.cancellableUntil && Date.parse(order.cancellableUntil) > now;
}
