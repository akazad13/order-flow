import { afterEach, describe, expect, it, vi } from 'vitest';
import { onLoginChanged } from '../auth';
import { createOrderApi } from './clients';

// The client captures fetch when it is created: stub it first, then create one (with an absolute
// address, as there is no page to resolve a relative one against). Every API client uses the same
// gateway middleware (spec 023).
function orderApiAnswering(response: () => Response) {
  const fetch = vi.fn(async (_request: Request) => response());
  vi.stubGlobal('fetch', fetch);
  return { fetch, api: createOrderApi('http://shop.test/order-api') };
}

describe('calls through the gateway (spec 023)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('carry the X-CSRF header and no token (AC-1, AC-5)', async () => {
    const { fetch, api } = orderApiAnswering(() => new Response('{}', { status: 404 }));

    await api.getOrder('8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d');

    const request = fetch.mock.calls[0][0];
    expect(request.headers.get('X-CSRF')).toBe('1');
    expect(request.headers.get('Authorization')).toBeNull();
  });

  it('end the login when the gateway answers 401 (AC-4)', async () => {
    const { api } = orderApiAnswering(() => new Response(null, { status: 401 }));
    const listener = vi.fn();
    const unsubscribe = onLoginChanged(listener);

    await api.getOrder('8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d');

    expect(listener).toHaveBeenCalledWith(null);
    unsubscribe();
  });
});
