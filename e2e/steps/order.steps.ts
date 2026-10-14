import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { Given, Then, When, createCustomer, inventoryUrl, logIn, paymentUrl } from './fixtures';

// The flow is asynchronous (broker in the middle): rely on Playwright's
// auto-retrying expect with a timeout, never on fixed sleeps.
const EVENTUAL_TIMEOUT_MS = 15_000;

Given('an item priced {float} with {int} in stock', async ({ request, scenario }, unitPrice: number, available: number) => {
  scenario.sku = `E2E-${randomUUID().slice(0, 8).toUpperCase()}`;
  const response = await request.put(`${inventoryUrl}/stock/${scenario.sku}`, {
    data: { name: `E2E item ${scenario.sku}`, unitPrice, available },
  });
  expect(response.ok()).toBeTruthy();
});

Given('a new customer is on the order page', async ({ page, request, scenario }) => {
  await createCustomer(request, scenario);
  page.on('request', (request) => {
    if (request.method() === 'GET' && /\/order-api\/orders\/[0-9a-f-]{36}$/.test(request.url())) {
      scenario.orderReads.push(request.url());
    }
  });
  page.on('websocket', (socket) => {
    if (socket.url().includes('/order-api/hubs/orders')) {
      socket.on('framereceived', ({ payload }) => scenario.hubFrames.push(payload.toString()));
    }
  });
  await page.goto('/');
  await logIn(page, scenario);
});

Given('a visitor is on the order page', async ({ page }) => {
  await page.goto('/');
});

Then('they are asked to log in and see no order form', async ({ page }) => {
  await expect(page.getByRole('button', { name: 'Log in' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Place order' })).toHaveCount(0);
});

When('they log in as a new customer', async ({ page, request, scenario }) => {
  await createCustomer(request, scenario);
  await logIn(page, scenario);
});

Then('they see they are logged in', async ({ page, scenario }) => {
  await expect(page.getByText(`Logged in as ${scenario.username}`)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Place order' })).toBeVisible();
  await expect(page.getByLabel('Customer ID')).toHaveCount(0);
});

When('they log out', async ({ page }) => {
  await page.getByRole('button', { name: 'Log out' }).click();
});

Given('their payments will be declined', async ({ request, scenario }) => {
  const response = await request.put(`${paymentUrl}/test/declines/${scenario.customerId}`, {
    data: { reason: 'Card declined' },
  });
  expect(response.ok()).toBeTruthy();
});

Given('their payments are left undecided', async ({ request, scenario }) => {
  // Spec 006, Q4: keeps the order Pending until the customer cancels it.
  const response = await request.put(`${paymentUrl}/test/stalls/${scenario.customerId}`);
  expect(response.ok()).toBeTruthy();
});

When('they cancel the order', async ({ page }) => {
  const order = page.getByRole('region', { name: 'Order' });
  await expect(order).toContainText('Status: Pending');
  // Wait until the page watches the order, so the outcome can only arrive as a push (spec 005).
  await expect(order).toHaveAttribute('data-watching', 'true');
  await order.getByRole('button', { name: 'Cancel order' }).click();
});

When('they order {int} of {string}', async ({ page }, quantity: number, sku: string) => {
  await placeOrder(page, sku, quantity);
});

When('they order {int} of that item', async ({ page, scenario }, quantity: number) => {
  await placeOrder(page, scenario.sku!, quantity);
});

Then('they see their order', async ({ page }) => {
  await expect(page.getByRole('region', { name: 'Order' })).toBeVisible();
});

Then('they see their order with status {string}', async ({ page }, status: string) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(status);
});

Then('they see their order with status {string} within {int} seconds', async ({ page }, status: string, seconds: number) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(`Status: ${status}`, { timeout: seconds * 1000 });
});

Then('the status was pushed to the page', async ({ scenario }) => {
  // SignalR's JSON protocol: an invocation frame naming the client method (spec 005, AC-1).
  // Only meaningful once the page was watching before the change (see "they cancel the order"):
  // a change made before the page joined arrives through the one read instead (AC-3).
  expect(scenario.hubFrames.some((frame) => frame.includes('"target":"OrderUpdated"'))).toBe(true);
});

Then('the page read the order at most once after placing it', async ({ scenario }) => {
  // One read after joining the hub (spec 005, AC-3); the status itself was pushed.
  expect(scenario.orderReads.length).toBeLessThanOrEqual(1);
});

Then('they see a total of {float}', async ({ page }, total: number) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(`Total: ${total.toFixed(2)}`);
});

Then('they see the reason {string}', async ({ page }, reason: string) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(`Reason: ${reason}`);
});

Then('the item has {int} in stock again', async ({ request, scenario }, available: number) => {
  await expect
    .poll(async () => (await (await request.get(`${inventoryUrl}/stock/${scenario.sku}`)).json()).available, {
      timeout: EVENTUAL_TIMEOUT_MS,
    })
    .toBe(available);
});

Then('their inbox shows {string}', async ({ page }, text: string) => {
  await expect(page.getByRole('region', { name: 'Inbox' })).toContainText(text, { timeout: EVENTUAL_TIMEOUT_MS });
});

Then('their inbox shows, in order:', async ({ page }, table) => {
  const expected = table.hashes().map((row: Record<string, string>) => row.message);
  await expect(page.getByRole('region', { name: 'Inbox' }).getByRole('listitem')).toHaveText(expected, {
    timeout: EVENTUAL_TIMEOUT_MS,
  });
});

async function placeOrder(page: import('@playwright/test').Page, sku: string, quantity: number) {
  await page.getByLabel('SKU').fill(sku);
  await page.getByLabel('Quantity').fill(String(quantity));
  await page.getByRole('button', { name: 'Place order' }).click();
}
