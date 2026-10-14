import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { Given, Then, When, createUser, endSessions, logIn, storedToken } from './fixtures';

// Spec 015: staff support customers' orders, and logins renew themselves.

When('a member of staff looks up that order', async ({ page, staffPage, request }) => {
  const order = page.getByRole('region', { name: 'Order' });
  await expect(order).toContainText('Status: Pending');
  const orderId = (await order.locator('code').first().textContent())!.trim();

  const staff = { username: `e2e-staff-${randomUUID().slice(0, 8)}`, password: randomUUID() };
  await createUser(request, staff, ['staff']);
  await staffPage.goto('/');
  await logIn(staffPage, staff);

  await staffPage.getByLabel('Order ID').fill(orderId);
  await staffPage.getByRole('button', { name: 'Look up' }).click();
  await expect(staffPage.getByRole('group', { name: 'Looked-up order' })).toContainText('Status: Pending');
});

When('the member of staff cancels it for the customer', async ({ staffPage }) => {
  const order = staffPage.getByRole('group', { name: 'Looked-up order' });
  await order.getByRole('button', { name: 'Cancel for the customer' }).click();
  await expect(order).toContainText('Status: Cancelled', { timeout: 15_000 });
});

Then('they see {string}', async ({ page }, text: string) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(text);
});

// The page's clock is under the test's control, so a token's lifetime passes in an instant (spec 015).
// Keycloak still runs on real time, so the old token stays valid there: the assertions are about
// what the page does, not about what the services refuse (spec 012 covers expired tokens).
Given('the page clock is under test control', async ({ page }) => {
  await page.clock.install();
});

Given('the page has noted its access token', async ({ page, scenario }) => {
  scenario.firstToken = (await storedToken(page))!;
  expect(scenario.firstToken).toBeTruthy();
});

When("most of the access token's lifetime passes", async ({ page }) => {
  // Tokens last 15 minutes (realm `accessTokenLifespan`); renewal starts a minute before expiry.
  // Jump to 14:10: renewal is due, expiry is not. One jump fires each due timer once.
  await page.clock.fastForward('14:10');
});

When("the rest of the first token's lifetime passes", async ({ page }) => {
  await page.clock.fastForward('01:00');
});

When('their session is ended at Keycloak', async ({ request, scenario }) => {
  await endSessions(request, scenario.customerId);
});

Then('the page has a new access token', async ({ page, scenario }) => {
  await expect.poll(() => storedToken(page), { timeout: 15_000 }).not.toBe(scenario.firstToken);
});

Then('the shop is called with the new token', async ({ page, scenario }) => {
  const placing = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/order-api/orders'));
  await page.getByLabel('SKU').fill('SKU-001');
  await page.getByLabel('Quantity').fill('1');
  await page.getByRole('button', { name: 'Place order' }).click();

  const sent = (await placing).headers()['authorization'];
  const stored = await storedToken(page);
  expect(stored).not.toBe(scenario.firstToken);
  expect(sent).toBe(`Bearer ${stored}`);
  await expect(page.getByRole('region', { name: 'Order' })).toContainText('Status:');
});
