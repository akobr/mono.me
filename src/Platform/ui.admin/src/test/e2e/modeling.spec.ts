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
