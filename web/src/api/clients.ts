import createClient from 'openapi-fetch';
import type { components as OrderComponents, paths as OrderPaths } from './order-api';
import type { components as NotificationComponents, paths as NotificationPaths } from './notification-api';

// Types are generated from contracts/http/*.openapi.yaml (npm run gen:api); never edit them by hand.
export type Order = OrderComponents['schemas']['Order'];
export type ValidationProblem = OrderComponents['schemas']['ValidationProblem'];
export type InboxMessage = NotificationComponents['schemas']['InboxMessage'];

export type PlaceOrderResult = { ok: true; order: Order } | { ok: false; problem: ValidationProblem };

/**
 * The UI's Order API client. A factory so the Pact consumer test (spec 008) can point the
 * exact code the app uses at Pact's mock server.
 */
export function createOrderApi(baseUrl: string) {
  const client = createClient<OrderPaths>({ baseUrl });

  return {
    async placeOrder(customerId: string, sku: string, quantity: number): Promise<PlaceOrderResult> {
      const { data, error } = await client.POST('/orders', {
        body: { customerId, lines: [{ sku, quantity }] },
      });
      if (data) {
        return { ok: true, order: data };
      }
      return { ok: false, problem: error as ValidationProblem };
    },

    async getOrder(orderId: string): Promise<Order | null> {
      const { data } = await client.GET('/orders/{orderId}', {
        params: { path: { orderId } },
      });
      return data ?? null;
    },
  };
}

const orderApi = createOrderApi('/order-api');
const notificationApi = createClient<NotificationPaths>({ baseUrl: '/notification-api' });

export function placeOrder(customerId: string, sku: string, quantity: number): Promise<PlaceOrderResult> {
  return orderApi.placeOrder(customerId, sku, quantity);
}

export function getOrder(orderId: string): Promise<Order | null> {
  return orderApi.getOrder(orderId);
}

export async function getInbox(customerId: string): Promise<InboxMessage[]> {
  const { data } = await notificationApi.GET('/inbox/{customerId}', {
    params: { path: { customerId } },
  });
  return data ?? [];
}
