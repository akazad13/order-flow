#!/usr/bin/env node
// Realm builder (spec 019). The production realm (deploy/keycloak/orderflow-realm.json) is the single
// source of truth for clients, roles and mappers; development only adds to it
// (deploy/keycloak/dev-overlay.json): demo users, the E2E client, local addresses. This script merges
// the two into the development realm that docker compose imports (deploy/keycloak/dev/).
//
//   node scripts/build-dev-realm.mjs           writes the development realm
//   node scripts/build-dev-realm.mjs --check   fails if it is out of date, or the production realm
//                                              has a user, another client or a secret (AC-1)
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const PROD_REALM = 'deploy/keycloak/orderflow-realm.json';
export const DEV_OVERLAY = 'deploy/keycloak/dev-overlay.json';
export const DEV_REALM = 'deploy/keycloak/dev/orderflow-realm.json';
// The gateway is the realm's only client in production (spec 023; the browser's own client before it).
export const WEB_CLIENT = 'orderflow-gateway';

const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

/** Objects merge key by key; anything else (arrays included) is replaced by the overlay. */
function merge(base, overlay) {
  if (!isObject(base) || !isObject(overlay)) return structuredClone(overlay);
  const merged = structuredClone(base);
  for (const [key, value] of Object.entries(overlay)) merged[key] = key in merged ? merge(merged[key], value) : structuredClone(value);
  return merged;
}

/**
 * The development realm: the production realm, with the overlay's clients merged by clientId (or
 * added) and its users and realm roles added. Nothing in the production realm is removed (R-1).
 */
export function devRealm(prod, overlay) {
  const { clients = [], users = [], roles, ...rest } = overlay;
  const realm = merge(prod, rest);

  realm.clients = (prod.clients ?? []).map((client) => {
    const extra = clients.find((c) => c.clientId === client.clientId);
    return extra ? merge(client, extra) : structuredClone(client);
  });
  realm.clients.push(...clients.filter((c) => !(prod.clients ?? []).some((p) => p.clientId === c.clientId)).map((c) => structuredClone(c)));

  realm.roles = structuredClone(prod.roles ?? {});
  realm.roles.realm = [...(prod.roles?.realm ?? []), ...(roles?.realm ?? [])];
  realm.users = [...(prod.users ?? []), ...structuredClone(users)];
  return realm;
}

/** What must never be in the production realm (AC-1). */
export function productionProblems(prod) {
  const problems = [];
  if ((prod.users ?? []).length) problems.push(`it has users (${prod.users.map((u) => u.username).join(', ')}): users belong in the development overlay or are created in production`);

  for (const client of prod.clients ?? []) {
    if (client.clientId !== WEB_CLIENT) problems.push(`it has the client ${client.clientId}: only ${WEB_CLIENT} belongs in production`);
    for (const address of [...(client.redirectUris ?? []), ...(client.webOrigins ?? [])]) {
      if (!address.startsWith('${') && !address.startsWith('https://')) problems.push(`${client.clientId} allows ${address}: production addresses are HTTPS or come from the environment`);
    }
  }

  const walk = (value, at) => {
    if (Array.isArray(value)) value.forEach((v, i) => walk(v, `${at}[${i}]`));
    else if (isObject(value)) {
      for (const [key, v] of Object.entries(value)) {
        // A secret may only come from the environment, as a placeholder Keycloak resolves on import (spec 023).
        const placeholder = typeof v === 'string' && /^\$\{[A-Z0-9_]+\}$/.test(v);
        if (/secret|password|credentials/i.test(key) && !placeholder) problems.push(`it has a ${key} at ${at}.${key}: secrets stay out of git (R-2)`);
        walk(v, `${at}.${key}`);
      }
    }
  };
  walk(prod, 'realm');
  return problems;
}

const render = (realm) => `${JSON.stringify(realm, null, 2)}\n`;

/** Checks the repository: the production realm's rules, and the development realm is current. */
export function check(root) {
  const read = (file) => JSON.parse(readFileSync(path.join(root, file), 'utf8'));
  const prod = read(PROD_REALM);
  const problems = productionProblems(prod).map((p) => `${PROD_REALM}: ${p}`);

  let current = null;
  try {
    current = readFileSync(path.join(root, DEV_REALM), 'utf8');
  } catch {
    // missing: reported below
  }
  if (current?.replace(/\r\n/g, '\n') !== render(devRealm(prod, read(DEV_OVERLAY)))) {
    problems.push(`${DEV_REALM} is out of date: run node scripts/build-dev-realm.mjs`);
  }
  return problems;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  if (process.argv.includes('--check')) {
    const problems = check(root);
    for (const problem of problems) console.error(problem);
    if (problems.length) process.exit(1);
    console.log('The production realm is clean, and the development realm is up to date.');
  } else {
    const read = (file) => JSON.parse(readFileSync(path.join(root, file), 'utf8'));
    mkdirSync(path.dirname(path.join(root, DEV_REALM)), { recursive: true });
    writeFileSync(path.join(root, DEV_REALM), render(devRealm(read(PROD_REALM), read(DEV_OVERLAY))));
    console.log(`Wrote ${DEV_REALM}.`);
  }
}
