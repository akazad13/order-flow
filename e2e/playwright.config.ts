import { defineConfig, devices } from '@playwright/test';
import { defineBddConfig } from 'playwright-bdd';

// Gherkin lives with the specs (features/e2e); steps live here.
const testDir = defineBddConfig({
  featuresRoot: '../features/e2e',
  features: '../features/e2e/**/*.feature',
  steps: ['steps/**/*.ts'],
});

export default defineConfig({
  testDir,
  // One real login before the suite: right after a start, the first ones are slow (spec 023).
  globalSetup: './scripts/warm-up.ts',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  // No retries: a flaky test is a bug to fix, not to hide (proposal: 20 green runs in a row).
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:5180',
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
