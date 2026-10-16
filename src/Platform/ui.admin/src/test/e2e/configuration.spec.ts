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

test('the breadcrumb opens the configuration of a parent and keeps Lineage at the right', async ({ page }) => {
  await openApp(page, `${path}?q=northwind`)
  const breadcrumb = page.getByRole('navigation', { name: 'Annotation ancestors' })
  await expect(breadcrumb.getByRole('link')).toHaveText(['Subject northwind', 'Context prod'])
  await expect(breadcrumb.locator('[aria-current="page"]')).toHaveText('Execution prod')

  // As in the annotation detail: Lineage ends the row of the breadcrumb, level with it.
  const row = (await breadcrumb.locator('..').boundingBox())!
  const nav = (await breadcrumb.boundingBox())!
  const lineage = (await page.getByRole('button', { name: 'Lineage' }).boundingBox())!
  expect(Math.abs(lineage.x + lineage.width - (row.x + row.width))).toBeLessThan(1)
  expect(lineage.x).toBeGreaterThanOrEqual(nav.x + nav.width)
  expect(lineage.y).toBeLessThan(nav.y + nav.height)
  expect(lineage.y + lineage.height).toBeGreaterThan(nav.y)

  await breadcrumb.getByRole('link', { name: 'Context prod' }).click()
  await expect(page).toHaveURL(/\/acme\/billing\/default\/configurations\/cnt\.northwind\.prod\?q=northwind$/)
  await expect(page.getByRole('link', { name: 'Effective' })).toBeVisible()
  await expect(breadcrumb.locator('[aria-current="page"]')).toHaveText('Context prod')
})

test('the JSON editor completes a binding and explains it on hover', async ({ page }) => {
  await openFromTemplate(page)
  await page.getByRole('radio', { name: 'JSON' }).click()
  await expect(page.locator('.cm-content').first()).toContainText('"retries"')

  await replaceEditorText(page, '{\n  "endpoint": "')
  await page.keyboard.type('@con')
  const completion = page.locator('.cm-tooltip-autocomplete')
  // Clicked, not Enter: a key right after the list opens is ignored (interactionDelay).
  await completion.getByRole('option', { name: /^config/ }).click()

  // The argument lists the paths of the configuration.
  await completion.getByRole('option', { name: '/retries' }).click()
  await page.keyboard.press('End')
  await page.keyboard.insertText('"\n}')
  await expect(page.locator('.cm-content').first()).toContainText('"endpoint": "@config(\\"/retries\\")"')

  // The hover opens after the pointer rests on the binding; under load a single move can be missed, so move again.
  const hover = page.locator('.cm-tooltip-hover')
  await expect(async () => {
    await page.mouse.move(0, 0)
    await page.locator('.cm-binding').first().hover()
    await expect(hover).toContainText('Function config', { timeout: 2_000 })
  }).toPass()
  await expect(hover).toContainText('Reads a value of the configuration being resolved')
})

test('the fx reference of a form field lists the sources of the server and fills the value', async ({ page }) => {
  await openFromTemplate(page)
  const endpoint = page.locator('[data-path="/endpoint"]')
  await endpoint.getByRole('button', { name: 'fx' }).click()

  await endpoint.getByRole('button', { name: 'Binding reference' }).click()
  const reference = page.getByRole('region', { name: 'Binding reference' })
  await expect(reference.locator('[data-binding="source-primaryVault"]')).toContainText('secret')
  await reference.locator('[data-binding="function-config"]').getByRole('button', { name: 'Use' }).first().click()

  await expect(endpoint.getByRole('textbox')).toHaveValue('@config("/maxPrice")')
  await expect(endpoint.locator('[data-binding-explanation]')).toContainText(
    'Function config: Reads a value of the configuration being resolved',
  )
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
