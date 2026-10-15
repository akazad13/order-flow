// Tests for the realm builder (spec 019, AC-1).
import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { check, devRealm, productionProblems } from './build-dev-realm.mjs';

const PROD = {
  realm: 'orderflow',
  roles: { realm: [{ name: 'staff' }] },
  clients: [
    {
      clientId: 'orderflow-gateway',
      secret: '${ORDERFLOW_GATEWAY_SECRET}',
      redirectUris: ['${ORDERFLOW_WEB_URL}/*'],
      webOrigins: ['${ORDERFLOW_WEB_URL}'],
      attributes: { 'pkce.code.challenge.method': 'S256', 'post.logout.redirect.uris': '${ORDERFLOW_WEB_URL}/*' },
      protocolMappers: [{ name: 'roles' }],
    },
  ],
};

describe('productionProblems', () => {
  it('passes a clean production realm', () => {
    assert.deepEqual(productionProblems(PROD), []);
  });

  it('refuses users, other clients, secrets and plain-HTTP addresses', () => {
    const realm = structuredClone(PROD);
    realm.users = [{ username: 'alice', credentials: [{ value: 'alice' }] }];
    realm.clients.push({ clientId: 'orderflow-e2e', secret: 'shh', redirectUris: [] });
    realm.clients[0].secret = 'a-real-secret';
    realm.clients[0].redirectUris.push('http://localhost:5180/*');

    const problems = productionProblems(realm).join('\n');
    assert.match(problems, /it has users \(alice\)/);
    assert.match(problems, /it has the client orderflow-e2e/);
    assert.match(problems, /orderflow-gateway allows http:\/\/localhost:5180\/\*/);
    assert.match(problems, /it has a secret at realm\.clients\[1\]\.secret/);
    assert.match(problems, /it has a secret at realm\.clients\[0\]\.secret/, 'a literal secret, even on the gateway');
    assert.match(problems, /it has a credentials at realm\.users\[0\]\.credentials/);
  });
});

describe('devRealm', () => {
  const overlay = {
    clients: [
      { clientId: 'orderflow-gateway', secret: 'dev-only', redirectUris: ['http://localhost:5180/*'], attributes: { 'post.logout.redirect.uris': 'http://localhost:5180/*' } },
      { clientId: 'orderflow-e2e', secret: 'dev-only' },
    ],
    users: [{ username: 'alice' }],
    roles: { realm: [{ name: 'tester' }] },
  };

  it('adds to the production realm and removes nothing', () => {
    const dev = devRealm(PROD, overlay);

    assert.deepEqual(dev.clients.map((c) => c.clientId), ['orderflow-gateway', 'orderflow-e2e']);
    assert.equal(dev.clients[0].secret, 'dev-only');
    const web = dev.clients[0];
    assert.deepEqual(web.redirectUris, ['http://localhost:5180/*']);
    assert.deepEqual(web.webOrigins, ['${ORDERFLOW_WEB_URL}']);
    assert.equal(web.attributes['pkce.code.challenge.method'], 'S256');
    assert.equal(web.attributes['post.logout.redirect.uris'], 'http://localhost:5180/*');
    assert.deepEqual(web.protocolMappers, [{ name: 'roles' }]);
    assert.deepEqual(dev.users, [{ username: 'alice' }]);
    assert.deepEqual(dev.roles.realm.map((r) => r.name), ['staff', 'tester']);
    assert.deepEqual(PROD.clients.length, 1, 'the production realm itself is not changed');
  });
});

describe('the repository (AC-1)', () => {
  it('has a clean production realm and an up-to-date development realm', () => {
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    assert.deepEqual(check(root), []);
  });
});
