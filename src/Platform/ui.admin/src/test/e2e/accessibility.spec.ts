import { expect, test } from '@playwright/test'
import { expectAccessible, openApp, openBulkCreate, signedOut } from './support'

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

test('the bulk create dialog has no serious accessibility violations', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')
  const dialog = await openBulkCreate(page, testInfo)
  // New, Exists and Duplicate rows, and an invalid line with its message
  await dialog
    .getByRole('textbox', { name: 'Keys' })
    .fill('northwind.billing\nexe.cyberdyne.invoicing.prod\nrst.invoicing\nusg.northwind.billing\nexe.a.b')
  await expect(dialog.getByRole('button', { name: 'Remove invalid lines' })).toBeVisible()
  await expect(dialog.getByRole('region', { name: 'Review' }).getByRole('row')).toHaveCount(6)
  await expectAccessible(page)
})

test('onboarding has no serious accessibility violations', async ({ page }) => {
  await openApp(page, '/onboarding', 'new-user')
  await expectAccessible(page)
})

test('sign-in has no serious accessibility violations', async ({ page }) => {
  await signedOut(page)
  await openApp(page, '/login')
  await expectAccessible(page)
})

for (const [name, button] of [
  ['export', 'Export configurations'],
  ['import', 'Import configurations'],
] as const) {
  test(`the ${name} dialog has no serious accessibility violations`, async ({ page }) => {
    await openApp(page, '/acme/billing/default/configurations')
    await page.getByRole('button', { name: button }).click()
    await page.getByRole('dialog').waitFor()
    await expectAccessible(page)
  })
}
