import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// The empty states of the graph and its create actions (424-130).

/** Registers the view `empty` in acme/billing through the mock API and opens its graph: a view without annotations. */
async function openEmptyView(page: Page): Promise<void> {
  await openApp(page, '/acme/billing/default/annotations')
  await page.waitForFunction(() => !!navigator.serviceWorker?.controller)
  const status = await page.evaluate(async () => {
    const response = await fetch('/api/v1/acme/billing/views', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ Name: 'empty' }),
    })
    return response.status
  })
  expect(status).toBe(201)
  await goto(page, '/acme/billing/empty/graph')
}

/** Hides every type from the legend (from its popover on a phone) and returns what the empty state must stay below. */
async function hideEveryType(page: Page, testInfo: TestInfo): Promise<Locator> {
  const legend = page.getByRole('group', { name: 'Annotation types' })
  const shown = legend.getByRole('button', { pressed: true })
  const types = page.getByRole('button', { name: /^Types/ })
  if (isMobile(testInfo)) {
    await types.click()
  }
  await expect(shown).toHaveCount(7)
  while ((await shown.count()) > 0) {
    await shown.first().click()
  }
  if (isMobile(testInfo)) {
    await page.keyboard.press('Escape')
    await expect(legend).toBeHidden()
    return types
  }
  return legend
}

const bottomOf = async (locator: Locator) => {
  const box = (await locator.boundingBox())!
  return box.y + box.height
}

test('the graph of an empty view offers New annotation and Bulk create, and opens the first annotation', async ({
  page,
}) => {
  await openEmptyView(page)
  const graph = page.getByRole('application')
  const empty = graph.getByRole('heading', { name: 'No annotations in this view' })
  await expect(empty).toBeVisible()
  await expect(page.getByRole('group', { name: 'Annotation types' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /^Types/ })).toHaveCount(0)

  await expect(graph.getByRole('button', { name: 'Bulk create', exact: true })).toBeVisible()
  await graph.getByRole('button', { name: 'New annotation', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'New annotation' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Subject' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('cyberdyne')
  await dialog.getByRole('button', { name: 'Create' }).click()

  await expect(page).toHaveURL(/\/acme\/billing\/empty\/graph\/sbt\.cyberdyne$/)
  await expect(page.getByTestId('annotation-identity')).toHaveText('sbt.cyberdyne')
  await expect(empty).toHaveCount(0)
})

test('Bulk create opens from the empty graph', async ({ page }) => {
  await openEmptyView(page)
  await page.getByRole('application').getByRole('button', { name: 'Bulk create', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Bulk create annotations' })).toBeVisible()
})

test('"Every type is hidden" stays below the legend, which turns the types back on', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/graph')
  await expect(page.getByText('367 annotations', { exact: true })).toBeVisible()

  const above = await hideEveryType(page, testInfo)
  const hidden = page.getByRole('heading', { name: 'Every type is hidden' })
  await expect(hidden).toBeVisible()
  // The whole empty state, icon included, starts below the legend.
  const state = page.getByRole('application').locator('div', { has: hidden }).last()
  expect((await state.boundingBox())!.y).toBeGreaterThanOrEqual(await bottomOf(above))

  // Before 424-130 the empty state was a full-height column over the middle of the legend: it took the clicks of the
  // middle chips on a desktop, and of the Types button on a phone.
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: /^Types/ }).click()
  } else {
    await page.locator('button[title="Show contexts"]').click()
    await expect(hidden).toHaveCount(0)
  }
  await page.getByRole('button', { name: 'All types' }).click()
  await expect(hidden).toHaveCount(0)
  await expect(page.getByText('367 annotations', { exact: true })).toBeVisible()
})

test('Enter on a legend chip toggles its type while a node is selected', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'On a phone the legend is in a popover outside the graph.')
  await openApp(page, '/acme/billing/default/graph/sbt.contoso')
  await expect(page.getByTestId('annotation-identity')).toHaveText('sbt.contoso')

  const chip = page.locator('button[title="Hide subjects"]')
  await chip.focus()
  await page.keyboard.press('Enter')
  await expect(page.locator('button[title="Show subjects"]')).toHaveAttribute('aria-pressed', 'false')
  await expect(page).toHaveURL(/\/graph\/sbt\.contoso\?/)
})

test('New in the top bar of Graph opens the new annotation in the graph', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/graph')
  await expect(page.getByText('367 annotations', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: isMobile(testInfo) ? 'New annotation' : 'New', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'New annotation' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Subject' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('cyberdyne')
  await dialog.getByRole('button', { name: 'Create' }).click()

  // The detail keeps the query of the graph.
  await expect(page).toHaveURL(/\/acme\/billing\/default\/graph\/sbt\.cyberdyne\?mock-scenario=default$/)
  await expect(page.getByTestId('annotation-identity')).toHaveText('sbt.cyberdyne')
})

test('a dialog left open on Graph does not open again on Annotations', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')
  await expect(page.getByRole('button', { name: 'Reload the catalog' })).toBeEnabled()
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Open navigation' }).click()
    await page.getByRole('dialog').getByRole('link', { name: 'Graph', exact: true }).click()
  } else {
    await page.getByRole('link', { name: 'Graph', exact: true }).first().click()
  }
  await expect(page).toHaveURL(/\/acme\/billing\/default\/graph/)

  const newAnnotation = page.getByRole('button', { name: isMobile(testInfo) ? 'New annotation' : 'New', exact: true })
  const dialog = page.getByRole('dialog', { name: 'New annotation' })
  await newAnnotation.click()
  await expect(dialog).toBeVisible()

  await page.goBack()
  await expect(page).toHaveURL(/\/acme\/billing\/default\/annotations/)
  await expect(page.getByRole('button', { name: 'Reload the catalog' })).toBeEnabled()
  await expect(dialog).toHaveCount(0)
  await newAnnotation.click()
  await expect(dialog).toBeVisible()
})

test('a Reader has no create actions on Graph', async ({ page }) => {
  // northwind.portal: the mock user is a Reader
  await openApp(page, '/northwind/portal/default/graph')
  await expect(page.getByRole('button', { name: 'Reload the catalog' })).toBeEnabled()
  await expect(page.getByText(/^\d+ annotations$/)).toBeVisible()
  for (const name of ['New', 'New annotation', 'Bulk create']) {
    await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
  }

  // An empty view of a Reader: the empty state without actions, and no legend.
  await goto(page, '/northwind/portal/empty/graph')
  const graph = page.getByRole('application')
  await expect(graph.getByRole('heading', { name: 'No annotations in this view' })).toBeVisible()
  await expect(graph.getByRole('button')).toHaveCount(0)
})
