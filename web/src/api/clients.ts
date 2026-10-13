import createClient from 'openapi-fetch';
import type { components as OrderComponents, paths as OrderPaths } from './order-api';
import type { components as NotificationComponents, paths as NotificationPaths } from './notification-api';

// Types are generated from contracts/http/*.openapi.yaml (npm run gen:api); never edit them by hand.
export type Order = OrderComponents['schemas']['Order'];
export type ValidationProblem = OrderComponents['schemas']['ValidationProblem'];
export type InboxMessage = NotificationComponents['schemas']['InboxMessage'];

const orderApi = createClient<OrderPaths>({ baseUrl: '/order-api' });
const notificationApi = createClient<NotificationPaths>({ baseUrl: '/notification-api' });

export type PlaceOrderResult = { ok: true; order: Order } | { ok: false; problem: ValidationProblem };

export async function placeOrder(customerId: string, sku: string, quantity: number): Promise<PlaceOrderResult> {
  const { data, error } = await orderApi.POST('/orders', {
    body: { customerId, lines: [{ sku, quantity }] },
  });
  if (data) {
    return { ok: true, order: data };
  }
  return { ok: false, problem: error as ValidationProblem };
}

export async function getInbox(customerId: string): Promise<InboxMessage[]> {
  const { data } = await notificationApi.GET('/inbox/{customerId}', {
    params: { path: { customerId } },
  });
  return data ?? [];
}
