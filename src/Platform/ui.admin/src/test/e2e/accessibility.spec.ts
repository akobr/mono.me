import { test } from '@playwright/test'
import { expectAccessible, openApp, signedOut } from './support'

// Accessibility of the main screens (spec section 15): no serious or critical axe violations (WCAG 2.1 AA).
const screens: [string, string][] = [
  ['organizations', '/orgs'],
  ['annotations', '/acme/billing/default/annotations'],
  ['annotation detail', '/acme/billing/default/annotations/exe.northwind.invoicing.prod'],
  ['configuration', '/acme/billing/default/configurations/exe.northwind.invoicing.prod'],
  ['templates', '/acme/billing/default/templates/exe'],
  ['schemas', '/acme/billing/default/schemas'],
  ['graph', '/acme/billing/default/graph'],
  ['members', '/acme/billing/members'],
  ['machines', '/acme/billing/machines'],
  ['settings', '/acme/billing/settings'],
  ['key values', '/acme/billing/default/key-values'],
  ['key value detail', '/acme/billing/default/key-values/db.password'],
  ['secrets settings', '/acme/billing/secrets'],
  ['audit', '/acme/billing/audit'],
  ['usage', '/acme/billing/usage'],
  ['account', '/account'],
]

for (const [name, path] of screens) {
  test(`${name} has no serious accessibility violations`, async ({ page }) => {
    await openApp(page, path)
    await expectAccessible(page)
  })
}

test('onboarding has no serious accessibility violations', async ({ page }) => {
  await openApp(page, '/onboarding', 'new-user')
  await expectAccessible(page)
})

test('sign-in has no serious accessibility violations', async ({ page }) => {
  await signedOut(page)
  await openApp(page, '/login')
  await expectAccessible(page)
})
