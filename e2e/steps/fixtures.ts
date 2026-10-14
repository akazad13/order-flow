import { randomUUID } from 'node:crypto';
import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { test as base, createBdd } from 'playwright-bdd';

// Per-scenario data. Fresh IDs keep parallel runs from seeing each other's
// inbox or draining each other's stock (constitution V).
type Scenario = {
  /** The Keycloak user ID: the customer, as every service sees it (spec 012, R-1). Set when the user is created. */
  customerId: string;
  username: string;
  password: string;
  sku?: string;
  /** GET /order-api/orders/{id} requests the page made (spec 005: pushed, not polled). */
  orderReads: string[];
  /** Text frames the page received on the order hub's WebSocket. */
  hubFrames: string[];
};

export const test = base.extend<{ scenario: Scenario }>({
  scenario: async ({}, use) => {
    const username = `e2e-${randomUUID().slice(0, 13)}`;
    await use({ customerId: '', username, password: randomUUID(), orderReads: [], hubFrames: [] });
  },
});

export const { Given, When, Then } = createBdd(test);

export const inventoryUrl = process.env.INVENTORY_URL ?? 'http://localhost:5102';
export const paymentUrl = process.env.PAYMENT_URL ?? 'http://localhost:5103';
export const keycloakUrl = process.env.KEYCLOAK_URL ?? 'http://localhost:8180';

// Keycloak's dev admin from docker-compose.yml (local and CI only, ADR 0008).
const keycloakAdmin = { username: process.env.KEYCLOAK_ADMIN ?? 'admin', password: process.env.KEYCLOAK_ADMIN_PASSWORD ?? 'admin' };

/**
 * Creates the scenario's own customer in the `orderflow` realm (spec 012, Q5: fresh data per test)
 * and records the user ID, which is the customer ID every service sees.
 */
export async function createCustomer(request: APIRequestContext, scenario: Scenario): Promise<void> {
  const login = await request.post(`${keycloakUrl}/realms/master/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'admin-cli', ...keycloakAdmin },
  });
  expect(login.ok()).toBeTruthy();
  const { access_token: adminToken } = await login.json();

  const created = await request.post(`${keycloakUrl}/admin/realms/orderflow/users`, {
    headers: { Authorization: `Bearer ${adminToken}` },
    data: {
      username: scenario.username,
      enabled: true,
      email: `${scenario.username}@example.com`,
      emailVerified: true,
      firstName: 'E2E',
      lastName: 'Customer',
      credentials: [{ type: 'password', value: scenario.password, temporary: false }],
    },
  });
  expect(created.status()).toBe(201);
  scenario.customerId = created.headers()['location'].split('/').pop()!;
}

/** Logs in through Keycloak's real login page (spec 012, AC-7). */
export async function logIn(page: Page, scenario: Scenario): Promise<void> {
  await page.getByRole('button', { name: 'Log in' }).click();
  await page.getByLabel('Username or email').fill(scenario.username);
  await page.getByLabel('Password', { exact: true }).fill(scenario.password);
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText(`Logged in as ${scenario.username}`)).toBeVisible();
}
