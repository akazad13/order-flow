// Spec 019: smoke test of the production profile (deploy/prod/docker-compose.prod.yml).
// Runs in: e2e/ with playwright.prod.config.ts, against the production profile only.
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type APIRequestContext } from '@playwright/test';

const keycloak = 'https://localhost:8443';
const realm = `${keycloak}/realms/orderflow`;
const project = process.env.COMPOSE_PROJECT_NAME ?? 'orderflow-prod';

// The secrets file that make-env.mjs wrote (AC-4): the test reads it like an operator would.
const secrets = Object.fromEntries(
  readFileSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../deploy/prod/.env'), 'utf8')
    .split('\n')
    .filter((line) => /^[A-Z_]+=/.test(line))
    .map((line) => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1).trim()]),
);

test.describe.configure({ mode: 'serial' });

async function adminToken(request: APIRequestContext, username: string, password: string) {
  return request.post(`${keycloak}/realms/master/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'admin-cli', username, password },
  });
}

async function adminApi(request: APIRequestContext) {
  const login = await adminToken(request, secrets.KEYCLOAK_ADMIN, secrets.KEYCLOAK_ADMIN_PASSWORD);
  expect(login.ok()).toBeTruthy();
  const headers = { Authorization: `Bearer ${(await login.json()).access_token}` };
  return {
    get: async (p: string) => (await request.get(`${keycloak}/admin/realms/orderflow${p}`, { headers })).json(),
    post: (p: string, data: unknown) => request.post(`${keycloak}/admin/realms/orderflow${p}`, { headers, data }),
    delete: (p: string) => request.delete(`${keycloak}/admin/realms/orderflow${p}`, { headers }),
  };
}

/** One of the profile's containers, by compose service name. */
function container(service: string): string {
  const id = execFileSync('docker', ['ps', '-q', '--filter', `label=com.docker.compose.project=${project}`, '--filter', `label=com.docker.compose.service=${service}`], { encoding: 'utf8' }).trim();
  expect(id, `the ${service} container of ${project}`).toBeTruthy();
  return id.split('\n')[0];
}

test('Keycloak answers only over HTTPS, and tokens name the HTTPS address (AC-3)', async ({ request }) => {
  const metadata = await request.get(`${realm}/.well-known/openid-configuration`);
  expect(metadata.ok()).toBeTruthy();
  expect((await metadata.json()).issuer).toBe(realm);

  // Plain HTTP on the same port, or the development port: nothing answers.
  await expect(request.get('http://localhost:8443/realms/orderflow', { timeout: 5_000 })).rejects.toThrow();
  await expect(request.get('http://localhost:8180/realms/orderflow', { timeout: 5_000 })).rejects.toThrow();
});

test("Keycloak's administrator comes from the secrets file; admin / admin does not work (AC-4)", async ({ request }) => {
  expect((await adminToken(request, 'admin', 'admin')).status()).toBe(401);
  expect((await adminToken(request, secrets.KEYCLOAK_ADMIN, secrets.KEYCLOAK_ADMIN_PASSWORD)).ok()).toBeTruthy();
});

test('The production realm has no demo users and no test client (AC-1)', async ({ request }) => {
  const admin = await adminApi(request);
  for (const username of ['alice', 'sam', 'ada']) {
    expect(await admin.get(`/users?username=${username}&exact=true`), username).toEqual([]);
  }
  expect(await admin.get('/clients?clientId=orderflow-e2e')).toEqual([]);

  // The web client's address came from the environment on import (Q2).
  const [web] = await admin.get('/clients?clientId=orderflow-web');
  expect(web.redirectUris).toEqual(['https://localhost:5443/*']);
});

test('Nothing test-only is switched on (AC-6)', async ({ request }) => {
  expect((await request.put('http://localhost:5103/test/stalls/someone')).status()).toBe(404);
  expect((await request.put('http://localhost:5102/stock/SKU-001', { data: { name: 'x', unitPrice: 1, available: 1 } })).status()).toBe(401);
});

test('A customer logs in through the real page over HTTPS and an order is confirmed (AC-5)', async ({ page, request }) => {
  const admin = await adminApi(request);
  const customer = { username: `smoke-${randomUUID().slice(0, 8)}`, password: randomUUID() };
  const created = await admin.post('/users', {
    username: customer.username,
    enabled: true,
    email: `${customer.username}@example.com`,
    emailVerified: true,
    firstName: 'Smoke',
    lastName: 'Test',
    credentials: [{ type: 'password', value: customer.password, temporary: false }],
  });
  expect(created.status()).toBe(201);
  const userId = created.headers()['location'].split('/').pop()!;

  try {
    await page.goto('/');
    expect(page.url()).toMatch(/^https:\/\/localhost:5443\//);
    await page.getByRole('button', { name: 'Log in' }).click();
    await expect(page).toHaveURL(/^https:\/\/localhost:8443\/realms\/orderflow\//);
    await page.getByLabel('Username or email').fill(customer.username);
    await page.getByLabel('Password', { exact: true }).fill(customer.password);
    await page.getByRole('button', { name: 'Sign In' }).click();
    await expect(page.getByText(`Logged in as ${customer.username}`)).toBeVisible();

    await page.getByLabel('SKU').fill('SKU-001');
    await page.getByLabel('Quantity').fill('1');
    await page.getByRole('button', { name: 'Place order' }).click();
    await expect(page.getByRole('region', { name: 'Order' })).toContainText('Status: Confirmed', { timeout: 20_000 });
  } finally {
    await admin.delete(`/users/${userId}`);
  }
});

test('Keycloak keeps its realm and users in its own PostgreSQL, across a restart (AC-2)', async ({ request }) => {
  const realms = execFileSync('docker', ['exec', container('keycloak-db'), 'psql', '-U', 'keycloak', '-d', 'keycloak', '-tAc', "select count(*) from realm where name = 'orderflow'"], { encoding: 'utf8' });
  expect(realms.trim()).toBe('1');

  const admin = await adminApi(request);
  const username = `persist-${randomUUID().slice(0, 8)}`;
  expect((await admin.post('/users', { username, enabled: true })).status()).toBe(201);

  execFileSync('docker', ['restart', container('keycloak')]);
  await expect
    .poll(async () => (await request.get(`${realm}/.well-known/openid-configuration`).catch(() => null))?.ok() ?? false, { timeout: 120_000, intervals: [2_000] })
    .toBe(true);

  const after = await adminApi(request);
  const [user] = await after.get(`/users?username=${username}&exact=true`);
  expect(user?.username).toBe(username);
  await after.delete(`/users/${user.id}`);
});
