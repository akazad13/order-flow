import { randomUUID } from 'node:crypto';
import { test as base, createBdd } from 'playwright-bdd';

// Per-scenario data. Fresh IDs keep parallel runs from seeing each other's
// inbox or draining each other's stock (constitution V).
type Scenario = {
  customerId: string;
  sku?: string;
};

export const test = base.extend<{ scenario: Scenario }>({
  scenario: async ({}, use) => {
    await use({ customerId: `e2e-${randomUUID()}` });
  },
});

export const { Given, When, Then } = createBdd(test);

export const inventoryUrl = process.env.INVENTORY_URL ?? 'http://localhost:5102';
export const paymentUrl = process.env.PAYMENT_URL ?? 'http://localhost:5103';
