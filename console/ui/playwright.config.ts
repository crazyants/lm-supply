import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',

  // Serialize tests — AI inference is GPU-bound
  fullyParallel: false,
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  reporter: process.env.CI
    ? [['html', { outputFolder: 'playwright-report' }]]
    : [['html', { open: 'on-failure' }], ['line']],

  use: {
    // Test against Vite dev server (proxies API calls to backend at :5000)
    baseURL: 'http://localhost:3000',
    screenshot: 'only-on-failure',
    trace: 'on-first-retry',
    headless: true,
    actionTimeout: 60_000,
    navigationTimeout: 30_000,
  },

  timeout: 120_000,
  expect: {
    timeout: 30_000,
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
