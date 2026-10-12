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

  // The description is Markdown: the Preview tab renders headings, lists and tables.
  await page
    .getByRole('textbox', { name: 'Description' })
    .fill('## Endpoints\n\n- api\n- web\n\n| Name | URL |\n| --- | --- |\n| api | `/api` |')
  await page.getByRole('tab', { name: 'Preview' }).click()
  const preview = page.locator('.markdown')
  await expect(preview.getByRole('heading', { name: 'Endpoints' })).toBeVisible()
  await expect(preview.getByRole('listitem')).toHaveText(['api', 'web'])
  await expect(preview.getByRole('table').getByRole('cell', { name: '/api' })).toBeVisible()

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

test('shows only subjects or only responsibilities at the root of the tree, with every path', async ({
  page,
}, testInfo) => {
  test.skip(isMobile(testInfo), 'The tree mode is desktop only.')
  // The orphaned unit of execution of the mock data: its execution and context are missing.
  const key = 'uxe.northwind.invoicing.legacy.pdf'
  const rows = page.getByRole('row')

  // By subject: below the context and below the usage of its execution.
  await openApp(page, `/acme/billing/default/annotations?mode=tree&q=key:${key}`)
  await expect(rows.filter({ hasText: key })).toHaveCount(2)
  await expect(rows.filter({ hasText: 'sbt.northwind' })).toHaveCount(1)
  await expect(rows.filter({ hasText: 'cnt.northwind.legacy' })).toHaveCount(1)
  await expect(rows.filter({ hasText: 'rst.invoicing' })).toHaveCount(0)

  // Selecting one copy selects the annotation, so every copy shows selected.
  const copies = page.getByRole('checkbox', { name: `Select ${key}` })
  await copies.first().click()
  await expect(copies.nth(0)).toBeChecked()
  await expect(copies.nth(1)).toBeChecked()
  await expect(page.getByText('1 selected')).toBeVisible()

  // By responsibility: below the unit and below the usage of its execution.
  await goto(page, `/acme/billing/default/annotations?mode=tree&by=responsibility&q=key:${key}`)
  await expect(rows.filter({ hasText: key })).toHaveCount(2)
  await expect(rows.filter({ hasText: 'rst.invoicing' })).toHaveCount(1)
  await expect(rows.filter({ hasText: 'unt.invoicing.pdf' })).toHaveCount(1)
  await expect(rows.filter({ hasText: 'sbt.northwind' })).toHaveCount(0)
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

test('comes back to the filter, the detail and its tab after another section', async ({ page }, testInfo) => {
  // The sidebar is a slideover below lg, and on a phone the detail covers the screen until it is closed.
  const phone = isMobile(testInfo)
  const navigate = async (name: string) => {
    if (phone) {
      await page.getByRole('button', { name: 'Open navigation' }).click()
      await page.getByRole('dialog').getByRole('link', { name, exact: true }).click()
    } else {
      await page.getByRole('link', { name, exact: true }).first().click()
    }
  }

  await openApp(page, '/acme/billing/default/annotations')
  await page.getByRole('textbox', { name: 'Filter annotations' }).fill('type:sbt')
  await expect(page).toHaveURL(/q=type(%3A|:)sbt/)
  if (!phone) {
    await openFromList(page, 'sbt.northwind', testInfo)
    await page.getByRole('link', { name: 'History', exact: true }).click()
    await expect(page).toHaveURL(/\/annotations\/sbt\.northwind\/history/)
  }

  await navigate('Configurations')
  await expect(page).toHaveURL(/\/acme\/billing\/default\/configurations$/)
  await navigate('Annotations')

  await expect(page.getByRole('textbox', { name: 'Filter annotations' })).toHaveValue('type:sbt')
  if (!phone) {
    await expect(page).toHaveURL(/\/annotations\/sbt\.northwind\/history\?.*q=type(%3A|:)sbt/)
    await expect(page.getByRole('region', { name: 'History' })).toBeVisible()

    // Selecting the open section again starts over.
    await navigate('Annotations')
    await expect(page).toHaveURL(/\/acme\/billing\/default\/annotations$/)
    await expect(page.getByRole('textbox', { name: 'Filter annotations' })).toHaveValue('')
  }
})
