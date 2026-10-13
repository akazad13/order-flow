import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import * as clients from './api/clients';

vi.mock('./api/clients');

describe('App', () => {
  beforeEach(() => {
    vi.mocked(clients.getInbox).mockResolvedValue([]);
  });

  it('shows the placed order as Pending', async () => {
    vi.mocked(clients.placeOrder).mockResolvedValue({
      ok: true,
      order: {
        orderId: '8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d',
        customerId: 'customer-1',
        status: 'Pending',
        lines: [{ sku: 'SKU-001', quantity: 1 }],
        placedAt: '2026-10-14T10:00:00Z',
      },
    });

    render(<App />);
    await userEvent.click(screen.getByRole('button', { name: 'Place order' }));

    const order = await screen.findByRole('region', { name: 'Order' });
    expect(within(order).getByText('Pending')).toBeInTheDocument();
    expect(clients.placeOrder).toHaveBeenCalledWith('customer-1', 'SKU-001', 1);
  });

  it('lists validation errors from the API', async () => {
    vi.mocked(clients.placeOrder).mockResolvedValue({
      ok: false,
      problem: { title: 'One or more validation errors occurred.', status: 400, errors: { 'lines[0].quantity': ['Quantity must be at least 1.'] } },
    });

    render(<App />);
    await userEvent.click(screen.getByRole('button', { name: 'Place order' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Quantity must be at least 1.');
    expect(screen.queryByRole('region', { name: 'Order' })).not.toBeInTheDocument();
  });
});
