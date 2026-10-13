import { randomUUID } from 'node:crypto';
import { test as base, createBdd } from 'playwright-bdd';

type Customer = { id: string };

export const test = base.extend<{ customer: Customer }>({
  // A fresh customer per scenario keeps parallel runs from seeing each other's inbox.
  customer: async ({}, use) => {
    await use({ id: `e2e-${randomUUID()}` });
  },
});

export const { Given, When, Then } = createBdd(test);
