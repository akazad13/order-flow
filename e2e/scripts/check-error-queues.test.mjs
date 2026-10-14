// Tests for the no-hidden-faults check (spec 009, AC-6 and AC-8).
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { faultedQueues } from './check-error-queues.mjs';

describe('faultedQueues', () => {
  it('passes when only working queues hold messages', () => {
    assert.deepEqual(faultedQueues([{ name: 'order-order-state', messages: 3 }, { name: 'payment-stock-reserved_error', messages: 0 }]), []);
  });

  it('names error and skipped queues that hold messages', () => {
    const queues = [
      { name: 'payment-stock-reserved_error', messages: 2 },
      { name: 'order-order-state_skipped', messages: 1 },
      { name: 'inventory-order-placed', messages: 5 },
    ];

    assert.deepEqual(faultedQueues(queues), [
      { name: 'payment-stock-reserved_error', messages: 2 },
      { name: 'order-order-state_skipped', messages: 1 },
    ]);
  });

  it('treats a queue with no message count as empty', () => {
    assert.deepEqual(faultedQueues([{ name: 'notification-order-placed_error' }]), []);
  });
});
