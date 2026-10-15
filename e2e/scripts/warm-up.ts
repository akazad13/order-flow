import { randomUUID } from 'node:crypto';
import { chromium, request, type FullConfig } from '@playwright/test';

// Spec 023: one real login through the gateway and Keycloak's page before the suite. Right after the
// stack starts, the first logins are slow (Keycloak's login page, the code exchange, the gateway's
// first fetch of Keycloak's metadata) and the first scenarios timed out on them. wait-for-stack.mjs
// warms the services; this warms the login. Development stack only (it uses Keycloak's dev admin).
const keycloak = process.env.KEYCLOAK_URL ?? 'http://localhost:8180';
const admin = { username: process.env.KEYCLOAK_ADMIN ?? 'admin', password: process.env.KEYCLOAK_ADMIN_PASSWORD ?? 'admin' };

export default async function warmUp(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0].use.baseURL!;
  const api = await request.newContext();
  const token = (await (await api.post(`${keycloak}/realms/master/protocol/openid-connect/token`, { form: { grant_type: 'password', client_id: 'admin-cli', ...admin } })).json()).access_token;
  const headers = { Authorization: `Bearer ${token}` };
  const user = { username: `warm-up-${randomUUID().slice(0, 8)}`, password: randomUUID() };
  const created = await api.post(`${keycloak}/admin/realms/orderflow/users`, {
    headers,
    data: { username: user.username, enabled: true, email: `${user.username}@example.com`, emailVerified: true, firstName: 'Warm', lastName: 'Up', credentials: [{ type: 'password', value: user.password, temporary: false }] },
  });
  if (created.status() !== 201) throw new Error(`warm-up user not created: ${created.status()} ${await created.text()}`);
  const userId = created.headers()['location']!.split('/').pop();

  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ baseURL });
    await page.goto('/');
    await page.getByRole('button', { name: 'Log in' }).click({ timeout: 60_000 });
    await page.getByLabel('Username or email').fill(user.username, { timeout: 60_000 });
    await page.getByLabel('Password', { exact: true }).fill(user.password);
    await page.getByRole('button', { name: 'Sign In' }).click();
    try {
      await page.getByText(`Logged in as ${user.username}`).waitFor({ timeout: 60_000 });
    } catch (error) {
      console.error(`warm-up login stuck at ${page.url()}: ${(await page.locator('body').innerText()).slice(0, 400)}`);
      throw error;
    }
    console.log('warm: a login went through the gateway and Keycloak');
  } finally {
    await browser.close();
    await api.delete(`${keycloak}/admin/realms/orderflow/users/${userId}`, { headers });
    await api.dispose();
  }
}
