import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { Then, When, createUser, endSessions, logIn } from './fixtures';

// Spec 015: staff support customers' orders, and logins renew themselves.

When('a member of staff looks up that order', async ({ page, shopPage, request }) => {
  const order = page.getByRole('region', { name: 'Order' });
  await expect(order).toContainText('Status: Pending');
  const orderId = (await order.locator('code').first().textContent())!.trim();

  const staff = { username: `e2e-staff-${randomUUID().slice(0, 8)}`, password: randomUUID() };
  await createUser(request, staff, ['staff']);
  await shopPage.goto('/');
  await logIn(shopPage, staff);

  await shopPage.getByLabel('Order ID').fill(orderId);
  await shopPage.getByRole('button', { name: 'Look up' }).click();
  await expect(shopPage.getByRole('group', { name: 'Looked-up order' })).toContainText('Status: Pending');
});

When('the member of staff cancels it for the customer', async ({ shopPage }) => {
  const order = shopPage.getByRole('group', { name: 'Looked-up order' });
  await order.getByRole('button', { name: 'Cancel for the customer' }).click();
  await expect(order).toContainText('Status: Cancelled', { timeout: 15_000 });
});

Then('they see {string}', async ({ page }, text: string) => {
  await expect(page.getByRole('region', { name: 'Order' })).toContainText(text);
});

// Spec 023: the gateway keeps the tokens; the page has none.
Then("no token is in the page's storage or readable cookies", async ({ page }) => {
  const visible = await page.evaluate(() => ({
    storage: [...Object.values({ ...sessionStorage }), ...Object.values({ ...localStorage })].join(' '),
    cookies: document.cookie,
  }));
  // A JWT starts with "eyJ" (base64 of '{"'); a session key or anything else does not.
  expect(visible.storage).not.toContain('eyJ');
  expect(visible.cookies).not.toContain('eyJ');
  expect(visible.cookies).not.toContain('orderflow.session');
});

Then('the session cookie is HttpOnly and SameSite=Lax', async ({ page }) => {
  const session = (await page.context().cookies()).find((c) => c.name === 'orderflow.session');
  expect(session, 'the session cookie').toBeTruthy();
  expect(session!.httpOnly).toBe(true);
  expect(session!.sameSite).toBe('Lax');
});

When('their session is ended at Keycloak', async ({ request, scenario }) => {
  await endSessions(request, scenario.customerId);
});
