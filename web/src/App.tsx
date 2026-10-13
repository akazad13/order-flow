import { useEffect, useState, type FormEvent } from 'react';
import { getInbox, placeOrder, type InboxMessage, type Order } from './api/clients';

const INBOX_POLL_MS = 1000;

export function App() {
  const [customerId, setCustomerId] = useState('customer-1');
  const [sku, setSku] = useState('SKU-001');
  const [quantity, setQuantity] = useState(1);
  const [order, setOrder] = useState<Order | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [inbox, setInbox] = useState<InboxMessage[]>([]);

  // Live status arrives in spec 005 (SignalR); until then, poll the fake inbox.
  useEffect(() => {
    if (!order) return;
    let cancelled = false;
    const refresh = async () => {
      const messages = await getInbox(order.customerId);
      if (!cancelled) setInbox(messages);
    };
    void refresh();
    const timer = setInterval(refresh, INBOX_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [order]);

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
        </section>
      )}

      {order && (
        <section aria-label="Inbox">
          <h2>Inbox</h2>
          {inbox.length === 0 ? (
            <p>No messages yet.</p>
          ) : (
            <ul>
              {inbox.map((message) => (
                <li key={message.messageId}>{message.text}</li>
              ))}
            </ul>
          )}
        </section>
      )}
    </main>
  );
}
