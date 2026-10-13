import { expect } from '@playwright/test';
import { Given, Then, When } from './fixtures';

// The flow is asynchronous (broker in the middle): rely on Playwright's
// auto-retrying expect with a timeout, never on fixed sleeps.
const EVENTUAL_TIMEOUT_MS = 15_000;

Given('a new customer is on the order page', async ({ page, customer }) => {
  await page.goto('/');
  await page.getByLabel('Customer ID').fill(customer.id);
});

When('they order {int} of {string}', async ({ page }, quantity: number, sku: string) => {
  await page.getByLabel('SKU').fill(sku);
  await page.getByLabel('Quantity').fill(String(quantity));
  await page.getByRole('button', { name: 'Place order' }).click();
});

Then('they see their order with status {string}', async ({ page }, status: string) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(status);
});

Then('their inbox shows {string}', async ({ page }, text: string) => {
  await expect(page.getByRole('region', { name: 'Inbox' })).toContainText(text, { timeout: EVENTUAL_TIMEOUT_MS });
});
