import { expect, test, type Page } from '@playwright/test'
import { goto, openApp, replaceEditorText } from './support'

const key = 'exe.northwind.invoicing.prod'
const path = `/acme/billing/default/configurations/${key}`

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

/** Opens the drop-down of the New dialog on Configurations and searches it; the options are teleported to the body. */
async function searchNewConfiguration(page: Page, term: string) {
  // The one of the top bar: with a filter, the empty state (and its button) shows until the catalog has loaded.
  await page.getByRole('button', { name: 'New configuration' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New configuration' })
  await dialog.getByRole('button', { name: 'Annotation' }).click()
  await page.getByPlaceholder('Search keys').fill(term)
  return dialog
}

test('New opens the configuration of an annotation without one', async ({ page }) => {
  await openApp(page, '/acme/billing/default/configurations?q=northwind')

  // usg.northwind.invoicing has a configuration in the seed: it is a row, not an option.
  await searchNewConfiguration(page, 'northwind.invoicing')
  await expect(page.getByRole('option', { name: key, exact: true })).toBeVisible()
  await expect(page.getByRole('option', { name: 'usg.northwind.invoicing', exact: true })).toHaveCount(0)
  await page.getByRole('option', { name: key, exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'New configuration' })
  await expect(dialog.getByRole('button', { name: 'Annotation' })).toHaveText(key)
  // The pick moves the focus to Open editor, so Enter opens it.
  await expect(dialog.getByRole('button', { name: 'Open editor' })).toBeFocused()
  await page.keyboard.press('Enter')

  await expect(dialog).toBeHidden()
  await expect(page).toHaveURL(
    /\/acme\/billing\/default\/configurations\/exe\.northwind\.invoicing\.prod\?q=northwind$/,
  )
  await expect(page.getByText('No configuration yet')).toBeVisible()
  await page.getByRole('button', { name: 'Create empty' }).click()
  await expect(page.getByText('Configuration created', { exact: true })).toBeVisible()

  // Now it is a row of the list and no longer offered.
  await goto(page, '/acme/billing/default/configurations?q=northwind.invoicing.prod')
  await expect(page.getByText(key, { exact: true }).first()).toBeVisible()
  await searchNewConfiguration(page, 'exe.northwind.invoicing')
  await expect(page.getByRole('option', { name: 'exe.northwind.invoicing.staging', exact: true })).toBeVisible()
  await expect(page.getByRole('option', { name: key, exact: true })).toHaveCount(0)
})

test('the empty list of a filter offers New configuration', async ({ page }) => {
  await openApp(page, '/acme/billing/default/configurations?q=no-such-annotation')

  await expect(page.getByRole('heading', { name: 'No configuration matches the filter' })).toBeVisible()
  // the top bar and the empty state
  const buttons = page.getByRole('button', { name: 'New configuration' })
  await expect(buttons).toHaveCount(2)
  await buttons.nth(1).click()
  await expect(page.getByRole('dialog', { name: 'New configuration' })).toBeVisible()
})

test('a reader has no New button on Configurations', async ({ page }) => {
  await openApp(page, '/northwind/portal/default/configurations')

  // The top bar is there, without New.
  await expect(page.getByRole('button', { name: 'Search…' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'New configuration' })).toHaveCount(0)
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
  await openApp(page, `/acme/billing/default/modeling/compare?key=${key}`)

  await expect(page).toHaveURL(/[?&]to=staging(&|$)/)
  await expect(page.getByRole('combobox', { name: 'Compare default with' })).toHaveText('staging')
  await expect(page.getByText('Compare a configuration across views')).toBeHidden()
  await expect(page.locator('.cm-editor').first()).toBeVisible()
})

// The Views page used to link the default view to itself (424-64); such a link no longer compares a view with itself.
test('compare views never compares a view with itself', async ({ page }) => {
  const diffs: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('/diff/')) {
      diffs.push(new URL(request.url()).pathname)
    }
  })

  // acme/billing has staging too: it takes the place of the current view in ?to=
  await openApp(page, `/acme/billing/default/modeling/compare?to=default&key=${key}`)
  await expect(page).toHaveURL(/[?&]to=staging(&|$)/)
  await expect(page.getByRole('combobox', { name: 'Compare default with' })).toHaveText('staging')
  await expect(page.locator('.cm-editor').first()).toBeVisible()
  // the page sees the requests of the mock API: the diff with staging is among them
  await expect.poll(() => diffs.some((diff) => diff.endsWith('/diff/staging'))).toBe(true)
  expect(diffs.filter((diff) => diff.endsWith('/diff/default'))).toEqual([])

  // acme/shipping has only its default view: nothing to compare with, and nothing is requested
  diffs.length = 0
  await goto(page, `/acme/shipping/default/modeling/compare?to=default&key=${key}`)
  await expect(page.getByText('No other view to compare with')).toBeVisible()
  await expect(page.getByText('Compare a configuration across views')).toBeHidden()
  expect(diffs).toEqual([])
})

/** The step marker of the gutter level with the editor line that contains `text`. */
async function markerOf(page: Page, text: string): Promise<string | undefined> {
  return page.evaluate((needle) => {
    const line = [...document.querySelectorAll('.cm-line')].find((element) => element.textContent?.includes(needle))
    const box = line?.getBoundingClientRect()
    const marker = [...document.querySelectorAll('.cm-composition-gutter .cm-composition-marker')].find((element) => {
      const rect = element.getBoundingClientRect()
      const middle = (rect.top + rect.bottom) / 2
      // the hidden spacer keeps the width of the gutter
      return getComputedStyle(element).visibility !== 'hidden' && !!box && middle > box.top && middle < box.bottom
    })
    return marker?.textContent ?? undefined
  }, text)
}

/** The scroll position of the panel around the steps of the composition: its nearest scrolling ancestor. */
async function panelScrollTop(page: Page): Promise<number> {
  return page
    .locator('li[data-step]')
    .first()
    .evaluate((element) => {
      for (let node = element.parentElement; node; node = node.parentElement) {
        const overflow = getComputedStyle(node).overflowY
        if ((overflow === 'auto' || overflow === 'scroll') && node.scrollHeight > node.clientHeight) {
          return node.scrollTop
        }
      }
      return document.scrollingElement?.scrollTop ?? 0
    })
}

/** True when the editor line that contains `text` is in the window and inside every clipping ancestor. */
async function lineInView(page: Page, text: string): Promise<boolean> {
  return page.evaluate((needle) => {
    const line = [...document.querySelectorAll('.cm-line')].find((element) => element.textContent?.includes(needle))
    if (!line) {
      return false
    }
    const box = line.getBoundingClientRect()
    for (let node = line.parentElement; node; node = node.parentElement) {
      if (getComputedStyle(node).overflowY !== 'visible') {
        const rect = node.getBoundingClientRect()
        if (box.top < rect.top || box.bottom > rect.bottom) {
          return false
        }
      }
    }
    return box.top >= 0 && box.bottom <= window.innerHeight
  }, text)
}

// The Composition view of the Effective tab (424-36): the mock composes flat, so every ancestor is applied once.
test('the composition shows the step that set each line and selects the lines of a step', async ({ page }) => {
  const effectiveReads: string[] = []
  page.on('requestfinished', (request) => {
    if (request.method() === 'GET' && new URL(request.url()).pathname.endsWith(`/configuration/${key}`)) {
      effectiveReads.push(request.url())
    }
  })
  await openApp(page, `${path}/effective`)
  // The usage gets a value the execution template lacks and a mode the template replaces. The context's 120 values
  // put the own value of the execution far below the top of the editor (70vh).
  await page.evaluate(
    ([usage, context, execution]) => {
      const mock = window.storytellerMock!
      mock.changeConfiguration(usage, { queue: 'invoices', mode: 'stream' })
      mock.changeConfiguration(
        context,
        Object.fromEntries(Array.from({ length: 120 }, (_, index) => [`setting${index + 1}`, index + 1])),
      )
      mock.changeConfiguration(execution, { owner: 'invoices-team' })
    },
    ['usg.northwind.invoicing', 'cnt.northwind.prod', key] as const,
  )
  const readsBefore = effectiveReads.length
  await page.getByRole('radio', { name: 'Composition' }).click()
  await expect(page).toHaveURL(/[?&]show=composition(&|$)/)
  await expect(page.locator('.cm-content')).toContainText('"queue": "invoices"')

  const steps = page.locator('li[data-step]')
  const usage = await steps.filter({ hasText: 'usg.northwind.invoicing' }).getAttribute('data-step')
  const template = await steps.filter({ hasText: 'Template · Execution' }).getAttribute('data-step')
  await expect.poll(() => markerOf(page, '"queue": "invoices"')).toBe(usage)
  await expect.poll(() => markerOf(page, '"mode": "batch"')).toBe(`${template}T`)
  // the Effective read from before the changes is read again once, and then the hashes agree
  await expect.poll(() => effectiveReads.length).toBe(readsBefore + 1)
  await expect(page.getByText('Read at different times')).toHaveCount(0)

  // the toggle highlights the lines of the template step and keeps the focus; its first line, the mode, is in view
  // already on a large screen, and then nothing scrolls
  const toggle = page.getByRole('button', { name: `Show the lines of step ${template}` })
  await toggle.scrollIntoViewIfNeeded()
  const modeInView = await lineInView(page, '"mode": "batch"')
  const scrolled = await panelScrollTop(page)
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-pressed', 'true')
  await expect(toggle).toBeFocused()
  const selected = page.locator('.cm-line.cm-composition-selected')
  await expect(selected.filter({ hasText: '"mode": "batch"' })).toHaveCount(1)
  if (modeInView) {
    // CodeMirror scrolls in its next measure, in an animation frame
    await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    expect(await panelScrollTop(page)).toBe(scrolled)
  }
  await expect(selected.filter({ hasText: '"queue"' })).toHaveCount(0)
  const status = page.getByText(/^Line \d+: step \d+ · /)
  await expect(status).toHaveText(new RegExp(String.raw`^Line \d+: step ${template} · Template · Execution$`))

  // a click on a marker selects its step
  await page
    .locator('.cm-composition-marker')
    .filter({ hasText: new RegExp(`^${usage}$`) })
    .first()
    .click()
  await expect(page.getByRole('button', { name: `Show the lines of step ${usage}` })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(toggle).toHaveAttribute('aria-pressed', 'false')
  await expect(selected.filter({ hasText: '"queue": "invoices"' })).toHaveCount(1)
  await expect(status).toHaveText(new RegExp(String.raw`^Line \d+: step ${usage} · usg\.northwind\.invoicing$`))

  // the toggle of the own step scrolls the editor down to its line, and the focus stays on the toggle
  const own = await steps.filter({ hasText: key }).getAttribute('data-step')
  expect(await page.locator('.cm-scroller').evaluate((element) => element.scrollTop)).toBe(0)
  const ownToggle = page.getByRole('button', { name: `Show the lines of step ${own}` })
  await ownToggle.click()
  await expect(selected.filter({ hasText: '"owner": "invoices-team"' })).toBeInViewport()
  await expect(ownToggle).toBeFocused()

  // Document is the default again, without the query
  await page.getByRole('radio', { name: 'Document' }).click()
  await expect(page).not.toHaveURL(/show=/)
  await expect(page.locator('.cm-composition-gutter')).toHaveCount(0)
})
