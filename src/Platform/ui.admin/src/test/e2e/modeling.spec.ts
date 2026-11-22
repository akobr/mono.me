import { expect, test, type Page } from '@playwright/test'
import { openApp } from './support'

const modeling = '/acme/billing/default/modeling'

// Close from md, Back below it.
const leave = (page: Page) => page.getByRole('button', { name: /^(Close|Back)$/ }).first()

async function confirmDelete(page: Page, text: string) {
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('textbox').fill(text)
  await dialog.getByRole('button', { name: 'Delete', exact: true }).click()
  await expect(dialog).toBeHidden()
}

// 424-158: CodeMirror reported the {} shown after a delete back as an edit, so the editor kept {} instead of the
// starter document or the empty state, and leaving asked "Discard changes?".
test('a deleted schema returns to the starter document and closes without a prompt', async ({ page }) => {
  await openApp(page, `${modeling}/schemas/type/uxe`)
  await page.getByRole('radio', { name: 'JSON' }).click()
  const editor = page.locator('.cm-content').first()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible()

  await confirmDelete(page, 'delete')
  await expect(page.getByText('Schema deleted', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create', exact: true })).toBeEnabled()
  await expect(editor).toContainText('"$schema"')
  await expect(page.getByText('This schema does not exist yet', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Revert', exact: true })).toBeDisabled()

  await leave(page).click()
  await expect(page).toHaveURL(`${modeling}/schemas`)
  await expect(page.getByRole('dialog', { name: 'Discard changes?' })).toHaveCount(0)
})

test('a deleted template returns to the empty state and closes without a prompt', async ({ page }) => {
  await openApp(page, `${modeling}/templates/exe`)
  await expect(page.locator('.cm-content').first()).toBeVisible()

  await confirmDelete(page, 'exe')
  await expect(page.getByText('Template deleted', { exact: true })).toBeVisible()
  await expect(page.getByText('No execution template', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create template' })).toBeVisible()
  await expect(page.locator('.cm-content')).toHaveCount(0)

  await leave(page).click()
  await expect(page).toHaveURL(`${modeling}/templates`)
  await expect(page.getByRole('dialog', { name: 'Discard changes?' })).toHaveCount(0)
})

// The untouched starter document of a missing schema is not a change, but Create and ⌘S still create it.
test('the starter document of a missing schema saves with the shortcut', async ({ page }) => {
  await openApp(page, `${modeling}/schemas/type/rst`)
  await expect(page.getByText('This schema does not exist yet', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create', exact: true })).toBeEnabled()

  await page.locator('.cm-content').first().click()
  await page.keyboard.press('ControlOrMeta+S')
  await expect(page.getByText('Schema saved (v1)', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Delete', exact: true })).toBeVisible()
})

// A Reader browses Modeling read-only (424-272); the Reader of northwind/portal has the type schemas of the seed.
const reader = '/northwind/portal/default/modeling'

test('a reader reads a schema without write actions', async ({ page }) => {
  const writes: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (request.method() !== 'GET' && path.startsWith('/api/')) {
      writes.push(`${request.method()} ${path}`)
    }
  })
  await openApp(page, `${reader}/schemas/type/exe`)
  const detail = page.getByRole('region', { name: 'Execution schema' })
  const editor = detail.getByRole('textbox', { name: 'Schema document', exact: true })
  await expect(editor).toContainText('"properties"')
  await expect(editor).not.toBeEditable()
  await expect(detail.getByText('Read-only', { exact: true })).toBeVisible()
  for (const name of ['Save', 'Create', 'Revert', 'Delete']) {
    await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
  }

  // The editor takes no focus, so the shortcut reaches the page, which saves only for Contributors.
  await editor.click()
  await page.keyboard.press('ControlOrMeta+S')
  // The versions load after it, so a save would have been sent by then; a Reader restores none of them.
  await detail.getByRole('tab', { name: 'Versions', exact: true }).click()
  await expect(detail.getByRole('button', { name: 'Compare', exact: true })).toBeVisible()
  await expect(detail.getByRole('button', { name: /^Restore/ })).toHaveCount(0)
  await expect(page.getByText(/^Schema saved|^The schema was not saved/)).toHaveCount(0)
  expect(writes).toEqual([])
})

// Below Contributor nothing creates a missing schema, so the detail says that it does not exist instead of offering
// the starter document (rst has no type schema in the seed). The shortcut matters here: for a Contributor it creates
// the schema from the untouched starter document, for a Reader it must send nothing.
test('a reader gets No schema instead of the starter document of a missing schema, and the shortcut creates nothing', async ({
  page,
}) => {
  const writes: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (request.method() !== 'GET' && path.startsWith('/api/')) {
      writes.push(`${request.method()} ${path}`)
    }
  })
  await openApp(page, `${reader}/schemas/type/rst`)
  const detail = page.getByRole('region', { name: 'Responsibility schema' })
  await expect(detail.getByRole('heading', { name: 'No schema', exact: true })).toBeVisible()
  await expect(detail.getByText('This schema does not exist in this view, so it validates nothing.')).toBeVisible()
  await expect(detail.getByText('This schema does not exist yet', { exact: true })).toHaveCount(0)
  await expect(detail.getByRole('textbox')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Create', exact: true })).toHaveCount(0)

  await detail.getByRole('heading', { name: 'No schema', exact: true }).click()
  await page.keyboard.press('ControlOrMeta+S')
  // The versions are read after the shortcut, so a create would have been sent by then.
  const versions = page.waitForResponse((response) =>
    /\/configuration-schema\/type\/rst\/versions/.test(response.url()),
  )
  await detail.getByRole('tab', { name: 'Versions', exact: true }).click()
  await versions
  await expect(page.getByText(/^Schema saved|^The schema was not saved/)).toHaveCount(0)
  expect(writes).toEqual([])
})
