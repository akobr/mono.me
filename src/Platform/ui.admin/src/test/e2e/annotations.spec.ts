import { expect, test } from '@playwright/test'
import { goto, isMobile, openApp, openFromList } from './support'

test('filters the grid, edits an annotation in the detail and saves it', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')

  await page.getByRole('textbox', { name: 'Filter annotations' }).fill('type:sbt')
  await expect(page).toHaveURL(/q=type(%3A|:)sbt/)
  await openFromList(page, 'sbt.northwind', testInfo)
  await expect(page).toHaveURL(/\/annotations\/sbt\.northwind/)

  await page.getByRole('textbox', { name: 'Title' }).fill('Northwind Traders')
  await page.getByRole('button', { name: /^Save/ }).click()
  await expect(page.getByText('Annotation saved', { exact: true })).toBeVisible()

  // The mock API keeps the change for the tab: a reload shows the stored title.
  await goto(page, '/acme/billing/default/annotations/sbt.northwind')
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue('Northwind Traders')
})

test('filters with the bottom sheet on a phone', async ({ page }, testInfo) => {
  test.skip(!isMobile(testInfo), 'The bottom sheet replaces the filter tools below md.')
  await openApp(page, '/acme/billing/default/annotations')

  await page.getByRole('button', { name: 'Filters' }).click()
  await page.getByRole('button', { name: 'Executions' }).click()
  await page.getByRole('button', { name: 'Active', exact: true }).click()
  await page.getByRole('button', { name: 'Done' }).click()

  await expect(page).toHaveURL(/q=type(%3A|:)exe(\+|%20)is(%3A|:)active/)
  await expect(page.getByRole('listitem').first()).toContainText('exe.')
})

test('opens the detail of a graph node', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/graph')
  await expect(page.getByText('367 annotations')).toBeVisible()

  if (isMobile(testInfo)) {
    // Keyboard: the arrow keys move between connected nodes and Enter opens one.
    await page.getByRole('application').focus()
    await page.keyboard.press('ArrowDown')
    await page.keyboard.press('Enter')
  } else {
    // Pointer: find a node by its tooltip, then click it.
    const box = (await page.locator('canvas').last().boundingBox())!
    let clicked = false
    for (let y = box.y + 140; y < box.y + box.height - 40 && !clicked; y += 14) {
      for (let x = box.x + 40; x < box.x + box.width - 40; x += 14) {
        await page.mouse.move(x, y)
        if (await page.getByRole('tooltip').count()) {
          await page.mouse.click(x, y)
          clicked = true
          break
        }
      }
    }
    expect(clicked).toBe(true)
  }

  await expect(page).toHaveURL(/\/graph\/(rst|unt|sbt|usg|cnt|exe|uxe)\./)
  await expect(page.getByRole('link', { name: 'Annotation', exact: true })).toBeVisible()
})

test('new annotations start in the default time zone of the project', async ({ page }, testInfo) => {
  const dialog = page.getByRole('dialog', { name: 'New annotation' })
  const openNewAnnotation = () =>
    page.getByRole('button', { name: isMobile(testInfo) ? 'New annotation' : 'New', exact: true }).click()

  // Every project starts in UTC.
  await openApp(page, '/acme/billing/default/annotations')
  await openNewAnnotation()
  await expect(dialog.getByRole('button', { name: 'Time zone', exact: true })).toContainText('UTC')
  await page.keyboard.press('Escape')

  // Not the browser's zone (Europe/Prague): an annotation without a zone shows that one as its placeholder.
  await goto(page, '/acme/billing/settings')
  await page.getByRole('button', { name: 'Default time zone' }).click()
  await page.getByPlaceholder('Search time zones').fill('Tokyo')
  await page.getByRole('option', { name: /Asia\/Tokyo/ }).click()
  await expect(page.getByRole('button', { name: 'Default time zone' })).toContainText('Asia/Tokyo')

  await goto(page, '/acme/billing/default/annotations')
  await openNewAnnotation()
  await expect(dialog.getByRole('button', { name: 'Time zone', exact: true })).toContainText('Asia/Tokyo')
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Subject' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('cyberdyne')
  await dialog.getByRole('button', { name: 'Create' }).click()
  await expect(page.getByText('sbt.cyberdyne created', { exact: true })).toBeVisible()

  await goto(page, '/acme/billing/default/annotations/sbt.cyberdyne')
  await expect(page.getByRole('button', { name: 'Time zone', exact: true })).toContainText('Asia/Tokyo')
})
