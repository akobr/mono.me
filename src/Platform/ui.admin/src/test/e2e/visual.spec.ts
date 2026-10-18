import { expect, test, type Page } from '@playwright/test'
import { goto, openApp, type MockScenario } from './support'

// Key screens in both themes and both viewports (spec section 18), compared with a 0.2 % tolerance. Fonts render
// differently per platform, so the baselines are made and compared in the Playwright Linux container only:
//   npm run test:visual:docker              compare
//   npm run test:visual:docker -- --update  write new baselines
/** Moves an annotation to Deleted through the mock API, then shows `path` again. */
const deleted = (key: string) => async (page: Page, path: string) => {
  await page.waitForFunction(() => !!navigator.serviceWorker?.controller)
  await page.evaluate(
    (target) => fetch(`/api/v1/acme/billing/default/annotations/${target}`, { method: 'DELETE' }),
    key,
  )
  await goto(page, path)
}

const screens: {
  name: string
  path: string
  scenario?: MockScenario
  prepare?: (page: Page, path: string) => Promise<void>
}[] = [
  { name: 'organizations', path: '/orgs' },
  { name: 'annotations', path: '/acme/billing/default/annotations' },
  { name: 'annotation-detail', path: '/acme/billing/default/annotations/exe.northwind.invoicing.prod' },
  {
    name: 'annotation-history',
    path: '/acme/billing/default/annotations/exe.northwind.invoicing.prod/history',
  },
  { name: 'configurations', path: '/acme/billing/default/configurations' },
  { name: 'deleted', path: '/acme/billing/default/modeling/deleted', prepare: deleted('sbt.contoso') },
  { name: 'template', path: '/acme/billing/default/modeling/templates/exe' },
  { name: 'schemas', path: '/acme/billing/default/modeling/schemas' },
  { name: 'export-import', path: '/acme/billing/default/modeling/export-import' },
  { name: 'members', path: '/acme/billing/members' },
  { name: 'machines', path: '/acme/billing/machines' },
  { name: 'key-values', path: '/acme/billing/default/key-values' },
  { name: 'settings', path: '/acme/billing/settings#secrets' },
  { name: 'monitor', path: '/acme/billing/monitor' },
  { name: 'monitor-audit', path: '/acme/billing/monitor?tab=audit' },
  { name: 'onboarding', path: '/onboarding', scenario: 'new-user' },
]

const viewports = {
  desktop: { viewport: { width: 1440, height: 900 } },
  mobile: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true },
} as const

for (const [device, options] of Object.entries(viewports)) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test.describe(`${device} ${colorScheme}`, () => {
      test.use({ ...options, colorScheme, deviceScaleFactor: 1, reducedMotion: 'reduce' })

      for (const screen of screens) {
        test(screen.name, async ({ page }) => {
          // The mock data is relative to "now": a fixed clock keeps dates and expiries stable.
          await page.clock.setFixedTime(new Date('2026-10-10T08:00:00Z'))
          await openApp(page, screen.path, screen.scenario)
          await screen.prepare?.(page, screen.path)
          await page.evaluate(() => document.fonts.ready)
          await expect(page).toHaveScreenshot(`${screen.name}-${device}-${colorScheme}.png`)
        })
      }
    })
  }
}
