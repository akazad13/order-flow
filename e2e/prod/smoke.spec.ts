// Spec 019: smoke test of the production profile (deploy/prod/docker-compose.prod.yml).
// Runs in: e2e/ with playwright.prod.config.ts, against the production profile only.
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { connect } from 'node:net';
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

/** True if something accepts a TCP connection on localhost at the port. */
function listening(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = connect({ host: '127.0.0.1', port, timeout: 3_000 });
    socket.once('connect', () => (socket.destroy(), resolve(true)));
    socket.once('timeout', () => (socket.destroy(), resolve(false)));
    socket.once('error', () => resolve(false));
  });
}

/** An HTTP request from inside the profile's network: bash's /dev/tcp in the Order container (spec 021, Q2). Returns the status line. */
function fromInside(method: string, host: string, path: string): string {
  const script = `exec 3<>/dev/tcp/${host}/8080 && printf '${method} ${path} HTTP/1.1\r\nHost: ${host}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n' >&3 && head -1 <&3`;
  return execFileSync('docker', ['exec', container('order'), 'bash', '-c', script], { encoding: 'utf8' }).trim();
}

test('Only the shop and Keycloak are reachable from outside (spec 021, AC-1)', async () => {
  expect(await listening(5443), 'the shop').toBe(true);
  expect(await listening(8443), 'Keycloak').toBe(true);
  for (const [port, what] of [[5101, 'Order'], [5102, 'Inventory'], [5103, 'Payment'], [5104, 'Notification'], [5432, 'PostgreSQL'], [5672, 'RabbitMQ'], [15672, "RabbitMQ's management UI"]] as const) {
    expect(await listening(port), `${what} (${port})`).toBe(false);
  }
});

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

  // The gateway is the only client (spec 023); its address and secret came from the environment on import (Q2).
  const clients = (await admin.get('/clients')) as { clientId: string; id: string; redirectUris?: string[] }[];
  const gateway = clients.find((c) => c.clientId === 'orderflow-gateway');
  expect(gateway?.redirectUris).toEqual(['https://localhost:5443/bff/signin-oidc']);
  expect(clients.map((c) => c.clientId)).not.toContain('orderflow-web');
  expect((await admin.get(`/clients/${gateway!.id}/client-secret`)).value).toBe(secrets.ORDERFLOW_GATEWAY_SECRET);
});

test('Nothing test-only is switched on (AC-6; checked through the shop and from inside, spec 021 AC-3)', async ({ request }) => {
  expect(fromInside('PUT', 'payment', '/test/stalls/someone')).toMatch(/^HTTP\/1\.1 404/);
  // Through the gateway, as the page would call (spec 023: with X-CSRF), but without a session: 401.
  expect((await request.put('/inventory-api/stock/SKU-001', { headers: { 'X-CSRF': '1' }, data: { name: 'x', unitPrice: 1, available: 1 } })).status()).toBe(401);
  // Without the page's header, refused before anything else (spec 023, AC-5).
  expect((await request.put('/inventory-api/stock/SKU-001', { data: { name: 'x', unitPrice: 1, available: 1 } })).status()).toBe(403);
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

    // Spec 023, AC-1 and AC-7: no token reachable by the page; the session cookie is Secure and HttpOnly.
    const visible = await page.evaluate(() => [...Object.values({ ...sessionStorage }), ...Object.values({ ...localStorage }), document.cookie].join(' '));
    expect(visible).not.toContain('eyJ');
    const session = (await page.context().cookies()).find((c) => c.name === 'orderflow.session');
    expect(session?.secure).toBe(true);
    expect(session?.httpOnly).toBe(true);
  } finally {
    await admin.delete(`/users/${userId}`);
  }
});

test('Keycloak keeps its realm and users in its own PostgreSQL, across a restart (AC-2)', async ({ request }) => {
  // A restart takes Keycloak 25 to 30 seconds, about Playwright's default for a whole test (spec 023 found it).
  test.setTimeout(180_000);
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
