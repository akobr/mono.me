import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { expectAccessible, goto, isMobile, openApp } from './support'

// Soft delete, Deleted, restore, re-create and history (soft-delete spec 4.7) against the mock API.
const base = '/acme/billing/default'

/** Calls the mock API from the page (the MSW worker answers it), for data the flow under test needs first. */
async function api(page: Page, method: 'GET' | 'DELETE', path: string): Promise<unknown> {
  // the worker answers only once it controls the page
  await page.waitForFunction(() => !!navigator.serviceWorker?.controller)
  return page.evaluate(
    async (request) => {
      const response = await fetch(`/api/v1${request.path}`, { method: request.method })
      const text = await response.text()
      return text ? JSON.parse(text) : undefined
    },
    { method, path },
  )
}

const deleteByApi = (page: Page, key: string, scope = base) => api(page, 'DELETE', `${scope}/annotations/${key}`)

async function openNewAnnotation(page: Page, testInfo: TestInfo) {
  await page.getByRole('button', { name: isMobile(testInfo) ? 'New annotation' : 'New', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'New annotation' })).toBeVisible()
}

async function createSubject(page: Page, testInfo: TestInfo, name: string) {
  await openNewAnnotation(page, testInfo)
  const dialog = page.getByRole('dialog', { name: 'New annotation' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Subject' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill(name)
  await dialog.getByRole('button', { name: 'Create' }).click()
}

async function rowAction(page: Page, key: string, action: string) {
  await page.getByRole('button', { name: `Actions for ${key}` }).click()
  await page.getByRole('menuitem', { name: action, exact: true }).click()
}

test('deletes an annotation and undoes it from the toast', async ({ page }) => {
  await openApp(page, `${base}/annotations/sbt.contoso`)

  await page.getByRole('button', { name: 'More actions' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  const confirm = page.getByRole('dialog', { name: 'Delete sbt.contoso?' })
  await expect(confirm).toContainText('move to Deleted, with their configuration')
  await confirm.getByRole('button', { name: 'Delete' }).click()

  await expect(page.getByText('sbt.contoso deleted', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Undo' }).click()
  await expect(page.getByText('sbt.contoso restored', { exact: true })).toBeVisible()
  await expect(page).toHaveURL(/\/annotations\/sbt\.contoso/)
  await expect(page.getByRole('textbox', { name: 'Title' })).toBeVisible()
})

test('restores a deletion from the Deleted screen, with and without descendants', async ({ page }) => {
  await openApp(page, `${base}/annotations`)
  await deleteByApi(page, 'sbt.contoso')
  await deleteByApi(page, 'sbt.fabrikam')
  await goto(page, `${base}/modeling/deleted`)

  await expect(page.getByRole('row').filter({ hasText: 'sbt.contoso' })).toBeVisible()
  // only the roots of deletions are listed
  await expect(page.getByRole('row').filter({ hasText: 'cnt.contoso.prod' })).toHaveCount(0)

  await rowAction(page, 'sbt.contoso', 'Restore')
  await expect(page.getByText('sbt.contoso restored', { exact: true })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'sbt.contoso' })).toHaveCount(0)

  // restored alone, the subject leaves its children deleted; they become roots
  await rowAction(page, 'sbt.fabrikam', 'Restore without descendants')
  await expect(page.getByText('sbt.fabrikam restored', { exact: true })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'cnt.fabrikam.prod' })).toBeVisible()
})

test('creating over a deleted key restores it, or creates it new', async ({ page }, testInfo) => {
  await openApp(page, `${base}/annotations`)
  await deleteByApi(page, 'sbt.contoso')
  await deleteByApi(page, 'sbt.globex')
  await goto(page, `${base}/annotations`)

  // Restore and update keeps the history (and the configuration) of the deleted key.
  await createSubject(page, testInfo, 'contoso')
  const deleted = page.getByRole('dialog', { name: 'Deleted annotations' })
  await expect(deleted).toContainText('sbt.contoso')
  await deleted.getByRole('button', { name: 'Restore and update' }).click()
  await expect(page.getByText('sbt.contoso restored and updated', { exact: true })).toBeVisible()

  await goto(page, `${base}/annotations/sbt.contoso/history`)
  await expect(page.getByRole('region', { name: 'History' })).toContainText('Restored')

  // Create new replaces it: the history continues with Re-created.
  await goto(page, `${base}/annotations`)
  await createSubject(page, testInfo, 'globex')
  await page.getByRole('dialog', { name: 'Deleted annotations' }).getByRole('button', { name: 'Create new' }).click()
  await expect(page.getByText('sbt.globex created', { exact: true })).toBeVisible()

  await goto(page, `${base}/annotations/sbt.globex/history`)
  await expect(page.getByRole('region', { name: 'History' })).toContainText('Re-created')
})

test('only Administrators can delete permanently', async ({ page }) => {
  await openApp(page, `${base}/annotations`)
  await deleteByApi(page, 'sbt.contoso')
  await deleteByApi(page, 'sbt.northwind', '/acme/shipping/default')

  // acme.shipping: the mock user is a Contributor
  await goto(page, '/acme/shipping/default/modeling/deleted')
  await page.getByRole('button', { name: 'Actions for sbt.northwind' }).click()
  await expect(page.getByRole('menuitem', { name: 'Restore', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Delete permanently' })).toHaveCount(0)
  await page.keyboard.press('Escape')

  // acme.billing: the mock user is the Owner
  await goto(page, `${base}/modeling/deleted`)
  await rowAction(page, 'sbt.contoso', 'Delete permanently')
  const confirm = page.getByRole('dialog', { name: 'Delete sbt.contoso permanently?' })
  await expect(confirm).toContainText('removed for good')
  await confirm.getByRole('textbox').fill('sbt.contoso')
  await confirm.getByRole('button', { name: 'Delete permanently' }).click()
  await expect(page.getByText('sbt.contoso deleted permanently', { exact: true })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'sbt.contoso' })).toHaveCount(0)
})

test('the history shows who created and updated an annotation', async ({ page }) => {
  await openApp(page, `${base}/annotations/sbt.northwind`)

  await page.getByRole('textbox', { name: 'Title' }).fill('Northwind Traders')
  await page.getByRole('button', { name: /^Save/ }).click()
  await expect(page.getByText('Annotation saved', { exact: true })).toBeVisible()

  await page.getByRole('link', { name: 'History', exact: true }).click()
  const history = page.getByRole('region', { name: 'History' })
  await expect(history).toContainText('Updated')
  await expect(history).toContainText('Ada Lovelace')
  await expect(history).toContainText('Title:')
  await expect(history).toContainText('Northwind Traders')
  await expect(history).toContainText('Created')
  await expect(history).toContainText('System')
})

test('the configuration link tells own, inherited and missing configuration apart', async ({ page }) => {
  await openApp(page, `${base}/annotations`)
  const configurations = (await api(page, 'GET', `${base}/configurations`)) as {
    Configurations: { AnnotationKey: string; HasContent?: boolean }[]
  }
  const annotations = (await api(page, 'GET', `${base}/annotations`)) as { Annotations: { AnnotationKey: string }[] }
  const configured = new Set(
    configurations.Configurations.filter((summary) => summary.HasContent).map((summary) => summary.AnnotationKey),
  )
  const keys = annotations.Annotations.map((annotation) => annotation.AnnotationKey)

  const own = [...configured][0]!
  // an execution without own content still gets the execution template
  const inherited = keys.find((key) => key.startsWith('exe.') && !configured.has(key))!
  // a subject has no ancestors and no subject template exists
  const none = keys.find((key) => key.startsWith('sbt.') && !configured.has(key))!

  const link = page.locator('[data-origin]')

  await goto(page, `${base}/annotations/${own}`)
  await expect(link).toHaveAttribute('data-origin', 'own')
  await expect(link).toContainText(/Configuration · v\d+/)

  await goto(page, `${base}/annotations/${inherited}`)
  await expect(link).toHaveAttribute('data-origin', 'inherited')
  await expect(link).toContainText('Inherited configuration')

  await goto(page, `${base}/annotations/${none}`)
  await expect(link).toHaveAttribute('data-origin', 'none')
  await expect(link).toContainText('No configuration')
})

test('the Deleted screen has no serious accessibility violations', async ({ page }) => {
  await openApp(page, `${base}/annotations`)
  await deleteByApi(page, 'sbt.contoso')
  await goto(page, `${base}/modeling/deleted`)
  await expect(page.getByRole('row').filter({ hasText: 'sbt.contoso' })).toBeVisible()
  await expectAccessible(page)
})
