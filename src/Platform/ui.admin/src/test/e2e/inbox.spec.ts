import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { createZip, textFile } from '../../app/mock/zip'
import { goto, isMobile, openApp } from './support'

// The Inbox page of a view (424-31) against the mock API: the results of background jobs, the actions the platform
// suggests and its messages, open or done. The mock seeds five items into the default view of every project: for
// Contributors an action to accept, a message and an accepted action, for Administrators a failed export and a
// dismissed import. The mock user owns acme/billing, contributes to acme/shipping and reads northwind/portal.
// No step waits for the re-read of the page every 20 seconds: each waits for what its own action reads again.
const path = '/acme/billing/default/inbox'

// The seeded items, by their titles.
const suggestion = 'Generate a schema for 4 similar configurations'
const failedExport = 'Export failed'
const note = 'Schema suggestions are on for this view'
const finishedImport = 'Import finished'
const accepted = 'Generate a schema for 7 similar configurations'

/** The navigation menu: the docked sidebar, or on a phone the slideover behind the menu button. */
async function menu(page: Page, testInfo: TestInfo): Promise<Locator> {
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Open navigation' }).click()
    return page.getByRole('dialog')
  }
  return page.locator('nav').filter({ has: page.getByRole('link', { name: 'Organizations' }) })
}

/** The items of the shown list, newest first. */
const items = (page: Page, list: 'Open' | 'Done' = 'Open') =>
  page.getByRole('list', { name: `${list} items`, exact: true }).getByRole('listitem')

const item = (page: Page, title: string, list: 'Open' | 'Done' = 'Open') =>
  items(page, list).filter({ hasText: title })

/** A tab of the page; Open carries the count of the open items after its name. */
const tab = (page: Page, name: 'Open' | 'Done') => page.getByRole('tab', { name: new RegExp(`^${name}`) })

test('opens the page from the menu and with g i, and marks the entry while the view has open items', async ({
  page,
}, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')

  const navigation = await menu(page, testInfo)
  const entry = navigation.getByRole('link', { name: 'Inbox', exact: true })
  // a dot on the icon, and the same in words: the link keeps its name
  await expect(entry.locator('[data-slot="linkLeadingChip"]')).toBeVisible()
  await expect(entry).toHaveAccessibleDescription('Open items')
  await expect(entry.locator('[data-slot="linkTrailingBadge"]')).toHaveCount(0)

  await entry.click()
  await expect(page).toHaveURL(path)
  await expect(tab(page, 'Open')).toHaveAttribute('aria-selected', 'true')
  await expect(tab(page, 'Open')).toHaveAccessibleName(/^Open\s*3$/)
  await expect(items(page)).toHaveCount(3)
  await expect(items(page).nth(0)).toContainText(suggestion)
  await expect(items(page).nth(1)).toContainText(failedExport)
  await expect(items(page).nth(2)).toContainText(note)
  // the failed job of another Administrator: for Administrators only, with its error and nothing to open
  await expect(item(page, failedExport)).toContainText('Administrators only')
  await expect(item(page, failedExport)).toContainText('by Grace Hopper')
  await expect(item(page, failedExport).getByRole('button')).toHaveText(['Dismiss'])

  await goto(page, '/acme/billing/default/graph')
  await page.keyboard.press('g')
  await page.keyboard.press('i')
  await expect(page).toHaveURL(path)
  await expect(items(page)).toHaveCount(3)
})

test('lists the page among the pages of the command palette', async ({ page }) => {
  await openApp(page, '/acme/billing/default/annotations')

  await page.getByRole('button', { name: 'Search…' }).click()
  await page.getByPlaceholder('Search annotations, pages, projects and actions…').fill('Inbox')
  await page.getByRole('option', { name: /^Inbox/ }).click()

  await expect(page).toHaveURL(path)
  await expect(items(page)).toHaveCount(3)
})

test('accepts the suggested action after a confirmation: its result arrives, and the action is under Done', async ({
  page,
}) => {
  await openApp(page, path)

  await item(page, suggestion).getByRole('button', { name: 'Accept', exact: true }).click()
  const confirmation = page.getByRole('dialog', { name: 'Accept this action?' })
  await expect(confirmation).toContainText(suggestion)
  await expect(confirmation).toContainText('runs in the background')
  await confirmation.getByRole('button', { name: 'Accept', exact: true }).click()

  // the mock runs the action at once: the notification of its result is in the list the acceptance reads again
  await expect(items(page).nth(0)).toContainText('Schema generation finished')
  await expect(item(page, suggestion)).toHaveCount(0)
  await expect(tab(page, 'Open')).toHaveAccessibleName(/^Open\s*3$/)

  await tab(page, 'Done').click()
  await expect(page).toHaveURL(/[?&]tab=done\b/)
  await expect(items(page, 'Done')).toHaveCount(3)
  await expect(item(page, suggestion, 'Done')).toContainText('Accepted by Ada Lovelace')
  await expect(item(page, suggestion, 'Done').getByRole('button')).toHaveCount(0)
  await expect(item(page, accepted, 'Done')).toContainText('Accepted by Ada Lovelace')
})

test('dismisses an item, which is then under Done', async ({ page }) => {
  await openApp(page, path)

  await item(page, note).getByRole('button', { name: 'Dismiss', exact: true }).click()

  await expect(item(page, note)).toHaveCount(0)
  await expect(items(page)).toHaveCount(2)
  await expect(tab(page, 'Open')).toHaveAccessibleName(/^Open\s*2$/)

  await tab(page, 'Done').click()
  await expect(item(page, note, 'Done')).toContainText('Dismissed by Ada Lovelace')
  // the seeded import that somebody dismissed names no job, so it has nothing to show
  await expect(item(page, finishedImport, 'Done')).toContainText('Dismissed by Grace Hopper')
  await expect(item(page, finishedImport, 'Done').getByRole('button')).toHaveCount(0)

  // the page comes back on the tab of its address
  await page.reload()
  await page.waitForLoadState('networkidle')
  await expect(tab(page, 'Done')).toHaveAttribute('aria-selected', 'true')
  await expect(item(page, note, 'Done')).toBeVisible()
})

// The results of exports and imports are for Administrators, as the jobs are (424-66).
test("a contributor sees the items of Contributors, and none of the Administrators'", async ({ page }) => {
  await openApp(page, '/acme/shipping/default/inbox')

  await expect(items(page)).toHaveCount(2)
  await expect(items(page).nth(0)).toContainText(suggestion)
  await expect(items(page).nth(1)).toContainText(note)
  await expect(item(page, failedExport)).toHaveCount(0)
  await expect(page.getByText('Administrators only')).toHaveCount(0)
  await expect(tab(page, 'Open')).toHaveAccessibleName(/^Open\s*2$/)

  await tab(page, 'Done').click()
  await expect(items(page, 'Done')).toHaveCount(1)
  await expect(items(page, 'Done')).toContainText([accepted])
  await expect(item(page, finishedImport, 'Done')).toHaveCount(0)
})

// The API serves the inbox from Contributor up: a Reader sees the entry with the lock, and the page asks for nothing.
test('a reader sees the lock of Contributors, in the menu and on the page', async ({ page }, testInfo) => {
  const inboxRequests: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.endsWith('/inbox') && request.url().includes('/api/')) {
      inboxRequests.push(request.url())
    }
  })
  await openApp(page, '/northwind/portal/default/inbox')

  await expect(page.getByRole('heading', { name: 'Contributors only' })).toBeVisible()
  await expect(page.getByRole('tab')).toHaveCount(0)
  await expect(page.getByRole('list', { name: 'Open items' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Refresh' })).toHaveCount(0)

  const entry = (await menu(page, testInfo)).getByRole('link', { name: 'Inbox', exact: true })
  await expect(entry).toHaveAttribute('href', '/northwind/portal/default/inbox')
  await expect(entry.locator('[data-slot="linkTrailingBadge"]')).toHaveCount(1)
  await expect(entry.locator('[data-slot="linkLeadingChip"]')).toHaveCount(0)
  expect(inboxRequests).toEqual([])
})

// The tab that followed a job has shown its end, so it dismisses the job's item (spec 7.4). The jobs of the mock live
// in the page, so the test only clicks after openApp: goto would reload the app, and the archive would be gone.
test('finds an export that the tab started under Done, with its archive to download again', async ({
  page,
}, testInfo) => {
  await openApp(page, '/acme/billing/default/modeling/export-import')
  const panel = page.getByRole('region', { name: 'Export', exact: true })
  const downloading = page.waitForEvent('download')
  await panel.getByRole('button', { name: 'Export', exact: true }).click()
  const first = await downloading
  await expect(panel.getByText(/was downloaded\.$/)).toBeVisible()

  await (await menu(page, testInfo)).getByRole('link', { name: 'Inbox', exact: true }).click()
  await expect(page).toHaveURL(path)
  // the tab reported the export, so it is not one of the open items
  await expect(items(page)).toHaveCount(3)
  await expect(item(page, 'Export finished')).toHaveCount(0)

  await tab(page, 'Done').click()
  const exported = item(page, 'Export finished', 'Done')
  await expect(exported).toContainText('Dismissed by Ada Lovelace')
  await expect(exported).toContainText('Administrators only')
  await expect(exported).toContainText('Expires')

  const again = page.waitForEvent('download')
  await exported.getByRole('button', { name: 'Download', exact: true }).click()
  expect((await again).suggestedFilename()).toBe(first.suggestedFilename())
  expect(first.suggestedFilename()).toMatch(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/)
})

test('shows the result of an import that the tab started, from its item under Done', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/modeling/export-import')
  const panel = page.getByRole('region', { name: 'Import', exact: true })
  await panel.locator('input[type="file"]').setInputFiles({
    name: 'edited.zip',
    mimeType: 'application/zip',
    buffer: Buffer.from(createZip([textFile('annotations/rst.invoicing.json', '{"Title":"Edited by the test"}\n')])),
  })
  await panel.getByRole('button', { name: 'Import', exact: true }).click()
  await expect(
    page.getByRole('region', { name: 'Latest import', exact: true }).getByText('Imported', { exact: true }),
  ).toBeVisible()

  await (await menu(page, testInfo)).getByRole('link', { name: 'Inbox', exact: true }).click()
  await tab(page, 'Done').click()
  const imported = item(page, 'Import finished', 'Done').filter({ hasText: 'edited.zip' })
  await expect(imported).toContainText('edited.zip: 0 created, 1 updated, 0 unchanged, 0 skipped, 0 failed')
  await imported.getByRole('button', { name: 'Show the result', exact: true }).click()

  const result = page.getByRole('dialog', { name: 'Import finished' })
  const report = result.getByRole('region', { name: 'Report', exact: true })
  await expect(report.getByText('Imported', { exact: true })).toBeVisible()
  await expect(report).toContainText('annotations/rst.invoicing.json')
  await expect(result.getByRole('button', { name: 'Import now' })).toHaveCount(0)
  // the report is not cut: the slideover is as wide as the table needs, or the whole screen of a phone
  const viewport = page.viewportSize()!
  await expect
    .poll(async () => {
      const box = await report.boundingBox()
      return !!box && box.x >= 0 && box.x + box.width <= viewport.width
    })
    .toBe(true)

  await page.keyboard.press('Escape')
  await expect(result).toHaveCount(0)
})
