// Waits until every service in docker compose answers, so E2E never starts against a half-up stack.
// With --copies, also waits for the second copy of each service (docker-compose.copies.yml, spec 013).
// With --prod, waits for the production profile (spec 019): Keycloak and the shop over HTTPS,
// checked against the local CA from deploy/prod/make-certs.sh. The services' own ports are closed
// there (spec 021); `docker compose up --wait` waits for them to be healthy instead.
import { readFileSync } from 'node:fs';
import { get } from 'node:https';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const prod = process.argv.includes('--prod');
const targets = prod
  ? []
  : ['http://localhost:5101/health', 'http://localhost:5102/health', 'http://localhost:5103/health', 'http://localhost:5104/health', 'http://localhost:5105/health'];
if (prod) {
  targets.push('https://localhost:5443/', 'https://localhost:8443/realms/orderflow/.well-known/openid-configuration');
} else {
  targets.push('http://localhost:5180/', 'http://localhost:8180/realms/orderflow/.well-known/openid-configuration'); // Keycloak (spec 012)
}
if (process.argv.includes('--copies')) {
  targets.push(
    'http://localhost:5111/health',
    'http://localhost:5112/health',
    'http://localhost:5113/health',
    'http://localhost:5114/health',
    'http://localhost:5115/health', // the gateway (spec 023)
  );
}

const ca = prod ? readFileSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../deploy/prod/certs/ca.crt')) : undefined;

/** True if the URL answers 2xx; HTTPS is verified against the local CA. */
function answers(url) {
  if (!url.startsWith('https:')) {
    return fetch(url).then((r) => r.ok, () => false);
  }
  return new Promise((resolve) => {
    get(url, { ca }, (response) => {
      response.resume();
      resolve(response.statusCode >= 200 && response.statusCode < 300);
    }).on('error', () => resolve(false));
  });
}

const deadline = Date.now() + (prod ? 300_000 : 120_000);
const pause = () => new Promise((resolve) => setTimeout(resolve, 1000));

for (const url of targets) {
  for (;;) {
    if (await answers(url)) {
      console.log(`ready: ${url}`);
      break;
    }
    if (Date.now() > deadline) {
      console.error(`timed out waiting for ${url}`);
      process.exit(1);
    }
    await pause();
  }
}

// Healthy is not yet warm (found in spec 023): right after a start, each service's first real call
// loads its database model and fetches Keycloak's keys, and the first E2E steps timed out on it.
// So push one order through the whole flow first, as the dev-realm E2E client (spec 017), straight
// to the services. Not in the production profile: its ports are closed and it has no test client.
if (!prod) {
  const keycloak = process.env.KEYCLOAK_URL ?? 'http://localhost:8180';
  const login = await fetch(`${keycloak}/realms/orderflow/protocol/openid-connect/token`, {
    method: 'POST',
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: process.env.E2E_CLIENT_ID ?? 'orderflow-e2e',
      client_secret: process.env.E2E_CLIENT_SECRET ?? 'orderflow-e2e-secret',
    }),
  });
  const headers = { Authorization: `Bearer ${(await login.json()).access_token}`, 'Content-Type': 'application/json' };
  const sku = `WARM-UP-${Date.now()}`;
  await fetch(`http://localhost:5102/stock/${sku}`, { method: 'PUT', headers, body: JSON.stringify({ name: 'Warm-up item', unitPrice: 1, available: 1 }) });
  const placed = await fetch('http://localhost:5101/orders', { method: 'POST', headers, body: JSON.stringify({ lines: [{ sku, quantity: 1 }] }) });
  const { orderId } = await placed.json();
  for (;;) {
    const order = await (await fetch(`http://localhost:5101/orders/${orderId}`, { headers })).json();
    if (order.status === 'Confirmed') {
      console.log('warm: an order went through stock, payment and confirmation');
      break;
    }
    if (Date.now() > deadline + 60_000) {
      console.error(`timed out warming up: order ${orderId} is ${order.status}`);
      process.exit(1);
    }
    await pause();
  }
}
