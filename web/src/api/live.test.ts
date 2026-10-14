import { HttpTransportType } from '@microsoft/signalr';
import { afterEach, describe, expect, it, vi } from 'vitest';

const withUrl = vi.fn();

vi.mock('@microsoft/signalr', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@microsoft/signalr')>();
  const connection = {
    state: actual.HubConnectionState.Disconnected,
    on: vi.fn(),
    onreconnected: vi.fn(),
    start: vi.fn(() => new Promise(() => undefined)),
    stop: vi.fn(() => Promise.resolve()),
    invoke: vi.fn(),
  };
  class HubConnectionBuilder {
    withUrl(url: string, options: unknown) {
      withUrl(url, options);
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return connection;
    }
  }
  return { ...actual, HubConnectionBuilder };
});

const { watchOrder } = await import('./live');

describe('watchOrder', () => {
  afterEach(() => withUrl.mockReset());

  it('opens the WebSocket directly, so the connection never needs to reach the same copy twice (spec 013, AC-4)', () => {
    const stop = watchOrder('order-1', () => undefined, () => undefined);

    expect(withUrl).toHaveBeenCalledWith(
      '/order-api/hubs/orders',
      expect.objectContaining({ skipNegotiation: true, transport: HttpTransportType.WebSockets }),
    );
    stop();
  });
});
