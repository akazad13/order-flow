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
  /** The access token right after logging in (spec 015: renewed later). */
  firstToken?: string;
};

/** Someone who can log in through Keycloak's page. */
export type Credentials = { username: string; password: string };

export const test = base.extend<{ scenario: Scenario; shopPage: Page }>({
  scenario: async ({}, use) => {
    const username = `e2e-${randomUUID().slice(0, 13)}`;
    await use({ customerId: '', username, password: randomUUID(), orderReads: [], hubFrames: [] });
  },
  // A second browser, for someone working at the shop next to the customer: staff (spec 015) or an admin (spec 017).
  shopPage: async ({ browser, baseURL }, use) => {
    const context = await browser.newContext({ baseURL });
    await use(await context.newPage());
    await context.close();
  },
});

export const { Given, When, Then } = createBdd(test);

export const inventoryUrl = process.env.INVENTORY_URL ?? 'http://localhost:5102';
export const paymentUrl = process.env.PAYMENT_URL ?? 'http://localhost:5103';
export const keycloakUrl = process.env.KEYCLOAK_URL ?? 'http://localhost:8180';

// Keycloak's dev admin from docker-compose.yml (local and CI only, ADR 0008).
const keycloakAdmin = { username: process.env.KEYCLOAK_ADMIN ?? 'admin', password: process.env.KEYCLOAK_ADMIN_PASSWORD ?? 'admin' };

// The dev-realm client for tests, holding the realm role `admin` (spec 017, Q3). Never in production.
const e2eClient = { id: process.env.E2E_CLIENT_ID ?? 'orderflow-e2e', secret: process.env.E2E_CLIENT_SECRET ?? 'orderflow-e2e-secret' };

/** An admin's access token, for setting stock through the API (spec 017). */
export async function adminToken(request: APIRequestContext): Promise<string> {
  const response = await request.post(`${keycloakUrl}/realms/orderflow/protocol/openid-connect/token`, {
    form: { grant_type: 'client_credentials', client_id: e2eClient.id, client_secret: e2eClient.secret },
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).access_token;
}

/** Calls Keycloak's admin API (realm `orderflow`) as the dev admin. */
async function keycloakAdminApi(request: APIRequestContext) {
  const login = await request.post(`${keycloakUrl}/realms/master/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'admin-cli', ...keycloakAdmin },
  });
  expect(login.ok()).toBeTruthy();
  const headers = { Authorization: `Bearer ${(await login.json()).access_token}` };
  const realm = `${keycloakUrl}/admin/realms/orderflow`;
  return {
    get: (path: string) => request.get(`${realm}${path}`, { headers }),
    post: (path: string, data?: unknown) => request.post(`${realm}${path}`, { headers, data }),
  };
}

/** Creates a user in the `orderflow` realm, with realm roles (spec 015), and returns its ID. */
export async function createUser(request: APIRequestContext, user: Credentials, roles: string[] = []): Promise<string> {
  const admin = await keycloakAdminApi(request);
  const created = await admin.post('/users', {
    username: user.username,
    enabled: true,
    email: `${user.username}@example.com`,
    emailVerified: true,
    firstName: 'E2E',
    lastName: roles.length ? 'Staff' : 'Customer',
    credentials: [{ type: 'password', value: user.password, temporary: false }],
  });
  expect(created.status()).toBe(201);
  const id = created.headers()['location'].split('/').pop()!;

  if (roles.length) {
    const found = await Promise.all(roles.map(async (role) => (await admin.get(`/roles/${role}`)).json()));
    expect((await admin.post(`/users/${id}/role-mappings/realm`, found)).ok()).toBeTruthy();
  }
  return id;
}

/**
 * Creates the scenario's own customer in the `orderflow` realm (spec 012, Q5: fresh data per test)
 * and records the user ID, which is the customer ID every service sees.
 */
export async function createCustomer(request: APIRequestContext, scenario: Scenario): Promise<void> {
  scenario.customerId = await createUser(request, scenario);
}

/** Ends every session the user has at Keycloak, as an administrator would (spec 015, AC-7). */
export async function endSessions(request: APIRequestContext, userId: string): Promise<void> {
  const admin = await keycloakAdminApi(request);
  expect((await admin.post(`/users/${userId}/logout`)).ok()).toBeTruthy();
}

/** Logs in through Keycloak's real login page (spec 012, AC-7). */
export async function logIn(page: Page, user: Credentials): Promise<void> {
  await page.getByRole('button', { name: 'Log in' }).click();
  await page.getByLabel('Username or email').fill(user.username);
  await page.getByLabel('Password', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText(`Logged in as ${user.username}`)).toBeVisible();
}

/** The access token oidc-client-ts keeps for the page (session storage). */
export function storedToken(page: Page): Promise<string | null> {
  return page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((k) => k.startsWith('oidc.user:'));
    return key ? (JSON.parse(sessionStorage.getItem(key)!).access_token as string) : null;
  });
}
