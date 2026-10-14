import createClient, { type Middleware } from 'openapi-fetch';
import { accessToken } from '../auth';
import type { components as OrderComponents, paths as OrderPaths } from './order-api';
import type { components as NotificationComponents, paths as NotificationPaths } from './notification-api';
import type { components as InventoryComponents, paths as InventoryPaths } from './inventory-api';

// Types are generated from contracts/http/*.openapi.yaml (npm run gen:api); never edit them by hand.
export type Order = OrderComponents['schemas']['Order'];
export type ValidationProblem = OrderComponents['schemas']['ValidationProblem'];
export type InboxMessage = NotificationComponents['schemas']['InboxMessage'];
export type StockItem = InventoryComponents['schemas']['StockItem'];
export type SetStock = InventoryComponents['schemas']['SetStockRequest'];

/** Spec 017: the item as stored, or an error per field. */
export type SetStockResult = { ok: true; item: StockItem } | { ok: false; errors: string[] };

export type PlaceOrderResult = { ok: true; order: Order } | { ok: false; problem: ValidationProblem };

/**
 * Spec 006: accepted (the outcome is pushed, spec 005), already cancelled (nothing changed),
 * too late (the order is confirmed), or no such order.
 */
export type CancelOrderResult =
  | { outcome: 'accepted' }
  | { outcome: 'already-cancelled'; order: Order }
  | { outcome: 'too-late' }
  | { outcome: 'not-found' };

/** Sends the logged-in customer's access token (spec 012). */
function bearer(token: () => string | null): Middleware {
  return {
    onRequest({ request }) {
      const value = token();
      if (value) request.headers.set('Authorization', `Bearer ${value}`);
      return request;
    },
  };
}

/**
 * The UI's Order API client. A factory so the Pact consumer test (spec 008) can point the
 * exact code the app uses at Pact's mock server, with a token of its own.
 */
export function createOrderApi(baseUrl: string, token: () => string | null = accessToken) {
  const client = createClient<OrderPaths>({ baseUrl });
  client.use(bearer(token));

  return {
    async placeOrder(sku: string, quantity: number): Promise<PlaceOrderResult> {
      // The customer is the logged-in one (spec 012, R-1).
      const { data, error } = await client.POST('/orders', {
        body: { lines: [{ sku, quantity }] },
      });
      if (data) {
        return { ok: true, order: data };
      }
      return { ok: false, problem: error as ValidationProblem };
    },

    async cancelOrder(orderId: string): Promise<CancelOrderResult> {
      const { data, response } = await client.POST('/orders/{orderId}/cancellation', {
        params: { path: { orderId } },
      });
      switch (response.status) {
        case 202:
          return { outcome: 'accepted' };
        case 200:
          return { outcome: 'already-cancelled', order: data! };
        case 409:
          return { outcome: 'too-late' };
        default:
          return { outcome: 'not-found' };
      }
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
notificationApi.use(bearer(accessToken));

export function placeOrder(sku: string, quantity: number): Promise<PlaceOrderResult> {
  return orderApi.placeOrder(sku, quantity);
}

export function cancelOrder(orderId: string): Promise<CancelOrderResult> {
  return orderApi.cancelOrder(orderId);
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

const inventoryApi = createClient<InventoryPaths>({ baseUrl: '/inventory-api' });
inventoryApi.use(bearer(accessToken));

/** The catalog, open to everyone (spec 017, AC-3). */
export async function listStock(): Promise<StockItem[]> {
  const { data } = await inventoryApi.GET('/stock');
  return data ?? [];
}

/** Admins only (spec 017): sets an item's name, price and quantity available, adding it if new. */
export async function setStock(sku: string, item: SetStock): Promise<SetStockResult> {
  const { data, error, response } = await inventoryApi.PUT('/stock/{sku}', { params: { path: { sku } }, body: item });
  if (data) return { ok: true, item: data };
  if (response.status === 400) {
    const problem = error as { errors?: Record<string, string[]> } | undefined;
    return { ok: false, errors: Object.values(problem?.errors ?? {}).flat() };
  }
  return { ok: false, errors: [response.status === 403 ? 'Only admins can set stock.' : `Could not set stock (${response.status}).`] };
}
