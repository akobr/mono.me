import { expect, test } from '@playwright/test'
import { goto, isMobile, openApp, openFromList } from './support'

test('filters the grid, edits an annotation in the detail and saves it', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')

  await page.getByRole('textbox', { name: 'Filter annotations' }).fill('type:sbt')
  await expect(page).toHaveURL(/q=type(%3A|:)sbt/)
  await openFromList(page, 'sbt.northwind', testInfo)
  await expect(page).toHaveURL(/\/annotations\/sbt\.northwind/)

  // Name, type and key are text: the type icon and the key with the name in bold, no disabled text boxes.
  const identity = page.getByTestId('annotation-identity')
  await expect(identity).toHaveText('sbt.northwind')
  await expect(identity.getByRole('img', { name: 'Subject' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveCount(0)

  await page.getByRole('textbox', { name: 'Title' }).fill('Northwind Traders')
  await page.getByRole('button', { name: /^Save/ }).click()
  await expect(page.getByText('Annotation saved', { exact: true })).toBeVisible()

  // The mock API keeps the change for the tab: a reload shows the stored title.
  await goto(page, '/acme/billing/default/annotations/sbt.northwind')
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue('Northwind Traders')
})

test('opens the detail as an overlay, pins, resizes and shows it in full screen, and remembers it', async ({
  page,
}, testInfo) => {
  test.skip(isMobile(testInfo), 'Below md the detail is always full screen.')
  await openApp(page, '/acme/billing/default/annotations')
  await openFromList(page, 'sbt.northwind', testInfo)

  const detail = page.locator('[data-detail-mode]')
  await expect(detail).toHaveAttribute('data-detail-mode', 'overlay')

  await page.getByRole('button', { name: 'Pin the detail' }).click()
  await expect(detail).toHaveAttribute('data-detail-mode', 'pinned')

  // The border between the list and the detail is the resize handle.
  const before = (await detail.boundingBox())!.width
  const handle = (await page.getByRole('separator', { name: 'Resize the detail' }).boundingBox())!
  await page.mouse.move(handle.x + handle.width / 2, handle.y + 200)
  await page.mouse.down()
  await page.mouse.move(handle.x - 150, handle.y + 200, { steps: 5 })
  await page.mouse.up()
  expect((await detail.boundingBox())!.width).toBeGreaterThan(before + 100)

  await page.getByRole('button', { name: 'Open in full screen' }).click()
  await expect(detail).toHaveAttribute('data-detail-mode', 'full')
  await expect(page.getByRole('textbox', { name: 'Filter annotations' })).toBeHidden()

  // The next visit opens the detail the same way.
  await goto(page, '/acme/billing/default/annotations/sbt.northwind')
  await expect(detail).toHaveAttribute('data-detail-mode', 'full')

  await page.getByRole('button', { name: 'Exit full screen' }).click()
  await expect(detail).toHaveAttribute('data-detail-mode', 'pinned')
  await page.getByRole('button', { name: 'Close the detail' }).click()
  await expect(detail).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: 'Filter annotations' })).toBeVisible()
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
