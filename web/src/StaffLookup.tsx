import { useEffect, useState, type FormEvent } from 'react';
import { cancelOrder, getOrder, type Order } from './api/clients';
import { canCancel } from './orders';
import { watchOrder } from './api/live';

// Staff read the reason about the customer, not to them (spec 015). A Record over the generated
// union: a reason added to the contract fails the build until it has words here.
const REASON_TEXT: Record<NonNullable<Order['cancellation']>['reason'], string> = {
  OutOfStock: 'some items were out of stock',
  PaymentDeclined: 'the payment was declined',
  CustomerRequested: 'the customer cancelled it',
  StaffRequested: "by our support team, at the customer's request", // spec 016
};

type Lookup = { state: 'idle' } | { state: 'not-found' } | { state: 'found'; order: Order };
type CancelState = 'idle' | 'cancelling' | 'too-late';

/**
 * Spec 015: staff look up any order by its ID and cancel it on the customer's behalf. Like the
 * customer's page, a Pending order is watched, so the outcome of a cancellation is pushed.
 */
export function StaffLookup() {
  const [orderId, setOrderId] = useState('');
  const [lookup, setLookup] = useState<Lookup>({ state: 'idle' });
  const [cancelState, setCancelState] = useState<CancelState>('idle');

  const found = lookup.state === 'found' ? lookup.order : null;
  const cancellable = found ? canCancel(found) : false; // spec 022
  const watchedId = cancellable ? found!.orderId : undefined;

  useEffect(() => {
    if (!watchedId) return;
    let active = true;
    const show = (order: Order) => {
      if (active && order.orderId === watchedId) setLookup({ state: 'found', order });
    };
    const unwatch = watchOrder(watchedId, show, async () => {
      const latest = await getOrder(watchedId);
      if (latest) show(latest);
    });
    return () => {
      active = false;
      unwatch();
    };
  }, [watchedId]);

  async function handleLookup(event: FormEvent) {
    event.preventDefault();
    setCancelState('idle');
    const order = await getOrder(orderId.trim());
    setLookup(order ? { state: 'found', order } : { state: 'not-found' });
  }

  async function handleCancel() {
    if (!found) return;
    setCancelState('cancelling');
    const result = await cancelOrder(found.orderId);
    if (result.outcome === 'too-late') setCancelState('too-late');
    else if (result.outcome === 'already-cancelled') setLookup({ state: 'found', order: result.order });
  }

  return (
    <section aria-label="Staff">
      <h2>Look up an order</h2>
      <form onSubmit={handleLookup}>
        <label>
          Order ID
          <input value={orderId} onChange={(e) => setOrderId(e.target.value)} />
        </label>
        <button type="submit">Look up</button>
      </form>

      {lookup.state === 'not-found' && <p role="status">No order with that ID.</p>}

      {found && (
        <div aria-label="Looked-up order" role="group">
          <p>
            Order <code>{found.orderId}</code> for customer <code>{found.customerId}</code>
          </p>
          <p>
            Status: <strong>{found.status}</strong>
          </p>
          {found.cancellation && (
            <>
              <p>Reason: {REASON_TEXT[found.cancellation.reason]}</p>
              <p>{found.cancellation.detail}</p>
            </>
          )}
          {cancellable && cancelState === 'idle' && (
            <button type="button" onClick={handleCancel}>
              Cancel for the customer
            </button>
          )}
          {cancelState === 'cancelling' && found.status !== 'Cancelled' && <p>Cancelling...</p>}
          {cancelState === 'too-late' && <p role="status">Too late to cancel: the time to cancel this order has passed.</p>}
        </div>
      )}
    </section>
  );
}
