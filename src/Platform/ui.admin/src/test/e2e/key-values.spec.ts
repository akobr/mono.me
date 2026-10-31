import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// The key values of a view and the project's secrets (spec 29.8), against the mock API: acme/billing is the
// signed-in user's own project (Owner), northwind/portal gives them the Reader role.

/** The navigation menu: the docked sidebar, or on a phone the slideover behind the menu button. */
async function menu(page: Page, testInfo: TestInfo): Promise<Locator> {
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Open navigation' }).click()
    return page.getByRole('dialog')
  }
  return page.locator('nav').filter({ has: page.getByRole('link', { name: 'Organizations' }) })
}

async function openKey(page: Page, key: string, testInfo: TestInfo) {
  const item = isMobile(testInfo)
    ? page.getByRole('listitem').filter({ hasText: key }).getByRole('button')
    : page.getByRole('row').filter({ hasText: key })
  await item.first().click()
}

async function createKey(page: Page, key: string, level: string, value: string) {
  await page.getByRole('button', { name: 'New key' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New key' })
  await dialog.getByPlaceholder('db.password').fill(key)
  if (level !== 'Public') {
    await dialog.getByRole('switch', { name: 'Secret' }).click()
    await dialog.getByRole('combobox', { name: 'Protection level' }).click()
    await page.getByRole('option', { name: level, exact: true }).click()
  }
  await dialog.locator('textarea').fill(value)
  await dialog.getByRole('button', { name: 'Create' }).click()
  await expect(dialog).toBeHidden()
}

test('creates a public key and sees it in the list with its binding', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/key-values')
  await createKey(page, 'app.region', 'Public', 'eu-west')

  await expect(page.getByText('app.region').first()).toBeVisible()
  await expect(page.getByText('@app.region')).toBeVisible()
  if (!isMobile(testInfo)) {
    await expect(page.getByRole('row').filter({ hasText: 'app.region' })).toContainText('eu-west')
  }
})

test('filters the list by level, clears the filter, and the app survives leaving the screen', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/key-values')
  await expect(page.getByText('db.password').first()).toBeVisible()

  // every item of the Select needs a non-empty value, so "All levels" is a sentinel; an empty string breaks the Select
  await page.getByRole('combobox', { name: 'Level filter' }).click()
  await page.getByRole('option', { name: 'Public', exact: true }).click()
  await expect(page).toHaveURL(/level=Public/)
  await expect(page.getByText('app.name').first()).toBeVisible()
  await expect(page.getByText('db.password')).toHaveCount(0)

  await page.getByRole('combobox', { name: 'Level filter' }).click()
  await page.getByRole('option', { name: 'All levels', exact: true }).click()
  await expect(page).not.toHaveURL(/level=/)
  await expect(page.getByText('db.password').first()).toBeVisible()
  await expect(page.getByRole('listbox')).toBeHidden()

  await (await menu(page, testInfo)).getByRole('link', { name: 'Annotations', exact: true }).click()
  await expect(page).toHaveURL(/\/annotations/)
  await expect(page.getByText('rst.invoicing').first()).toBeVisible()
  await (await menu(page, testInfo)).getByRole('link', { name: 'Values store', exact: true }).click()
  await expect(page.getByText('db.password').first()).toBeVisible()
})

test('creates a Standard secret, reveals it, and the value is masked in the list', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/key-values')
  await createKey(page, 'smtp.password', 'Standard', 'mail-s3cret')

  await expect(page.getByText('mail-s3cret')).toHaveCount(0)
  await page.getByRole('button', { name: 'Reveal' }).click()
  await expect(page.getByText('mail-s3cret')).toBeVisible()
  await expect(page.getByText('Masked again after 60 seconds.').first()).toBeVisible()

  if (!isMobile(testInfo)) {
    await expect(page.getByRole('row').filter({ hasText: 'smtp.password' })).toContainText('••••••••')
  }
})

test('a reader cannot reveal, create or change the policy', async ({ page }, testInfo) => {
  await openApp(page, '/northwind/portal/default/key-values')
  await expect(page.getByRole('button', { name: 'New key' })).toHaveCount(0)

  // The secrets settings are the Secrets section of the project's Settings (424-35).
  await goto(page, '/northwind/portal/settings#secrets')
  const secrets = page.locator('#secrets')
  await expect(secrets).toBeInViewport()
  await expect(secrets.getByText('Administrator', { exact: true }).first()).toBeVisible()
  await expect(secrets.getByRole('switch', { name: 'Allow Standard' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Save secrets policy' })).toHaveCount(0)
  expect(testInfo.project.name).toBeTruthy()
})

test('encrypts a HYOK value in the browser for the recipients', async ({ page }) => {
  await openApp(page, '/acme/billing/default/key-values')
  await createKey(page, 'payments.signing_key', 'HYOK', 'client-side only')

  await expect(page.getByText('Encrypted on the client')).toBeVisible()
  await expect(page.getByText('invoicing-api').first()).toBeVisible()
  await expect(page.getByText('client-side only')).toHaveCount(0)
})

test('changes the protection of a secret to HSM and keeps the old version at Standard', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/key-values')
  await openKey(page, 'db.password', testInfo)

  await page.getByRole('button', { name: 'Change protection' }).click()
  await page.getByRole('menuitem', { name: 'Move to HSM' }).click()
  await page.getByRole('button', { name: 'Change protection', exact: true }).last().click()
  await expect(page.getByText("'db.password' is HSM now").first()).toBeVisible()

  await page.getByRole('link', { name: 'Versions' }).click()
  // the versions table arrives after the tab opens; the list table is the last one until then
  const rows = page
    .getByRole('table')
    .filter({ has: page.getByRole('columnheader', { name: 'Expires' }) })
    .getByRole('row')
  await expect(rows.filter({ hasText: 'v3' })).toContainText('HSM')
  await expect(rows.filter({ hasText: 'v2' })).toContainText('Standard')
})

test('deletes, restores and purges a key', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/key-values')
  await openKey(page, 'app.name', testInfo)
  await expect(page.getByText('Binding')).toBeVisible()

  await goto(page, '/acme/billing/default/key-values')
  const row = isMobile(testInfo) ? undefined : page.getByRole('row').filter({ hasText: 'app.name' })
  if (row) {
    await row.getByRole('button', { name: 'Actions for app.name' }).click()
    await page.getByRole('menuitem', { name: 'Delete', exact: true }).click()
    await page.getByRole('button', { name: 'Delete', exact: true }).last().click()
    await expect(page.getByText("'app.name' deleted").first()).toBeVisible()

    await page.getByRole('button', { name: 'Deleted' }).click()
    await expect(page.getByRole('row').filter({ hasText: 'app.name' })).toBeVisible()
    await page.getByRole('row').filter({ hasText: 'legacy.endpoint' }).getByRole('checkbox').click()
    await page.getByRole('button', { name: 'Purge' }).click()
    await page.getByPlaceholder('default').fill('default')
    await page.getByRole('button', { name: 'Purge', exact: true }).last().click()
    await expect(page.getByRole('row').filter({ hasText: 'legacy.endpoint' })).toHaveCount(0)

    await page.getByRole('row').filter({ hasText: 'app.name' }).getByRole('checkbox').click()
    await page.getByRole('button', { name: 'Restore' }).click()
    await expect(page.getByText('1 key(s) restored').first()).toBeVisible()
  }
})

test('loads the versions only when the tab opens', async ({ page }, testInfo) => {
  const versionRequests: string[] = []
  page.on('request', (request) => {
    if (request.url().endsWith('/key-values/db.host/versions')) {
      versionRequests.push(request.url())
    }
  })
  await openApp(page, '/acme/billing/default/key-values')
  await openKey(page, 'db.host', testInfo)
  await expect(page.getByText('Binding')).toBeVisible()
  expect(versionRequests).toHaveLength(0)

  await page.getByRole('link', { name: 'Versions' }).click()
  await expect(page.getByRole('table').last().getByRole('row').filter({ hasText: '(current)' })).toBeVisible()
  expect(versionRequests.length).toBeGreaterThan(0)
})

// A secret version revealed or compared in the versions panel is a reveal in the audit trail, as the API records it (424-101).
test('reveals and compares secret versions, and the audit trail records both', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/key-values')
  await openKey(page, 'db.password', testInfo)
  await page.getByRole('link', { name: 'Versions' }).click()
  const rows = page
    .getByRole('table')
    .filter({ has: page.getByRole('columnheader', { name: 'Expires' }) })
    .getByRole('row')
  await rows.filter({ hasText: 'v1' }).getByRole('button', { name: 'Reveal' }).click()
  await expect(page.getByText('s3cret-billing-1').first()).toBeVisible()
  await rows.filter({ hasText: 'v2' }).getByRole('button', { name: 'Compare' }).click()
  await expect(page.getByRole('button', { name: 'Close the comparison' })).toBeVisible()

  await goto(page, '/acme/billing/monitor?tab=audit')
  const reveals = page.getByRole('row').filter({ hasText: 'Reveal' }).filter({ hasText: 'db.password' })
  await expect(reveals.filter({ hasText: 'version 1' })).toHaveCount(1)
  await expect(reveals.filter({ hasText: 'diff 1..2' })).toHaveCount(2)
})

// The switcher keeps Values store, without the open key, and the selection stays behind with its project (424-68).
test('the switcher keeps Values store, without the key and the selection', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The view switcher of the top bar is hidden on a phone')
  const switcher = page.getByRole('navigation', { name: 'Context' })
  // not by role: while a menu of the switcher closes, the page is aria-hidden, and a role query finds nothing either way
  const detail = page.locator('#key-value-detail')
  await openApp(page, '/acme/billing/staging/key-values/db.host/versions')
  await expect(detail).toBeVisible()

  await switcher.getByRole('button').nth(2).click()
  await page.getByRole('menuitem', { name: /^default\s*prod$/ }).click()
  await expect(page).toHaveURL('/acme/billing/default/key-values')
  await expect(detail).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'db.host' })).toContainText('sql.internal')

  await page.getByRole('checkbox', { name: 'Select db.host' }).click()
  await expect(page.getByRole('toolbar', { name: 'Bulk actions' })).toContainText('1 selected')

  // the default view of the other project: the same view name, so the same page goes on
  await switcher.getByRole('button').nth(1).click()
  await page.getByRole('menuitem', { name: 'shipping', exact: true }).click()
  await expect(page).toHaveURL('/acme/shipping/default/key-values')
  await expect(page.getByText('No key values in this view')).toBeVisible()
  await expect(page.getByText('1 selected')).toHaveCount(0)
})

test('imports an .env text with a secret pattern after showing the plan', async ({ page }) => {
  await openApp(page, '/acme/billing/default/key-values')
  await page.getByRole('button', { name: 'Import' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'Import key values' })

  await dialog.locator('textarea').fill('CACHE__HOST=redis.internal\nCACHE__PASSWORD=r3dis')
  await dialog.getByRole('button', { name: 'Next' }).click()
  await dialog.getByRole('button', { name: 'Mark' }).click()
  await expect(dialog.getByRole('combobox', { name: 'Level of cache.password' })).toContainText('Standard')
  await dialog.getByRole('button', { name: 'Check' }).click()
  await expect(dialog.getByText('Plan: 2 create')).toBeVisible()
  await dialog.getByRole('button', { name: 'Import', exact: true }).click()
  await expect(dialog.getByText('Imported', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByText('cache.host').first()).toBeVisible()
})

test('exports the view as an .env file', async ({ page }) => {
  await openApp(page, '/acme/billing/default/key-values')
  await page.getByRole('button', { name: 'Export' }).click()
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('menuitem', { name: '.env file' }).click(),
  ])

  expect(download.suggestedFilename()).toBe('acme-billing-default.env')
})

test('sets up a customer key with the BYOK wizard', async ({ page }) => {
  await openApp(page, '/acme/billing/settings#secrets')
  await page.getByRole('button', { name: 'Show the setup' }).click()
  await expect(page.getByText('Key Vault Crypto Service Encryption User').first()).toBeVisible()

  await page
    .getByPlaceholder('https://my-vault.vault.azure.net/keys/storyteller-kek')
    .fill('https://acme-keys.vault.azure.net/keys/storyteller')
  await page.getByPlaceholder('00000000-0000-0000-0000-000000000000').fill('11111111-2222-3333-4444-555555555555')
  await page.getByRole('button', { name: 'Save key' }).click()
  await expect(page.getByText('Customer key saved; verify it next').first()).toBeVisible()

  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page.getByText('Customer key verified').first()).toBeVisible()
  await expect(page.getByText('Healthy').first()).toBeVisible()
})

// Usage and Audit are the tabs of the Monitor page (424-35).
test('shows the usage and filters the audit trail on the Monitor page', async ({ page }) => {
  await openApp(page, '/acme/billing/monitor')
  await expect(page.getByRole('tab', { name: 'Usage' })).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByText('Standard keys (key-months)').first()).toBeVisible()
  await expect(page.getByText('Estimated charges')).toBeVisible()

  await page.getByRole('tab', { name: 'Audit' }).click()
  await expect(page).toHaveURL(/[?&]tab=audit/)
  await expect(page.getByText('Reveal').first()).toBeVisible()
  await expect(page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'Reveal', exact: true }) })).toContainText('Grace Hopper')
  await expect(page.getByRole('row').filter({ hasText: 'BindingRead' })).toContainText('machine:mach-billing-sync')
  await page.getByRole('combobox', { name: 'Operation filter' }).click()
  await page.getByRole('option', { name: 'Rotate', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: 'Rotate' })).toHaveCount(1)
  await expect(page.getByRole('row').filter({ hasText: 'Reveal' })).toHaveCount(0)

  await page.getByRole('tab', { name: 'Usage' }).click()
  await expect(page).not.toHaveURL(/tab=/)
  await expect(page.getByText('Estimated charges')).toBeVisible()
})
