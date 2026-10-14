import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import * as clients from './api/clients';
import type { Order } from './api/clients';
import { watchOrder } from './api/live';
import { currentCustomer, logIn, logOut, onLoginChanged } from './auth';

vi.mock('./api/clients');
vi.mock('./api/live');
vi.mock('./auth');

const pending: Order = {
  orderId: '8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d',
  customerId: 'customer-1',
  status: 'Pending',
  lines: [{ sku: 'SKU-001', quantity: 2 }],
  total: null,
  cancellation: null,
  placedAt: '2026-10-14T10:00:00Z',
};
const confirmed: Order = { ...pending, status: 'Confirmed', total: 37 };

/** The live connection the App opened (spec 005), driven by the test. */
type Live = { orderId: string; push: (order: Order) => Promise<void>; joined: () => Promise<void>; unwatch: () => void };

async function placeAndWatch(): Promise<Live> {
  vi.mocked(clients.placeOrder).mockResolvedValue({ ok: true, order: pending });
  render(<App />);
  await userEvent.click(await screen.findByRole('button', { name: 'Place order' }));
  await waitFor(() => expect(watchOrder).toHaveBeenCalled());
  const [orderId, onUpdate, onWatching] = vi.mocked(watchOrder).mock.calls[0];
  return {
    orderId,
    push: (order) => act(async () => onUpdate(order)),
    joined: () => act(async () => onWatching()),
    unwatch: vi.mocked(watchOrder).mock.results[0].value as () => void,
  };
}

describe('App', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(clients.getInbox).mockResolvedValue([]);
    vi.mocked(clients.getOrder).mockResolvedValue(pending);
    vi.mocked(watchOrder).mockImplementation(() => vi.fn());
    vi.mocked(currentCustomer).mockResolvedValue({ id: 'customer-1', name: 'alice', isStaff: false, isAdmin: false });
    vi.mocked(onLoginChanged).mockImplementation(() => () => undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the placed order as Pending', async () => {
    vi.mocked(clients.placeOrder).mockResolvedValue({
      ok: true,
      order: {
        orderId: '8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d',
        customerId: 'customer-1',
        status: 'Pending',
        lines: [{ sku: 'SKU-001', quantity: 1 }],
        total: null,
        cancellation: null,
        placedAt: '2026-10-14T10:00:00Z',
      },
    });

    render(<App />);
    await userEvent.click(await screen.findByRole('button', { name: 'Place order' }));

    const order = await screen.findByRole('region', { name: 'Order' });
    expect(within(order).getByText('Pending')).toBeInTheDocument();
    expect(clients.placeOrder).toHaveBeenCalledWith('SKU-001', 1);
  });

  it('watches the placed order and shows the pushed confirmation, then stops watching (spec 005, AC-1)', async () => {
    const live = await placeAndWatch();

    await live.push(confirmed);

    const order = screen.getByRole('region', { name: 'Order' });
    expect(within(order).getByText('Confirmed')).toBeInTheDocument();
    expect(within(order).getByText('Total: 37.00')).toBeInTheDocument();
    expect(live.orderId).toBe(pending.orderId);
    await waitFor(() => expect(live.unwatch).toHaveBeenCalled());
  });

  it('shows why a cancelled order was cancelled', async () => {
    const live = await placeAndWatch();

    await live.push({ ...pending, status: 'Cancelled', cancellation: { reason: 'OutOfStock', detail: 'Only 1 of SKU-002 available, 2 requested.' } });

    const order = screen.getByRole('region', { name: 'Order' });
    expect(within(order).getByText('Cancelled')).toBeInTheDocument();
    expect(within(order).getByText('Reason: some items are out of stock')).toBeInTheDocument();
    expect(within(order).getByText('Only 1 of SKU-002 available, 2 requested.')).toBeInTheDocument();
  });

  it('reads the order once each time it starts watching, so nothing is missed (spec 005, AC-3)', async () => {
    const live = await placeAndWatch();

    expect(screen.getByRole('region', { name: 'Order' })).toHaveAttribute('data-watching', 'false');
    await live.joined();
    await waitFor(() => expect(clients.getOrder).toHaveBeenCalledTimes(1));
    expect(screen.getByText('Pending')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Order' })).toHaveAttribute('data-watching', 'true');

    // A reconnect: the order was confirmed while the connection was down.
    vi.mocked(clients.getOrder).mockResolvedValue(confirmed);
    await live.joined();

    expect(await screen.findByText('Confirmed')).toBeInTheDocument();
    expect(clients.getOrder).toHaveBeenCalledTimes(2);
    expect(clients.getOrder).toHaveBeenCalledWith(pending.orderId);
  });

  it('does not re-read the order on a timer while watching (spec 005, AC-4)', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await placeAndWatch();

    await act(() => vi.advanceTimersByTimeAsync(30_000));

    expect(clients.getOrder).not.toHaveBeenCalled();
  });

  it('keeps a final status when a slower read answers Pending after the push (spec 005, AC-5)', async () => {
    let answerRead: (order: Order) => void = () => {};
    vi.mocked(clients.getOrder).mockImplementation(() => new Promise((resolve) => (answerRead = resolve)));
    const live = await placeAndWatch();

    const reading = live.joined(); // the read is still in flight...
    await live.push(confirmed); // ...when the confirmation is pushed
    answerRead(pending);
    await reading;

    expect(screen.getByText('Confirmed')).toBeInTheDocument();
    expect(screen.queryByText('Pending')).not.toBeInTheDocument();
  });

  it('cancels a pending order and shows the pushed cancellation (spec 006, AC-8)', async () => {
    vi.mocked(clients.cancelOrder).mockResolvedValue({ outcome: 'accepted' });
    const live = await placeAndWatch();

    await userEvent.click(screen.getByRole('button', { name: 'Cancel order' }));

    expect(clients.cancelOrder).toHaveBeenCalledWith(pending.orderId);
    expect(await screen.findByText('Cancelling...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument();

    await live.push({ ...pending, status: 'Cancelled', cancellation: { reason: 'CustomerRequested', detail: "Cancelled at the customer's request." } });

    const order = screen.getByRole('region', { name: 'Order' });
    expect(within(order).getByText('Cancelled')).toBeInTheDocument();
    expect(within(order).getByText('Reason: you cancelled it')).toBeInTheDocument();
    expect(screen.queryByText('Cancelling...')).not.toBeInTheDocument();
  });

  it('tells the customer when it is too late to cancel (spec 006, AC-8)', async () => {
    vi.mocked(clients.cancelOrder).mockResolvedValue({ outcome: 'too-late' });
    await placeAndWatch();

    await userEvent.click(screen.getByRole('button', { name: 'Cancel order' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Too late to cancel: your order is already confirmed.');
  });

  it('explains when an accepted cancellation lost the race to the payment (spec 006, AC-6, AC-8)', async () => {
    vi.mocked(clients.cancelOrder).mockResolvedValue({ outcome: 'accepted' });
    const live = await placeAndWatch();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel order' }));

    await live.push(confirmed);

    expect(screen.getByText('Confirmed')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Too late to cancel: your order is already confirmed.');
  });

  it('offers no cancel once the order is final', async () => {
    const live = await placeAndWatch();
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeInTheDocument();

    await live.push(confirmed);

    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument();
  });

  it('asks a visitor to log in, and shows no order form (spec 012, AC-6)', async () => {
    vi.mocked(currentCustomer).mockResolvedValue(null);
    render(<App />);

    await userEvent.click(await screen.findByRole('button', { name: 'Log in' }));

    expect(logIn).toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Place order' })).not.toBeInTheDocument();
  });

  it('shows who is logged in, with no customer ID field (spec 012, AC-2, AC-6)', async () => {
    render(<App />);

    expect(await screen.findByText('Logged in as alice')).toBeInTheDocument();
    expect(screen.queryByLabelText('Customer ID')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Place order' })).toBeInTheDocument();
  });

  it('shows the order lookup to staff only (spec 015, AC-5)', async () => {
    const { unmount } = render(<App />);
    expect(await screen.findByText('Logged in as alice')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Staff' })).not.toBeInTheDocument();
    unmount();

    vi.mocked(currentCustomer).mockResolvedValue({ id: 'sam-id', name: 'sam', isStaff: true, isAdmin: false });
    render(<App />);
    expect(await screen.findByRole('region', { name: 'Staff' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Place order' })).toBeInTheDocument(); // staff are customers too (R-2)
  });

  it('shows the stock page to admins only (spec 017, AC-4)', async () => {
    vi.mocked(clients.listStock).mockResolvedValue([]);
    const { unmount } = render(<App />);
    expect(await screen.findByText('Logged in as alice')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Stock' })).not.toBeInTheDocument();
    unmount();

    vi.mocked(currentCustomer).mockResolvedValue({ id: 'sam-id', name: 'sam', isStaff: true, isAdmin: false });
    const staff = render(<App />);
    expect(await screen.findByText('Logged in as sam')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Stock' })).not.toBeInTheDocument();
    staff.unmount();

    vi.mocked(currentCustomer).mockResolvedValue({ id: 'ada-id', name: 'ada', isStaff: false, isAdmin: true });
    render(<App />);
    expect(await screen.findByRole('region', { name: 'Stock' })).toBeInTheDocument();
  });

  it('asks to log in again when the login ends on its own (spec 015, AC-7)', async () => {
    render(<App />);
    expect(await screen.findByText('Logged in as alice')).toBeInTheDocument();

    const [listener] = vi.mocked(onLoginChanged).mock.calls[0];
    act(() => listener(null));

    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Place order' })).not.toBeInTheDocument();
  });

  it('reads the logged-in customer inbox', async () => {
    await placeAndWatch();

    await waitFor(() => expect(clients.getInbox).toHaveBeenCalledWith('customer-1'));
  });

  it('logs out (spec 012, AC-6)', async () => {
    render(<App />);

    await userEvent.click(await screen.findByRole('button', { name: 'Log out' }));

    expect(logOut).toHaveBeenCalled();
  });

  it('lists validation errors from the API', async () => {
    vi.mocked(clients.placeOrder).mockResolvedValue({
      ok: false,
      problem: { title: 'One or more validation errors occurred.', status: 400, errors: { 'lines[0].quantity': ['Quantity must be at least 1.'] } },
    });

    render(<App />);
    await userEvent.click(await screen.findByRole('button', { name: 'Place order' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Quantity must be at least 1.');
    expect(screen.queryByRole('region', { name: 'Order' })).not.toBeInTheDocument();
  });
});
