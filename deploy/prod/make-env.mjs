#!/usr/bin/env node
// Spec 019 (AC-4, R-2): the production profile's secrets, in deploy/prod/.env, which git ignores.
// Makes the file with random passwords if it is missing; never overwrites one. The variables are
// listed in deploy/prod/.env.example.
import { randomBytes } from 'node:crypto';
import { existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const file = path.join(path.dirname(fileURLToPath(import.meta.url)), '.env');
if (existsSync(file)) {
  console.log(`${file} exists; not changed.`);
  process.exit(0);
}

const secret = () => randomBytes(24).toString('base64url');
writeFileSync(
  file,
  [
    '# Made by deploy/prod/make-env.mjs (spec 019). Never commit this file.',
    'KEYCLOAK_ADMIN=orderflow-admin',
    `KEYCLOAK_ADMIN_PASSWORD=${secret()}`,
    `KEYCLOAK_DB_PASSWORD=${secret()}`,
    '',
  ].join('\n'),
  { mode: 0o600 },
);
console.log(`Wrote ${file} with new random passwords.`);
