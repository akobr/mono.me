import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// The access audit of an organization (424-263): its Owner opens it from the menu, reads the seeded trail of acme
// and filters it; a change made on another page is its newest record; below Administrator nothing is asked for.

/** The navigation menu: the docked sidebar, or on a phone the slideover behind the menu button. */
async function openMenu(page: Page, testInfo: TestInfo) {
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Open navigation' }).click()
    return page.getByRole('dialog')
  }
  return page.locator('nav').filter({ has: page.getByRole('link', { name: 'Organizations' }) })
}

const rows = (page: Page) => page.getByRole('row').filter({ has: page.getByRole('cell') })

test('the Owner opens the access audit of the organization and filters it by an action', async ({ page }, testInfo) => {
  await openApp(page, '/acme')
  await (await openMenu(page, testInfo)).getByRole('link', { name: 'Audit', exact: true }).click()
  await expect(page).toHaveURL('/acme/audit')

  // the seeded records of acme and its projects, newest first
  await expect(rows(page).first()).toContainText('Federation rules changed')
  await expect(rows(page).first()).toContainText('mach-release-pipeline')
  await expect(rows(page).filter({ hasText: 'Member removed' })).toContainText('Charles Babbage')
  await expect(rows(page).filter({ hasText: 'Member removed' })).toContainText('Organization')
  await expect(rows(page).filter({ hasText: 'Role changed' })).toContainText('Ada Lovelace via mcp')
  await expect(rows(page).filter({ hasText: 'Machine credential reset' })).toContainText('The API key was replaced.')
  await expect(page.getByText(/^\d+ record\(s\)$/)).toBeVisible()

  await page.getByRole('combobox', { name: 'Action filter' }).click()
  await page.getByRole('option', { name: 'Role changed', exact: true }).click()

  await expect(rows(page)).toHaveCount(1)
  await expect(rows(page).first()).toContainText('Alan Turing')
  await expect(rows(page).first()).toContainText('billing')
  await expect(page.getByText('1 record(s)')).toBeVisible()

  await page.getByRole('textbox', { name: 'Target filter' }).fill('user_01MOCKGRACE')
  await expect(page.getByText('No matching records')).toBeVisible()
  await expect(page.getByText('kept for two years')).toBeVisible()
})

test('a changed description is the newest record, with its full values on demand', async ({ page }) => {
  await openApp(page, '/acme/billing/settings')
  await page.getByRole('textbox', { name: 'Description', exact: true }).fill('Invoices and payments of Acme.')
  await page.getByRole('button', { name: 'Save description' }).click()
  await expect(page.getByText('Description saved', { exact: true })).toBeVisible()

  await goto(page, '/acme/audit')
  const newest = rows(page).first()
  await expect(newest).toContainText('Project description changed')
  await expect(newest).toContainText('Ada Lovelace')
  await expect(newest.locator('code').last()).toHaveText('Invoices and payments of Acme.')

  await newest.getByRole('button', { name: 'Show the full values' }).click()
  const values = page.getByRole('dialog', { name: 'Show the full values' })
  await expect(values.getByRole('definition').first()).toHaveText(
    "Billing of Acme's B2B customers. Invoicing, payments, reporting, notifications, archiving and dunning are set up per customer and context.",
  )
  await expect(values.getByRole('definition').last()).toHaveText('Invoices and payments of Acme.')
})

test('below Administrator of the organization the page asks for nothing, and the menu has no Audit', async ({
  page,
}, testInfo) => {
  const requests: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (path.startsWith('/api/') && path.endsWith('/audit')) {
      requests.push(request.url())
    }
  })
  // Ada reads northwind/portal and has no role on northwind itself
  await openApp(page, '/northwind/audit')

  await expect(page.getByRole('heading', { name: 'Administrators only' })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  const menu = await openMenu(page, testInfo)
  await expect(menu.getByRole('link', { name: 'Overview', exact: true })).toBeVisible()
  await expect(menu.getByRole('link', { name: 'Audit', exact: true })).toHaveCount(0)
  expect(requests).toEqual([])
})
