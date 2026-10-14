import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import type { Order } from './clients';

/**
 * Live order updates (spec 005). Contract: contracts/realtime/order-updates.asyncapi.yaml.
 *
 * Joins the order's group and calls `onUpdate` with each pushed order. Calls `onWatching`
 * after every (re)join: changes made before joining are not replayed, so the caller reads
 * the order once then (AC-3). Returns a function that stops watching.
 */
export function watchOrder(orderId: string, onUpdate: (order: Order) => void, onWatching: () => void): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/order-api/hubs/orders')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  let stopped = false;

  const join = async () => {
    await connection.invoke('WatchOrder', orderId);
    if (!stopped) onWatching();
  };

  connection.on('OrderUpdated', (order: Order) => {
    if (!stopped) onUpdate(order);
  });
  // Groups belong to a connection: a reconnect is a new connection, so join again.
  connection.onreconnected(() => void join().catch(() => undefined));

  // withAutomaticReconnect only covers a connection that was up once; retry the first start too.
  const START_RETRY_MS = [0, 1000, 2000, 5000];
  const start = async (attempt: number): Promise<void> => {
    if (stopped) return;
    try {
      await connection.start();
      await join();
    } catch {
      if (stopped) return;
      // Connected but the join failed: start over, so the next attempt joins again.
      if (connection.state === HubConnectionState.Connected) await connection.stop().catch(() => undefined);
      const delay = START_RETRY_MS[Math.min(attempt + 1, START_RETRY_MS.length - 1)];
      setTimeout(() => void start(attempt + 1), delay);
    }
  };
  void start(0);

  return () => {
    stopped = true;
    void connection.stop();
  };
}
