import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { Given, Then, createUser, logIn } from './fixtures';

// Spec 017: an admin manages stock on the stock page, next to the customer's browser.

Given('an admin puts a new item on sale priced {float} with {int} in stock', async ({ shopPage, request, scenario }, unitPrice: number, available: number) => {
  scenario.sku = `E2E-${randomUUID().slice(0, 8).toUpperCase()}`;
  const admin = { username: `e2e-admin-${randomUUID().slice(0, 8)}`, password: randomUUID() };
  await createUser(request, admin, ['admin']);
  await shopPage.goto('/');
  await logIn(shopPage, admin);

  const stock = shopPage.getByRole('region', { name: 'Stock' });
  await stock.getByLabel('Item SKU').fill(scenario.sku);
  await stock.getByLabel('Name').fill(`E2E item ${scenario.sku}`);
  await stock.getByLabel('Unit price').fill(unitPrice.toFixed(2));
  await stock.getByLabel('Available').fill(String(available));
  await stock.getByRole('button', { name: 'Save item' }).click();
  await expect(stock.getByRole('status')).toContainText(`Saved ${scenario.sku}: ${available} available`);
});

Then("the admin's stock page shows {int} of that item available", async ({ shopPage, scenario }, available: number) => {
  // Read again after the orders: the stock held for them is no longer available.
  await expect
    .poll(
      async () => {
        await shopPage.reload();
        const row = shopPage.getByRole('row').filter({ hasText: scenario.sku! });
        return (await row.getByRole('cell').nth(3).textContent())?.trim();
      },
      { timeout: 15_000 },
    )
    .toBe(String(available));
});
