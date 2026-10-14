import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as clients from './api/clients';
import { StockAdmin } from './StockAdmin';

vi.mock('./api/clients');

const coffee = { sku: 'SKU-001', name: 'Coffee beans', unitPrice: 18.5, available: 10 };

describe('StockAdmin (spec 017, AC-4)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(clients.listStock).mockResolvedValue([coffee]);
  });

  it('lists the catalog', async () => {
    render(<StockAdmin />);

    const row = (await screen.findByText('SKU-001')).closest('tr')!;
    expect(within(row).getByText('Coffee beans')).toBeInTheDocument();
    expect(within(row).getByText('18.50')).toBeInTheDocument();
    expect(within(row).getByText('10')).toBeInTheDocument();
  });

  it('changes an item, then shows the catalog as stored', async () => {
    vi.mocked(clients.setStock).mockResolvedValue({ ok: true, item: { ...coffee, available: 25 } });
    render(<StockAdmin />);

    await userEvent.click(await screen.findByRole('button', { name: 'Edit SKU-001' }));
    const available = screen.getByLabelText('Available');
    await userEvent.clear(available);
    await userEvent.type(available, '25');
    vi.mocked(clients.listStock).mockResolvedValue([{ ...coffee, available: 25 }]);
    await userEvent.click(screen.getByRole('button', { name: 'Save item' }));

    expect(clients.setStock).toHaveBeenCalledWith('SKU-001', { name: 'Coffee beans', unitPrice: 18.5, available: 25 });
    expect(await screen.findByRole('status')).toHaveTextContent('Saved SKU-001: 25 available at 18.50.');
    await waitFor(() => expect(within(screen.getByText('SKU-001').closest('tr')!).getByText('25')).toBeInTheDocument());
  });

  it('adds a new item', async () => {
    vi.mocked(clients.setStock).mockResolvedValue({ ok: true, item: { sku: 'SKU-NEW', name: 'Tea', unitPrice: 3.5, available: 7 } });
    render(<StockAdmin />);

    await userEvent.type(screen.getByLabelText('Item SKU'), 'SKU-NEW');
    await userEvent.type(screen.getByLabelText('Name'), 'Tea');
    await userEvent.clear(screen.getByLabelText('Unit price'));
    await userEvent.type(screen.getByLabelText('Unit price'), '3.50');
    await userEvent.clear(screen.getByLabelText('Available'));
    await userEvent.type(screen.getByLabelText('Available'), '7');
    await userEvent.click(screen.getByRole('button', { name: 'Save item' }));

    expect(clients.setStock).toHaveBeenCalledWith('SKU-NEW', { name: 'Tea', unitPrice: 3.5, available: 7 });
  });

  it('shows the errors the service gives (AC-6)', async () => {
    vi.mocked(clients.setStock).mockResolvedValue({ ok: false, errors: ['Name is required.', 'Available cannot be negative.'] });
    render(<StockAdmin />);

    await userEvent.type(screen.getByLabelText('Item SKU'), 'SKU-001');
    await userEvent.click(screen.getByRole('button', { name: 'Save item' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Name is required.');
    expect(alert).toHaveTextContent('Available cannot be negative.');
  });
});
