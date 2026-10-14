import { useEffect, useState, type FormEvent } from 'react';
import { getInbox, getOrder, placeOrder, type InboxMessage, type Order } from './api/clients';
import { watchOrder } from './api/live';

// The inbox is still polled; order status is pushed (spec 005).
export const POLL_MS = 1000;

/** Status only moves forward: never replace a final status with Pending (spec 005, AC-5). */
function newer(current: Order | null, next: Order): Order {
  return current?.orderId === next.orderId && current.status !== 'Pending' && next.status === 'Pending' ? current : next;
}

// Same plain words as the customer's inbox (specs 002, 003).
const CANCELLATION_TEXT: Record<NonNullable<Order['cancellation']>['reason'], string> = {
  OutOfStock: 'some items are out of stock',
  PaymentDeclined: 'your payment was declined',
};

export function App() {
  const [customerId, setCustomerId] = useState('customer-1');
  const [sku, setSku] = useState('SKU-001');
  const [quantity, setQuantity] = useState(1);
  const [order, setOrder] = useState<Order | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [inbox, setInbox] = useState<InboxMessage[]>([]);

  const orderId = order?.orderId;
  const orderCustomerId = order?.customerId;
  const isPending = order?.status === 'Pending';

  // Watch the order while it is Pending (spec 005): updates are pushed, and the order is read
  // once after each (re)join, for anything that changed before (AC-3). No timer (AC-4).
  useEffect(() => {
    if (!orderId || !isPending) return;
    let active = true;
    const unwatch = watchOrder(
      orderId,
      (pushed) => {
        if (active) setOrder((current) => newer(current, pushed));
      },
      async () => {
        const latest = await getOrder(orderId);
        if (active && latest) setOrder((current) => newer(current, latest));
      },
    );
    return () => {
      active = false;
      unwatch();
    };
  }, [orderId, isPending]);

  useEffect(() => {
    if (!orderCustomerId) return;
    let cancelled = false;
    const refresh = async () => {
      const messages = await getInbox(orderCustomerId);
      if (!cancelled) setInbox(messages);
    };
    void refresh();
    const timer = setInterval(refresh, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [orderId, orderCustomerId]);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setErrors([]);
    const result = await placeOrder(customerId, sku, quantity);
    if (result.ok) {
      setOrder(result.order);
    } else {
      setOrder(null);
      setErrors(Object.values(result.problem?.errors ?? {}).flat());
    }
  }

  return (
    <main>
      <h1>OrderFlow</h1>

      <form onSubmit={handleSubmit}>
        <label>
          Customer ID
          <input value={customerId} onChange={(e) => setCustomerId(e.target.value)} />
        </label>
        <label>
          SKU
          <input value={sku} onChange={(e) => setSku(e.target.value)} />
        </label>
        <label>
          Quantity
          <input type="number" value={quantity} onChange={(e) => setQuantity(Number(e.target.value))} />
        </label>
        <button type="submit">Place order</button>
      </form>

      {errors.length > 0 && (
        <ul role="alert">
          {errors.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}

      {order && (
        <section aria-label="Order">
          <h2>Your order</h2>
          <p>
            Order <code>{order.orderId}</code>
          </p>
          <p>
            Status: <strong>{order.status}</strong>
          </p>
          {order.total != null && <p>Total: {order.total.toFixed(2)}</p>}
          {order.cancellation && (
            <>
              <p>Reason: {CANCELLATION_TEXT[order.cancellation.reason]}</p>
              <p>{order.cancellation.detail}</p>
            </>
          )}
        </section>
      )}

      {order && (
        <section aria-label="Inbox">
          <h2>Inbox</h2>
          {inbox.length === 0 ? (
            <p>No messages yet.</p>
          ) : (
            <ol>
              {inbox.map((message) => (
                <li key={message.messageId}>{message.text}</li>
              ))}
            </ol>
          )}
        </section>
      )}
    </main>
  );
}
