import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { Given, Then, When, inventoryUrl } from './fixtures';

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

Given('a new customer is on the order page', async ({ page, scenario }) => {
  await page.goto('/');
  await page.getByLabel('Customer ID').fill(scenario.customerId);
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

Then('they see a total of {float}', async ({ page }, total: number) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(`Total: ${total.toFixed(2)}`);
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
