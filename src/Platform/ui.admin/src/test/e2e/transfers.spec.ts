import { readFile } from 'node:fs/promises'
import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { createZip, readZip, textFile, type ZipFile } from '../../app/mock/zip'
import { goto, isMobile, openApp } from './support'

// The Export & Import page of a view (spec 4.1-4.4 of the Export and Import of a View), against the mock API. It is a
// tab of Modeling (424-38).
const path = '/acme/billing/default/modeling/export-import'

const section = (page: Page, name: 'Export' | 'Import' | 'Latest import') =>
  page.getByRole('region', { name, exact: true })

/** The navigation menu: the docked sidebar, or on a phone the slideover behind the menu button. */
async function menu(page: Page, testInfo: TestInfo): Promise<Locator> {
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Open navigation' }).click()
    return page.getByRole('dialog')
  }
  return page.locator('nav').filter({ has: page.getByRole('link', { name: 'Organizations' }) })
}

/**
 * Exports the view with the given kinds and format, and returns the downloaded zip. The toast that confirms the
 * download is checked before the file is read: it shows for five seconds, and reading the file can take longer.
 */
async function exportView(
  page: Page,
  options: { leaveOut?: string[]; format?: 'Folder of JSON files' | 'Git repository'; toast?: RegExp } = {},
) {
  const panel = section(page, 'Export')
  for (const kind of options.leaveOut ?? []) {
    await panel.getByRole('checkbox', { name: kind, exact: true }).click()
  }
  await panel.getByRole('radio', { name: options.format ?? 'Folder of JSON files' }).click()

  const downloading = page.waitForEvent('download')
  await panel.getByRole('button', { name: 'Export', exact: true }).click()
  const download = await downloading

  await expect(panel.getByText(/was downloaded\.$/)).toBeVisible()
  if (options.toast) {
    await expect(page.getByText(options.toast)).toBeVisible()
  }
  const files = await readZip(new Uint8Array(await readFile((await download.path())!)))
  return { name: download.suggestedFilename(), files }
}

/** Puts a zip of the files into the Import section. */
async function chooseArchive(page: Page, files: ZipFile[], name = 'edited.zip') {
  await section(page, 'Import')
    .locator('input[type="file"]')
    .setInputFiles({ name, mimeType: 'application/zip', buffer: Buffer.from(createZip(files)) })
}

/** The documents of the result: the rows of the table on desktop, the entries of the list on a phone. */
const documents = (page: Page, testInfo: TestInfo) =>
  isMobile(testInfo)
    ? section(page, 'Latest import').getByRole('list', { name: 'Documents' }).getByRole('listitem')
    : section(page, 'Latest import').getByRole('table', { name: 'Documents' }).getByRole('row')

const text = (file: ZipFile) => new TextDecoder().decode(file.data)
const json = (value: unknown) => `${JSON.stringify(value, null, 2)}\n`

test('opens the page from the sidebar and with g e; the configurations page has no transfer buttons', async ({
  page,
}, testInfo) => {
  await openApp(page, '/acme/billing/default/configurations')
  await expect(page.getByRole('button', { name: 'Export configurations' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Import configurations' })).toHaveCount(0)

  // Modeling opens on its first tab, Schemas.
  await (await menu(page, testInfo)).getByRole('link', { name: 'Modeling', exact: true }).click()
  await expect(page).toHaveURL('/acme/billing/default/modeling/schemas')
  await page.getByRole('tab', { name: 'Export & Import', exact: true }).click()
  await expect(page).toHaveURL(path)
  await expect(section(page, 'Export')).toBeVisible()
  await expect(section(page, 'Import')).toBeVisible()

  await goto(page, '/acme/billing/default/modeling/templates')
  await page.keyboard.press('g')
  await page.keyboard.press('e')
  await expect(page).toHaveURL(path)
})

test('exports only the annotations and templates', async ({ page }) => {
  await openApp(page, path)

  const { name, files } = await exportView(page, {
    leaveOut: ['Schemas', 'Configurations'],
    toast: /^Exported \d+ annotations and \d+ templates$/,
  })

  expect(name).toMatch(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/)
  const names = files.map((file) => file.name)
  expect(names).toEqual(
    expect.arrayContaining(['export.json', 'annotations/rst.invoicing.json', 'templates/exe.template.json']),
  )
  expect(names.filter((file) => file !== 'export.json').every((file) => /^(annotations|templates)\//.test(file))).toBe(
    true,
  )
  expect(JSON.parse(text(files.find((file) => file.name === 'export.json')!)).Include).toEqual([
    'Annotations',
    'Templates',
  ])
})

test('exports the view as a git repository', async ({ page }) => {
  await openApp(page, path)

  const { name, files } = await exportView(page, { format: 'Git repository' })

  expect(name).toMatch(/^export-acme-billing-default-git-\d{8}-\d{6}Z\.zip$/)
  expect(files.map((file) => file.name)).toContain('.git/HEAD')
  expect(files.map((file) => file.name)).not.toContain('export.json')
})

test('needs at least one kind to export', async ({ page }) => {
  await openApp(page, path)
  const panel = section(page, 'Export')

  for (const kind of ['Annotations', 'Schemas', 'Templates', 'Configurations']) {
    await panel.getByRole('checkbox', { name: kind, exact: true }).click()
  }

  await expect(panel.getByText('Pick at least one kind.')).toBeVisible()
  await expect(panel.getByRole('button', { name: 'Export', exact: true })).toBeDisabled()
})

test('checks an edited archive per kind, with a warning, and then imports it', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The flow is the same on phones; the Skip test covers the list of a phone.')
  await openApp(page, path)
  const { files } = await exportView(page)
  const file = (name: string) => files.find((entry) => entry.name === name)!
  const edit = (name: string, change: Record<string, unknown>) =>
    textFile(name, json({ ...(JSON.parse(text(file(name))) as Record<string, unknown>), ...change }))

  await chooseArchive(page, [
    edit('annotations/rst.invoicing.json', { Title: 'Edited by the test' }),
    // a new execution below a missing subject, usage and context: AncestorCreated
    textFile('elsewhere/exe.newco.billing.prod.json', json({ Title: 'New execution' })),
    edit('templates/exe.template.json', { importedByTest: true }),
    edit('schemas/exe.schema.json', { description: 'Edited by the test' }),
    file('templates/exe.template.1.json'),
    file('export.json'),
  ])
  await section(page, 'Import').getByRole('button', { name: 'Check', exact: true }).click()

  const result = section(page, 'Latest import')
  await expect(result.getByText('Checked: nothing was written')).toBeVisible()
  const summary = result.getByRole('table', { name: 'Summary' })
  // Kind, To create, To update, Unchanged, To skip, Fails, Warnings, Excluded files, Ignored files
  await expect(summary.getByRole('row', { name: /^Annotations/ })).toHaveText(/Annotations\s*1\s*1\s*0\s*0\s*0\s*1/)
  await expect(summary.getByRole('row', { name: /^Schemas/ })).toHaveText(/Schemas\s*0\s*1\s*0\s*0\s*0\s*0/)
  await expect(summary.getByRole('row', { name: /^Templates/ })).toHaveText(/Templates\s*0\s*1\s*0\s*0\s*0\s*0/)
  await expect(summary.getByRole('row', { name: /^Total/ })).toHaveText(/Total\s*1\s*3\s*0\s*0\s*0\s*1\s*0\s*2$/)

  const rows = documents(page, testInfo)
  const created = rows.filter({ hasText: 'exe.newco.billing.prod' })
  await expect(created).toContainText('To create')
  await expect(created).toContainText('AncestorCreated')
  await expect(created).toContainText('The missing ancestors')
  await expect(rows.filter({ hasText: 'templates/exe.template.json' })).toContainText('To update')
  await expect(rows.filter({ hasText: 'schemas/exe.schema.json' })).toContainText('Type schema')

  // the warnings chip leaves the annotation with the warning
  await result.getByRole('button', { name: 'Warnings 1' }).click()
  await expect(rows.filter({ hasText: 'exe.' })).toHaveCount(1)
  await result.getByRole('button', { name: 'Warnings 1' }).click()

  await result.getByRole('button', { name: 'Import now' }).click()
  await expect(result.getByText('Imported', { exact: true })).toBeVisible()
  const updated = rows.filter({ hasText: 'annotations/rst.invoicing.json' })
  await expect(updated).toContainText('Updated')
  await expect(
    rows.filter({ hasText: 'templates/exe.template.json' }).getByRole('link', { name: 'exe' }),
  ).toHaveAttribute('href', '/acme/billing/default/modeling/templates/exe')
  await expect(rows.filter({ hasText: 'schemas/exe.schema.json' }).getByRole('link', { name: 'exe' })).toHaveAttribute(
    'href',
    '/acme/billing/default/modeling/schemas/type/exe',
  )

  await updated.getByRole('link', { name: 'rst.invoicing' }).click()
  await expect(page).toHaveURL('/acme/billing/default/annotations/rst.invoicing')
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue('Edited by the test')
})

test('keeps what the view has with Skip, and shows the result again after a reload', async ({ page }, testInfo) => {
  await openApp(page, path)

  await chooseArchive(page, [
    textFile('annotations/rst.invoicing.json', json({ Title: 'Edited by the test' })),
    textFile('annotations/rst.skipped-by-the-test.json', json({ Title: 'New' })),
  ])
  const panel = section(page, 'Import')
  await panel.getByRole('radio', { name: 'Skip' }).click()
  await panel.getByRole('button', { name: 'Import', exact: true }).click()

  const result = section(page, 'Latest import')
  await expect(result.getByText('Imported', { exact: true })).toBeVisible()
  const skipped = documents(page, testInfo).filter({ hasText: 'annotations/rst.invoicing.json' })
  await expect(skipped).toContainText('Skipped')
  await expect(skipped).toContainText('DocumentExists')
  await expect(skipped).toContainText('The view has a different annotation rst.invoicing, and Skip keeps it.')
  // the document stays, so it is linked
  await expect(skipped.getByRole('link', { name: 'rst.invoicing' })).toHaveAttribute(
    'href',
    '/acme/billing/default/annotations/rst.invoicing',
  )

  await result.getByRole('button', { name: 'Skipped 1' }).click()
  await expect(documents(page, testInfo).filter({ hasText: 'rst.skipped-by-the-test' })).toHaveCount(0)

  await page.reload()
  await page.waitForLoadState('networkidle')
  await expect(section(page, 'Latest import').getByText('Imported', { exact: true })).toBeVisible()
  await expect(documents(page, testInfo).filter({ hasText: 'annotations/rst.invoicing.json' })).toContainText(
    'DocumentExists',
  )
})

// Modeling is locked below Contributor (424-38), and with it this page.
test('a reader sees the Modeling lock', async ({ page }) => {
  await openApp(page, '/northwind/portal/default/modeling/export-import')

  await expect(page.getByRole('heading', { name: 'Contributors only' })).toBeVisible()
  await expect(page.getByRole('tab')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Export', exact: true })).toHaveCount(0)
  await expect(section(page, 'Export')).toHaveCount(0)
  await expect(section(page, 'Import')).toHaveCount(0)
})

// The export and the import need the Administrator role (424-66). The mock user is a Contributor of acme/shipping: the
// page keeps the four other tabs of Modeling, with none selected, and shows a lock instead of the sections.
test('a contributor sees the lock of Administrators under the four other tabs', async ({ page }) => {
  await openApp(page, '/acme/shipping/default/modeling/export-import')

  await expect(page.getByRole('heading', { name: 'Administrators only' })).toBeVisible()
  await expect(
    page.getByText("The export and the import of the view are available to the project's Administrators."),
  ).toBeVisible()
  await expect(page.getByRole('tab')).toHaveText(['Schemas', 'Templates', 'Deleted', 'Compare views'])
  await expect(page.getByRole('tab', { selected: true })).toHaveCount(0)
  await expect(section(page, 'Export')).toHaveCount(0)
  await expect(section(page, 'Import')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Schemas', exact: true }).click()
  await expect(page).toHaveURL('/acme/shipping/default/modeling/schemas')
  await expect(page.getByRole('tab', { name: 'Schemas', exact: true })).toHaveAttribute('aria-selected', 'true')
})

test('a file that is not a zip is refused by the import', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'One run is enough.')
  await openApp(page, path)
  await section(page, 'Import')
    .locator('input[type="file"]')
    .setInputFiles({ name: 'not-a-zip.zip', mimeType: 'application/zip', buffer: Buffer.from('not a zip') })
  await section(page, 'Import').getByRole('button', { name: 'Import', exact: true }).click()

  await expect(section(page, 'Import').getByText('The body is not a zip archive.')).toBeVisible()
})
