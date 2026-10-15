import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as clients from './api/clients';
import type { Order } from './api/clients';
import { watchOrder } from './api/live';
import { StaffLookup } from './StaffLookup';

vi.mock('./api/clients');
vi.mock('./api/live');

const pending: Order = {
  orderId: '8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d',
  customerId: 'alice-id',
  status: 'Pending',
  lines: [{ sku: 'SKU-001', quantity: 1 }],
  total: null,
  cancellation: null,
  placedAt: '2026-10-15T10:00:00Z',
};
const cancelledBySupport: Order = {
  ...pending,
  status: 'Cancelled',
  cancellation: { reason: 'StaffRequested', detail: 'Cancelled by our support team at the customer\'s request.' },
};

async function lookUp(orderId: string) {
  render(<StaffLookup />);
  await userEvent.type(screen.getByLabelText('Order ID'), orderId);
  await userEvent.click(screen.getByRole('button', { name: 'Look up' }));
}

describe('StaffLookup (spec 015, AC-5)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(watchOrder).mockImplementation(() => vi.fn());
  });

  it("shows any customer's order by its ID", async () => {
    vi.mocked(clients.getOrder).mockResolvedValue(pending);
    await lookUp(pending.orderId);

    const order = await screen.findByRole('group', { name: 'Looked-up order' });
    expect(clients.getOrder).toHaveBeenCalledWith(pending.orderId);
    expect(within(order).getByText('alice-id')).toBeInTheDocument();
    expect(within(order).getByText('Pending')).toBeInTheDocument();
  });

  it('says so when there is no such order', async () => {
    vi.mocked(clients.getOrder).mockResolvedValue(null);
    await lookUp('no-such-order');

    expect(await screen.findByRole('status')).toHaveTextContent('No order with that ID.');
  });

  it("cancels for the customer, and shows the outcome when it is pushed", async () => {
    vi.mocked(clients.getOrder).mockResolvedValue(pending);
    vi.mocked(clients.cancelOrder).mockResolvedValue({ outcome: 'accepted' });
    await lookUp(pending.orderId);
    await waitFor(() => expect(watchOrder).toHaveBeenCalledWith(pending.orderId, expect.any(Function), expect.any(Function)));

    await userEvent.click(screen.getByRole('button', { name: 'Cancel for the customer' }));
    expect(clients.cancelOrder).toHaveBeenCalledWith(pending.orderId);
    expect(screen.getByText('Cancelling...')).toBeInTheDocument();

    const [, onUpdate] = vi.mocked(watchOrder).mock.calls[0];
    await act(async () => onUpdate(cancelledBySupport));
    const order = screen.getByRole('group', { name: 'Looked-up order' });
    expect(within(order).getByText('Cancelled')).toBeInTheDocument();
    expect(within(order).getByText("Reason: by our support team, at the customer's request")).toBeInTheDocument();
    expect(within(order).getByText("Cancelled by our support team at the customer's request.")).toBeInTheDocument();
  });

  it('says when it is too late', async () => {
    vi.mocked(clients.getOrder).mockResolvedValue(pending);
    vi.mocked(clients.cancelOrder).mockResolvedValue({ outcome: 'too-late' });
    await lookUp(pending.orderId);

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel for the customer' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Too late to cancel: the time to cancel this order has passed.');
  });
});
