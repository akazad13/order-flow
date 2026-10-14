// @vitest-environment node
/**
 * Consumer side of the web UI / Order API contract (spec 008). Each interaction calls the
 * real client the app uses against Pact's mock server, and Pact writes what the UI relies
 * on to pacts/. The Order API verifies that file in tests/OrderFlow.Order.PactTests.
 *
 * Matchers check types and formats, never generated values (spec 008, AC-2).
 */
import path from 'node:path';
import { MatchersV3, PactV4 } from '@pact-foundation/pact';
import { describe, expect, it } from 'vitest';
import { createOrderApi } from './clients';

const { uuid, like, regex, decimal, eachLike, fromProviderState } = MatchersV3;

const provider = new PactV4({
  consumer: 'orderflow-web',
  provider: 'orderflow-order-api',
  dir: path.resolve(__dirname, '../../../pacts'),
  logLevel: 'warn',
});

const EXAMPLE_ID = '8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d';
const isoTimestamp = (example: string) => regex('^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?(Z|[+-]\\d{2}:\\d{2})$', example);
const json = regex('^application/json(;.*)?$', 'application/json; charset=utf-8');

describe('Order API, as the web UI uses it', () => {
  it('places a valid order', () =>
    provider
      .addInteraction()
      .uponReceiving('a request to place a valid order')
      .withRequest('POST', '/orders', (request) =>
        request.jsonBody({ customerId: 'customer-1', lines: [{ sku: 'SKU-001', quantity: 2 }] }),
      )
      .willRespondWith(201, (response) =>
        response
          .headers({ 'Content-Type': json, Location: regex('^/orders/[0-9a-f-]{36}$', `/orders/${EXAMPLE_ID}`) })
          .jsonBody({
            orderId: uuid(EXAMPLE_ID),
            customerId: 'customer-1',
            status: 'Pending',
            lines: eachLike({ sku: like('SKU-001'), quantity: like(2) }),
            total: null,
            cancellation: null,
            placedAt: isoTimestamp('2026-10-14T10:00:00.0000000+00:00'),
          }),
      )
      .executeTest(async (mockServer) => {
        const result = await createOrderApi(mockServer.url).placeOrder('customer-1', 'SKU-001', 2);

        expect(result.ok).toBe(true);
        expect(result.ok && result.order.status).toBe('Pending');
      }));

  it('rejects an order with a zero quantity', () =>
    provider
      .addInteraction()
      .uponReceiving('a request to place an order with a zero quantity')
      .withRequest('POST', '/orders', (request) =>
        request.jsonBody({ customerId: 'customer-1', lines: [{ sku: 'SKU-001', quantity: 0 }] }),
      )
      .willRespondWith(400, (response) =>
        response
          .headers({ 'Content-Type': regex('^application/problem\\+json(;.*)?$', 'application/problem+json') })
          .jsonBody({
            title: like('One or more validation errors occurred.'),
            status: 400,
            errors: { 'lines[0].quantity': eachLike('Quantity must be at least 1.') },
          }),
      )
      .executeTest(async (mockServer) => {
        const result = await createOrderApi(mockServer.url).placeOrder('customer-1', 'SKU-001', 0);

        expect(result.ok).toBe(false);
        expect(!result.ok && Object.keys(result.problem.errors)).toContain('lines[0].quantity');
      }));

  it('reads a pending order', () =>
    provider
      .addInteraction()
      .given('a pending order exists')
      .uponReceiving('a request for a pending order')
      .withRequest('GET', fromProviderState('/orders/${orderId}', `/orders/${EXAMPLE_ID}`))
      .willRespondWith(200, (response) =>
        response.headers({ 'Content-Type': json }).jsonBody({
          orderId: uuid(EXAMPLE_ID),
          customerId: like('customer-1'),
          status: 'Pending',
          lines: eachLike({ sku: like('SKU-001'), quantity: like(2) }),
          total: null,
          cancellation: null,
          placedAt: isoTimestamp('2026-10-14T10:00:00.0000000+00:00'),
        }),
      )
      .executeTest(async (mockServer) => {
        const order = await createOrderApi(mockServer.url).getOrder(EXAMPLE_ID);

        expect(order?.status).toBe('Pending');
      }));

  it('reads a confirmed order with its total', () =>
    provider
      .addInteraction()
      .given('a confirmed order exists', { total: 37.0 })
      .uponReceiving('a request for a confirmed order')
      .withRequest('GET', fromProviderState('/orders/${orderId}', `/orders/${EXAMPLE_ID}`))
      .willRespondWith(200, (response) =>
        response.headers({ 'Content-Type': json }).jsonBody({
          orderId: uuid(EXAMPLE_ID),
          status: 'Confirmed',
          total: decimal(37.0),
          cancellation: null,
        }),
      )
      .executeTest(async (mockServer) => {
        const order = await createOrderApi(mockServer.url).getOrder(EXAMPLE_ID);

        expect(order?.status).toBe('Confirmed');
        expect(order?.total).toBe(37);
      }));

  it('reads a cancelled order with its reason', () =>
    provider
      .addInteraction()
      .given('an order cancelled for a shortage exists')
      .uponReceiving('a request for a cancelled order')
      .withRequest('GET', fromProviderState('/orders/${orderId}', `/orders/${EXAMPLE_ID}`))
      .willRespondWith(200, (response) =>
        response.headers({ 'Content-Type': json }).jsonBody({
          orderId: uuid(EXAMPLE_ID),
          status: 'Cancelled',
          cancellation: { reason: 'OutOfStock', detail: like('Only 1 of SKU-002 available, 2 requested.') },
        }),
      )
      .executeTest(async (mockServer) => {
        const order = await createOrderApi(mockServer.url).getOrder(EXAMPLE_ID);

        expect(order?.status).toBe('Cancelled');
        expect(order?.cancellation?.reason).toBe('OutOfStock');
      }));

  it('gets nothing for an unknown order', () =>
    provider
      .addInteraction()
      .uponReceiving('a request for an order that does not exist')
      .withRequest('GET', '/orders/00000000-0000-0000-0000-000000000000')
      .willRespondWith(404)
      .executeTest(async (mockServer) => {
        const order = await createOrderApi(mockServer.url).getOrder('00000000-0000-0000-0000-000000000000');

        expect(order).toBeNull();
      }));
});
