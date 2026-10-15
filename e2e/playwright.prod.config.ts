import { defineConfig, devices } from '@playwright/test';

// Spec 019: the production profile's smoke test (e2e/prod), run only against
// deploy/prod/docker-compose.prod.yml. Its certificate comes from a CA made on this machine
// (deploy/prod/make-certs.sh), which this browser and request context are told to accept.
export default defineConfig({
  testDir: './prod',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.PROD_BASE_URL ?? 'https://localhost:5443',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
