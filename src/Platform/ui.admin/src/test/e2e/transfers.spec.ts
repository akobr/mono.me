import { readFile } from 'node:fs/promises'
import { expect, test, type Page } from '@playwright/test'
import { createZip, readZip, textFile, type ZipFile } from '../../app/mock/zip'
import { isMobile, openApp } from './support'

const path = '/acme/billing/default/configurations'

/** Exports the view in a format and returns the downloaded files. */
async function exportView(page: Page, format: 'Folder' | 'Git repository') {
  await page.getByRole('button', { name: 'Export configurations' }).click()
  const dialog = page.getByRole('dialog', { name: 'Export configurations' })
  await dialog.getByRole('radio', { name: format }).click()

  const downloading = page.waitForEvent('download')
  await dialog.getByRole('button', { name: 'Export', exact: true }).click()
  const download = await downloading

  await expect(dialog).toBeHidden()
  await expect(page.getByText(/^Exported \d+ configurations?$/)).toBeVisible()

  const files = await readZip(new Uint8Array(await readFile((await download.path())!)))
  return { name: download.suggestedFilename(), files }
}

const text = (file: ZipFile) => new TextDecoder().decode(file.data)

test('exports the view as a folder', async ({ page }) => {
  await openApp(page, path)

  const { name, files } = await exportView(page, 'Folder')

  expect(name).toMatch(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/)
  const names = files.map((file) => file.name)
  expect(names).toContain('export.json')
  // one folder per kind; the version files sit next to their current file
  expect(names).toEqual(expect.arrayContaining(['annotations/rst.invoicing.json', 'templates/exe.template.json']))
  expect(names.some((file) => /^configurations\/[a-z]{3}\..+\.conf\.\d+\.json$/.test(file))).toBe(true)
  expect(
    names
      .filter((file) => file !== 'export.json')
      .every((file) => /^(annotations|schemas|templates|configurations)\/[^/]+\.json$/.test(file)),
  ).toBe(true)
})

test('exports the view as a git repository', async ({ page }) => {
  await openApp(page, path)

  const { name, files } = await exportView(page, 'Git repository')

  expect(name).toMatch(/^export-acme-billing-default-git-\d{8}-\d{6}Z\.zip$/)
  expect(files.map((file) => file.name)).toContain('.git/HEAD')
  expect(files.map((file) => file.name)).not.toContain('export.json')
})

test('imports an edited export: a dry run, then for real', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The file picker flow is the same on phones; one run is enough.')
  await openApp(page, path)
  const { files } = await exportView(page, 'Folder')

  // change one current document, add a key without an annotation and a broken file
  const manifest = JSON.parse(text(files.find((file) => file.name === 'export.json')!)) as {
    Configurations: { AnnotationKey: string; File: string }[]
  }
  const { AnnotationKey: key, File: fileName } = manifest.Configurations[0]!
  const current = files.find((file) => file.name === fileName)!
  const changed = { ...(JSON.parse(text(current)) as Record<string, unknown>), importedByTest: true }
  const archive = createZip([
    textFile(current.name, JSON.stringify(changed, null, 2)),
    textFile('rst.no-such-annotation.conf.json', '{"a":1}'),
    textFile('rst.broken.conf.json', '{"a":'),
    textFile('notes.txt', 'ignored'),
  ])

  await page.getByRole('button', { name: 'Import configurations' }).click()
  const dialog = page.getByRole('dialog', { name: 'Import configurations' })
  await dialog.locator('input[type="file"]').setInputFiles({
    name: 'edited.zip',
    mimeType: 'application/zip',
    buffer: Buffer.from(archive),
  })
  await dialog.getByRole('checkbox', { name: 'Only check, write nothing' }).click()
  await dialog.getByRole('button', { name: 'Check', exact: true }).click()

  await expect(dialog.getByText('was checked; nothing was written.')).toBeVisible()
  const items = dialog.getByRole('list', { name: 'Files' })
  await expect(items.getByRole('listitem').filter({ hasText: key })).toContainText('To update')
  await expect(items.getByRole('listitem').filter({ hasText: 'rst.no-such-annotation' })).toContainText(
    'has no annotation rst.no-such-annotation',
  )
  await expect(items.getByRole('listitem').filter({ hasText: 'rst.broken' })).toContainText('Fails')

  await dialog.getByRole('button', { name: 'Import now' }).click()
  await expect(dialog.getByText('was imported.')).toBeVisible()
  const updated = items.getByRole('listitem').filter({ hasText: key })
  await expect(updated).toContainText('Updated')
  await expect(updated.getByRole('link', { name: key })).toBeVisible()
  await expect(dialog.getByRole('list', { name: 'Summary' })).toContainText('Ignored files 1')
})

test('a reader can export but not import', async ({ page }) => {
  await openApp(page, '/northwind/portal/default/configurations')

  await expect(page.getByRole('button', { name: 'Export configurations' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Import configurations' })).toHaveCount(0)
})

test('a file that is not a zip is refused by the import', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'One run is enough.')
  await openApp(page, path)
  await page.getByRole('button', { name: 'Import configurations' }).click()
  const dialog = page.getByRole('dialog', { name: 'Import configurations' })
  await dialog.locator('input[type="file"]').setInputFiles({
    name: 'not-a-zip.zip',
    mimeType: 'application/zip',
    buffer: Buffer.from('not a zip'),
  })
  await dialog.getByRole('button', { name: 'Import', exact: true }).click()

  await expect(dialog.getByText('The body is not a zip archive.')).toBeVisible()
})
