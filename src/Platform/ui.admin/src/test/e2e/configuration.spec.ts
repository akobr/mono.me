import { expect, test, type Page } from '@playwright/test'
import { openApp, replaceEditorText } from './support'

const key = 'exe.northwind.invoicing.prod'
const path = `/acme/billing/default/configurations/${key}`

declare global {
  interface Window {
    storytellerMock?: { changeConfiguration: (key: string, changes: Record<string, unknown>) => void }
  }
}

/** The execution has no own configuration in the seed: it is created from the type template. */
async function openFromTemplate(page: Page) {
  await openApp(page, path)
  await page.getByRole('button', { name: 'Start from template' }).click()
  await expect(page.getByText('Configuration created', { exact: true })).toBeVisible()
}

/** Switches the editor to JSON and applies a change to the document. */
async function editJson(page: Page, change: (content: Record<string, unknown>) => void) {
  await page.getByRole('radio', { name: 'JSON' }).click()
  const editor = page.locator('.cm-content').first()
  await expect(editor).toContainText('"retries"')
  const content = JSON.parse(await editor.innerText()) as Record<string, unknown>
  change(content)
  await replaceEditorText(page, JSON.stringify(content, null, 2))
}

test('Lineage opens the configuration of an ancestor and keeps the filter', async ({ page }) => {
  await openApp(page, `${path}?q=northwind`)
  await page.getByRole('button', { name: 'Lineage' }).click()
  await page.getByRole('link', { name: 'sbt.northwind', exact: true }).click()

  await expect(page).toHaveURL(/\/acme\/billing\/default\/configurations\/sbt\.northwind\?q=northwind$/)
  await expect(page.getByRole('link', { name: 'Effective' })).toBeVisible()
  // a subject has no ancestors
  await expect(page.getByRole('button', { name: 'Lineage' })).toBeDisabled()
})

test('a schema violation is reported and can be saved anyway', async ({ page }) => {
  await openFromTemplate(page)
  await editJson(page, (content) => (content.retries = 42))
  await page.getByRole('button', { name: /^Save/ }).click()

  await expect(page.getByText('The configuration does not match its schema')).toBeVisible()
  await page.getByRole('button', { name: 'Save anyway' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Save anyway' }).click()
  await expect(page.getByText(/^Configuration saved \(v\d+\)$/)).toBeVisible()
})

test('a concurrent change opens the conflict dialog and can be overwritten', async ({ page }) => {
  await openFromTemplate(page)
  await editJson(page, (content) => (content.retries = 5))

  // Someone else saves the same field in the meantime.
  await page.evaluate((target) => window.storytellerMock!.changeConfiguration(target, { retries: 7 }), key)
  await page.getByRole('button', { name: /^Save/ }).click()

  const dialog = page.getByRole('dialog', { name: 'Someone else changed this configuration' })
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: 'Overwrite' }).click()
  await expect(page.getByText(/^Configuration saved \(v\d+\)$/)).toBeVisible()
})

test('saves a change and compares it with the previous version', async ({ page }) => {
  await openFromTemplate(page)
  await editJson(page, (content) => (content.retries = 4))
  await page.getByRole('button', { name: /^Save/ }).click()
  await expect(page.getByText(/^Configuration saved \(v\d+\)$/)).toBeVisible()

  await page.getByRole('link', { name: 'Versions' }).click()
  await page.getByRole('button', { name: 'Compare with previous' }).first().click()
  await expect(page).toHaveURL(/from=\d+/)
  await expect(page.locator('.cm-mergeView, .cm-changedLine, .cm-deletedChunk').first()).toBeVisible()
})

test('compare views preselects the only other view', async ({ page }) => {
  // The seeded project has the views default and staging.
  await openApp(page, `/acme/billing/default/compare?key=${key}`)

  await expect(page).toHaveURL(/[?&]to=staging(&|$)/)
  await expect(page.getByRole('combobox', { name: 'Compare default with' })).toHaveText('staging')
  await expect(page.getByText('Compare a configuration across views')).toBeHidden()
  await expect(page.locator('.cm-editor').first()).toBeVisible()
})
