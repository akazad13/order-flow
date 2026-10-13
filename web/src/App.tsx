import { useEffect, useState, type FormEvent } from 'react';
import { getInbox, getOrder, placeOrder, type InboxMessage, type Order } from './api/clients';

// Spec 001 Q3: poll until SignalR arrives in spec 005.
export const POLL_MS = 1000;

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

  // Re-read the order while it is Pending (spec 001, AC-5).
  useEffect(() => {
    if (!orderId || !isPending) return;
    let cancelled = false;
    const timer = setInterval(async () => {
      const latest = await getOrder(orderId);
      if (!cancelled && latest) setOrder(latest);
    }, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
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
