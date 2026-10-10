import { defineConfig, devices } from '@playwright/test'

// End-to-end and visual suites (spec section 18) against the e2e build: the API is served by MSW in the browser
// and a fake user is signed in, so no backend is needed.
//   npm run test:e2e      flows and accessibility, desktop (1440×900) and mobile (390×844)
//   npm run test:visual   screenshots; baselines are made in the Playwright Linux container (see README)
const port = 4174
const ci = !!process.env.CI
// PLAYWRIGHT_CHANNEL=chrome runs the suites in the installed Chrome instead of Playwright's Chromium download.
const channel = process.env.PLAYWRIGHT_CHANNEL || undefined

export default defineConfig({
  testDir: './test/e2e',
  fullyParallel: true,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  workers: ci ? 2 : undefined,
  reporter: ci ? [['github'], ['html', { open: 'never' }]] : [['list']],
  // One baseline per screenshot name for every platform: they are only compared in the Linux container.
  snapshotPathTemplate: '{testDir}/__screenshots__/{arg}{ext}',
  expect: {
    timeout: 10_000,
    toHaveScreenshot: { maxDiffPixelRatio: 0.002, animations: 'disabled', caret: 'hide' },
  },
  use: {
    baseURL: `http://localhost:${port}`,
    locale: 'en-US',
    timezoneId: 'Europe/Prague',
    trace: 'retain-on-failure',
    channel,
  },
  projects: [
    {
      name: 'desktop',
      testIgnore: /visual\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 }, channel },
    },
    {
      name: 'mobile',
      testIgnore: /visual\.spec\.ts/,
      use: { ...devices['Pixel 7'], viewport: { width: 390, height: 844 }, channel },
    },
    {
      name: 'visual',
      testMatch: /visual\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], channel },
    },
  ],
  webServer: {
    command: `npm run build:e2e && npx vite preview --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: !ci,
    timeout: 240_000,
  },
})
