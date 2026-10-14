import { useEffect, useState, type FormEvent } from 'react';
import { listStock, setStock, type StockItem } from './api/clients';

const EMPTY = { sku: '', name: '', unitPrice: '0.00', available: '0' };

/**
 * Spec 017: admins see the catalog, change an item (name, price, quantity available) or add one.
 * "Available" is set outright: it is what can still be held for new orders (R-1).
 */
export function StockAdmin() {
  const [items, setItems] = useState<StockItem[]>([]);
  const [form, setForm] = useState(EMPTY);
  const [errors, setErrors] = useState<string[]>([]);
  const [saved, setSaved] = useState<string | null>(null);

  async function refresh() {
    setItems(await listStock());
  }

  useEffect(() => {
    void refresh();
  }, []);

  function edit(item: StockItem) {
    setForm({ sku: item.sku, name: item.name, unitPrice: item.unitPrice.toFixed(2), available: String(item.available) });
    setErrors([]);
    setSaved(null);
  }

  async function handleSave(event: FormEvent) {
    event.preventDefault();
    setSaved(null);
    const sku = form.sku.trim();
    if (!sku) {
      setErrors(['SKU is required.']);
      return;
    }

    const result = await setStock(sku, { name: form.name, unitPrice: Number(form.unitPrice), available: Number(form.available) });
    if (result.ok) {
      setErrors([]);
      setSaved(`Saved ${result.item.sku}: ${result.item.available} available at ${result.item.unitPrice.toFixed(2)}.`);
      setForm(EMPTY);
      await refresh();
    } else {
      setErrors(result.errors);
    }
  }

  const field = (key: keyof typeof EMPTY) => ({
    value: form[key],
    onChange: (e: { target: { value: string } }) => setForm((current) => ({ ...current, [key]: e.target.value })),
  });

  return (
    <section aria-label="Stock">
      <h2>Stock</h2>
      <table>
        <thead>
          <tr>
            <th>SKU</th>
            <th>Name</th>
            <th>Price</th>
            <th>Available</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.sku}>
              <td>{item.sku}</td>
              <td>{item.name}</td>
              <td>{item.unitPrice.toFixed(2)}</td>
              <td>{item.available}</td>
              <td>
                <button type="button" onClick={() => edit(item)} aria-label={`Edit ${item.sku}`}>
                  Edit
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <form onSubmit={handleSave} aria-label="Set an item">
        <label>
          Item SKU
          <input {...field('sku')} />
        </label>
        <label>
          Name
          <input {...field('name')} />
        </label>
        <label>
          Unit price
          <input type="number" step="0.01" {...field('unitPrice')} />
        </label>
        <label>
          Available
          <input type="number" {...field('available')} />
        </label>
        <button type="submit">Save item</button>
      </form>

      {errors.length > 0 && (
        <ul role="alert">
          {errors.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
      {saved && <p role="status">{saved}</p>}
    </section>
  );
}
