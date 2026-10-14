// Tests for the contract gate (spec 014, AC-7). Rules first, then whole runs against tiny git
// repositories in temp folders (the OpenAPI ones run the real oasdiff image in Docker).
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { asyncapiBreaks, checkContracts, httpBreaks, majorRaised, schemaBreaks, versionProblems } from './check-contracts.mjs';

const PAYLOAD = {
  type: 'object',
  required: ['orderId', 'reason'],
  properties: {
    orderId: { type: 'string', format: 'uuid' },
    reason: { type: 'string', enum: ['OutOfStock', 'PaymentDeclined'] },
    note: { type: 'string' },
  },
};
const clone = (value) => structuredClone(value);

describe('schemaBreaks (AC-2, AC-3)', () => {
  it('finds nothing in an unchanged schema, or one with a new optional field', () => {
    const after = clone(PAYLOAD);
    after.properties.extra = { type: 'integer' };
    assert.deepEqual(schemaBreaks(PAYLOAD, clone(PAYLOAD), 'M'), []);
    assert.deepEqual(schemaBreaks(PAYLOAD, after, 'M'), []);
  });

  it('finds a removed field', () => {
    const after = clone(PAYLOAD);
    delete after.properties.note;
    assert.deepEqual(schemaBreaks(PAYLOAD, after, 'M'), ['M.note: removed']);
  });

  it('finds a field made required, and one made optional', () => {
    const after = clone(PAYLOAD);
    after.required = ['orderId', 'note'];
    assert.deepEqual(schemaBreaks(PAYLOAD, after, 'M'), ['M.note: now required', 'M.reason: no longer required']);
  });

  it('finds a changed type or format', () => {
    const after = clone(PAYLOAD);
    after.properties.orderId = { type: 'integer' };
    assert.deepEqual(schemaBreaks(PAYLOAD, after, 'M'), [
      'M.orderId: type changed from "string" to "integer"',
      'M.orderId: format changed from "uuid" to none',
    ]);
  });

  it('finds allowed values added and removed (Q4)', () => {
    const after = clone(PAYLOAD);
    after.properties.reason.enum = ['OutOfStock', 'CustomerRequested'];
    const breaks = schemaBreaks(PAYLOAD, after, 'M');
    assert.equal(breaks.length, 2);
    assert.match(breaks[0], /M\.reason: allowed values removed: "PaymentDeclined"/);
    assert.match(breaks[1], /M\.reason: allowed values added: "CustomerRequested"/);
  });

  it('compares oneOf choices one by one', () => {
    const before = { oneOf: [{ type: 'null' }, clone(PAYLOAD)] };
    const after = clone(before);
    delete after.oneOf[1].properties.note;
    assert.deepEqual(schemaBreaks(before, after, 'M'), ['M(oneOf 2).note: removed']);
  });

  it('looks inside arrays', () => {
    const before = { type: 'array', items: clone(PAYLOAD) };
    const after = clone(before);
    after.items.properties.note.type = 'number';
    assert.deepEqual(schemaBreaks(before, after, 'M'), ['M[].note: type changed from "string" to "number"']);
  });
});

describe('asyncapiBreaks (AC-2)', () => {
  const doc = {
    channels: { orderPlaced: { address: 'order-placed', messages: { OrderPlaced: { payload: clone(PAYLOAD) } } } },
    operations: { publishOrderPlaced: { action: 'send' } },
  };

  it('finds a removed channel and operation', () => {
    assert.deepEqual(asyncapiBreaks(doc, { channels: {}, operations: {} }), ['channel orderPlaced: removed', 'operation publishOrderPlaced: removed']);
  });

  it("finds a changed address, a removed message and a changed operation's action", () => {
    const after = clone(doc);
    after.channels.orderPlaced.address = 'orders-placed';
    after.channels.orderPlaced.messages = {};
    after.operations.publishOrderPlaced.action = 'receive';
    assert.deepEqual(asyncapiBreaks(doc, after), [
      'channel orderPlaced: address changed from "order-placed" to "orders-placed"',
      'channel orderPlaced: message OrderPlaced removed',
      'operation publishOrderPlaced: action changed from send to receive',
    ]);
  });

  it('finds payload breaks, and accepts new channels and operations (AC-3)', () => {
    const after = clone(doc);
    delete after.channels.orderPlaced.messages.OrderPlaced.payload.properties.note;
    after.channels.stockReserved = { address: 'stock-reserved', messages: {} };
    after.operations.receiveStockReserved = { action: 'receive' };
    assert.deepEqual(asyncapiBreaks(doc, after), ['OrderPlaced.note: removed']);
  });
});

describe('httpBreaks (AC-1)', () => {
  it('counts warnings, errors and a newly required login; not other information', () => {
    const changelog = [
      { id: 'request-property-removed', level: 2, operation: 'POST', path: '/orders', text: 'removed `customerId`' },
      { id: 'api-path-removed-without-deprecation', level: 3, operation: 'GET', path: '/stock', text: 'api path removed' },
      { id: 'api-global-security-added', level: 1, text: 'the security scheme `bearer` was added to the API' },
      { id: 'response-non-success-status-added', level: 1, operation: 'POST', path: '/orders', text: 'added 401' },
    ];
    assert.deepEqual(httpBreaks(changelog), [
      'POST /orders: removed `customerId`',
      'GET /stock: api path removed',
      'the security scheme `bearer` was added to the API',
    ]);
  });
});

describe('versions (R-1, AC-4)', () => {
  it('treats the first non-zero part as the major version', () => {
    assert.equal(majorRaised('1.4.0', '2.0.0'), true);
    assert.equal(majorRaised('1.4.0', '1.5.0'), false);
    assert.equal(majorRaised('0.4.0', '0.5.0'), true);
    assert.equal(majorRaised('0.4.0', '0.4.1'), false);
    assert.equal(majorRaised('0.5.0', '1.0.0'), true);
  });

  it('wants a new version for any change, never a lower one', () => {
    assert.deepEqual(versionProblems({ before: '1.0.0', after: '1.0.0', changed: false, breaks: [] }), []);
    assert.deepEqual(versionProblems({ before: '1.0.0', after: '1.1.0', changed: true, breaks: [] }), []);
    assert.match(versionProblems({ before: '1.0.0', after: '1.0.0', changed: true, breaks: [] })[0], /still 1\.0\.0/);
    assert.match(versionProblems({ before: '1.2.0', after: '1.1.0', changed: true, breaks: [] })[0], /went down/);
  });

  it('wants a new major version for a breaking change', () => {
    assert.deepEqual(versionProblems({ before: '1.0.0', after: '2.0.0', changed: true, breaks: ['x'] }), []);
    assert.deepEqual(versionProblems({ before: '1.0.0', after: '1.1.0', changed: true, breaks: ['x: removed'] }), [
      '1 breaking change(s) need a new major version (1.0.0 to 1.1.0):',
      '  - x: removed',
    ]);
  });
});

// ---------------------------------------------------------------------------------------------

const ASYNCAPI = (version, schemaFile = 'thing-happened.v1.json') => `asyncapi: 3.1.0
info:
  title: Events
  version: ${version}
channels:
  thingHappened:
    address: thing-happened
    messages:
      ThingHappened:
        payload:
          $ref: './schemas/${schemaFile}'
operations:
  publishThingHappened:
    action: send
    channel:
      $ref: '#/channels/thingHappened'
`;

const OPENAPI = (version, required = ['sku', 'quantity']) => `openapi: 3.1.0
info:
  title: Shop
  version: ${version}
paths:
  /orders:
    post:
      operationId: placeOrder
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: ${JSON.stringify(required)}
              properties:
                sku: { type: string }
                quantity: { type: integer }
      responses:
        '201':
          description: Placed.
`;

describe('checkContracts against a git repository (AC-4, AC-5)', () => {
  let root;
  afterEach(() => rmSync(root, { recursive: true, force: true }));

  const write = (file, text) => {
    const full = path.join(root, file);
    mkdirSync(path.dirname(full), { recursive: true });
    writeFileSync(full, text);
  };
  const git = (...args) => execFileSync('git', args, { cwd: root, stdio: 'pipe' });
  const repo = () => {
    root = mkdtempSync(path.join(tmpdir(), 'check-contracts-'));
    git('init', '-q');
    git('config', 'user.email', 'test@example.com');
    git('config', 'user.name', 'Test');
    write('contracts/events/asyncapi.yaml', ASYNCAPI('1.0.0'));
    write('contracts/events/schemas/thing-happened.v1.json', JSON.stringify(clone(PAYLOAD)));
    write('contracts/http/shop.openapi.yaml', OPENAPI('1.0.0'));
    git('add', '-A');
    git('commit', '-q', '-m', 'base');
  };
  const noOasdiff = () => [];

  it('passes an unchanged working tree, and a base that is not a commit', async () => {
    repo();
    assert.deepEqual((await checkContracts({ root, oasdiff: noOasdiff })).problems, []);
    assert.deepEqual((await checkContracts({ root, base: 'no-such-branch', oasdiff: noOasdiff })).problems, []);
  });

  it('fails a schema file change when the document keeps its version', async () => {
    repo();
    const payload = clone(PAYLOAD);
    payload.properties.extra = { type: 'string' };
    write('contracts/events/schemas/thing-happened.v1.json', JSON.stringify(payload));

    const { problems } = await checkContracts({ root, oasdiff: noOasdiff });
    assert.deepEqual(problems, ['contracts/events/asyncapi.yaml:', '  changed, but the version is still 1.0.0 (AC-4)']);
  });

  it('passes a compatible change with a new minor version (AC-3)', async () => {
    repo();
    const payload = clone(PAYLOAD);
    payload.properties.extra = { type: 'string' };
    write('contracts/events/schemas/thing-happened.v1.json', JSON.stringify(payload));
    write('contracts/events/asyncapi.yaml', ASYNCAPI('1.1.0'));

    const result = await checkContracts({ root, oasdiff: noOasdiff });
    assert.deepEqual(result.problems, []);
    assert.deepEqual(result.notes, []);
  });

  it('fails a breaking change to a published event, even with a new major version (constitution IV)', async () => {
    repo();
    const payload = clone(PAYLOAD);
    delete payload.properties.note;
    write('contracts/events/schemas/thing-happened.v1.json', JSON.stringify(payload));
    write('contracts/events/asyncapi.yaml', ASYNCAPI('1.1.0'));
    assert.deepEqual((await checkContracts({ root, oasdiff: noOasdiff })).problems, [
      'contracts/events/asyncapi.yaml:',
      '  1 breaking change(s) need a new major version (1.0.0 to 1.1.0):',
      '    - ThingHappened.note: removed',
      '  published events edited in a breaking way; publish a new version of the event instead (constitution IV):',
      '    - ThingHappened.note: removed',
    ]);

    write('contracts/events/asyncapi.yaml', ASYNCAPI('2.0.0'));
    assert.deepEqual((await checkContracts({ root, oasdiff: noOasdiff })).problems, [
      'contracts/events/asyncapi.yaml:',
      '  published events edited in a breaking way; publish a new version of the event instead (constitution IV):',
      '    - ThingHappened.note: removed',
    ]);
  });

  it('allows retiring a published event with a new major version, and notes it', async () => {
    repo();
    write('contracts/events/asyncapi.yaml', ASYNCAPI('2.0.0').replace(/channels:[\s\S]*/, 'channels: {}\noperations: {}\n'));
    const { problems, notes } = await checkContracts({ root, oasdiff: noOasdiff });
    assert.deepEqual(problems, []);
    assert.match(notes.join('\n'), /allowed by the new major version \(1\.0\.0 to 2\.0\.0\)[\s\S]*channel thingHappened: removed[\s\S]*operation publishThingHappened: removed/);
  });

  it('allows a breaking change to other AsyncAPI contracts with a new major version (AC-2)', async () => {
    repo();
    const payload = clone(PAYLOAD);
    delete payload.properties.note;
    write('contracts/realtime/updates.asyncapi.yaml', ASYNCAPI('1.0.0'));
    write('contracts/realtime/schemas/thing-happened.v1.json', JSON.stringify(PAYLOAD));
    git('add', '-A');
    git('commit', '-q', '-m', 'live updates');

    write('contracts/realtime/schemas/thing-happened.v1.json', JSON.stringify(payload));
    write('contracts/realtime/updates.asyncapi.yaml', ASYNCAPI('1.1.0'));
    assert.match((await checkContracts({ root, oasdiff: noOasdiff })).problems.join('\n'), /need a new major version[\s\S]*ThingHappened\.note: removed/);

    write('contracts/realtime/updates.asyncapi.yaml', ASYNCAPI('2.0.0'));
    const allowed = await checkContracts({ root, oasdiff: noOasdiff });
    assert.deepEqual(allowed.problems, []);
    assert.match(allowed.notes.join('\n'), /allowed by the new major version \(1\.0\.0 to 2\.0\.0\)[\s\S]*ThingHappened\.note: removed/);
  });

  it('compares committed revisions, from their merge base', async () => {
    repo();
    git('checkout', '-q', '-b', 'feature');
    write('contracts/events/asyncapi.yaml', ASYNCAPI('1.0.1').replace('title: Events', 'title: The events'));
    git('commit', '-q', '-am', 'retitle without a new version');
    git('checkout', '-q', '-');
    write('contracts/events/asyncapi.yaml', ASYNCAPI('1.1.0').replace('title: Events', 'title: Events!'));
    git('commit', '-q', '-am', 'base moves on');

    // Against the base branch's tip, the feature would look like it lowered the version (1.1.0 to
    // 1.0.1); from the merge base it changed the title with a new patch version: fine.
    const result = await checkContracts({ root, base: 'HEAD', head: 'feature', oasdiff: noOasdiff });
    assert.deepEqual(result.problems, []);
  });

  it('fails a removed contract, and passes a new one', async () => {
    repo();
    rmSync(path.join(root, 'contracts/http'), { recursive: true });
    write('contracts/realtime/updates.asyncapi.yaml', ASYNCAPI('1.0.0'));
    write('contracts/realtime/schemas/thing-happened.v1.json', JSON.stringify(PAYLOAD));
    assert.deepEqual((await checkContracts({ root, oasdiff: noOasdiff })).problems, [
      'contracts/http/shop.openapi.yaml: removed, which breaks every client and service that uses it',
    ]);
  });

  it('fails a breaking HTTP change found by the real oasdiff (AC-1)', async () => {
    repo();
    write('contracts/http/shop.openapi.yaml', OPENAPI('1.1.0', ['sku', 'quantity', 'note']).replace('quantity: { type: integer }', 'quantity: { type: integer }\n                note: { type: string }'));

    const { problems } = await checkContracts({ root });
    assert.equal(problems[0], 'contracts/http/shop.openapi.yaml:');
    assert.match(problems.join('\n'), /need a new major version \(1\.0\.0 to 1\.1\.0\)[\s\S]*POST \/orders: .*note/);
  });
});
