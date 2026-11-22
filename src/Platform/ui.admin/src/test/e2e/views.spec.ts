import { expect, test, type Locator, type Page } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

declare global {
  interface Window {
    /** The milliseconds every request of the page waits before it is sent; the test of Stop sets it. */
    slowRequests?: number
  }
}

// The comparison of a selection of annotations or configurations with another view of the project, and the copy of
// it to that view (424-309, spec "Copy and Compare Annotations and Configurations Between Views"). In the default
// dataset acme/billing (Owner) has the views default and staging, whose annotations and configurations differ;
// acme/shipping (Contributor) and northwind/portal (Reader) have only their default view.
const billing = '/acme/billing/default'
const comparison = `${billing}/modeling/compare/selection`

const bulkBar = (page: Page) => page.getByRole('toolbar', { name: 'Bulk actions' })
const selectRow = (page: Page, key: string) => page.getByRole('checkbox', { name: `Select ${key}`, exact: true })
const selectAll = (page: Page) => page.getByRole('checkbox', { name: 'Select all rows', exact: true })
/** The rows that the filter shows: the first number of "13 of 124" in the filter bar. */
const shownRows = async (page: Page) => Number((await page.getByText(/^\d+ of \d+$/).textContent())!.split(' ')[0])

/** The sections of the comparison page, one per key. They are no landmarks (a hundred would bury the page's). */
const sections = (page: Page) => page.locator('section[data-key]')
const section = (page: Page, key: string) => page.locator(`section[data-key="${key}"]`)
/** The heading of a section: the type, the key, the state and the changed lines. */
const heading = (page: Page, key: string) => section(page, key).getByRole('heading', { level: 3 })
/** The CodeMirror editors of a section, each named after its document. */
const editors = (page: Page, key: string) => section(page, key).getByRole('textbox')
const title = (page: Page, name: string) => page.getByRole('heading', { level: 2, name, exact: true })
const chips = (page: Page) => page.getByRole('group', { name: 'Filter by result' }).getByRole('button')
const chip = (page: Page, name: string) =>
  page.getByRole('group', { name: 'Filter by result' }).getByRole('button', { name, exact: true })
/** Every value of a parameter of the address, in its order. */
const query = (page: Page, name: string) => new URL(page.url()).searchParams.getAll(name)

/** The comparison of the view with staging has opened: the only other view of acme/billing is preselected. */
async function expectComparison(page: Page, kind: 'annotations' | 'configurations') {
  await expect(page).toHaveURL(/\/acme\/billing\/default\/modeling\/compare\/selection\?(.*&)?to=staging(&|$)/)
  expect(query(page, 'kind')).toEqual([kind])
  await expect(page.getByRole('combobox', { name: 'Target view' })).toHaveText('staging')
}

/**
 * The section of a key that only one of the views has, in the unified layout: one editor with the document of that
 * view, every line of it added (`cm-insertedLine`, the source has it) or removed (`cm-deletedLine`, the target has it).
 * The unified merge view of CodeMirror would compare the document with the empty text of the other side, and draw a
 * removed chunk in the editor: an empty line above a new document, or the whole document above an empty line.
 */
async function expectWholeDocument(
  page: Page,
  key: string,
  view: string,
  lineClass: 'cm-insertedLine' | 'cm-deletedLine',
  text: string,
) {
  await expect(editors(page, key)).toHaveCount(1)
  const editor = section(page, key).getByRole('textbox', { name: `${key} in ${view}`, exact: true })
  await expect(editor).toContainText(text)
  await expect(editor.locator(`.cm-line.${lineClass}`)).not.toHaveCount(0)
  await expect(editor.locator(`.cm-line:not(.${lineClass})`)).toHaveCount(0)
  await expect(section(page, key).locator('.cm-deletedChunk')).toHaveCount(0)
}

/** The copy dialog, by its title: "Copy 3 configurations to another view", "Copy sbt.contoso to staging". */
const copyDialog = (page: Page, name: string) => page.getByRole('dialog', { name, exact: true })
/** The documents of a check or of a copy: the rows of the dialog's table below its header. */
const documents = (dialog: Locator, name: 'Checked documents' | 'Copied documents') =>
  dialog
    .getByRole('region', { name, exact: true })
    .getByRole('row')
    .filter({ has: dialog.page().getByRole('cell') })
/** The text of such a row: its kind, its key, its outcome and, for some, the reason. Nothing separates the cells. */
const row = (...cells: string[]) =>
  new RegExp(`^\\s*${cells.map((cell) => cell.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('\\s*')}\\s*$`)
/** The title and the counts of the alert that a check or a copy ends with. */
async function expectSummary(dialog: Locator, headline: string, counts: string | RegExp) {
  await expect(dialog.getByText(headline, { exact: true })).toBeVisible()
  await expect(dialog.getByText(counts, { exact: true })).toBeVisible()
}

/** Checks the copy of the open dialog: nothing is written, and the plan is shown. */
async function check(dialog: Locator, counts: string) {
  await dialog.getByRole('button', { name: 'Check', exact: true }).click()
  await expectSummary(dialog, 'Checked: nothing was written', counts)
}

test('selects configurations by their checkboxes, also under a filter, and remembers the selection', async ({
  page,
}, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  await openApp(page, `${billing}/configurations?q=contoso`)

  await selectRow(page, 'sbt.contoso').click()
  await expect(selectRow(page, 'sbt.contoso')).toBeChecked()

  // The Owner gets the bar, with the comparison and the copy on: the project has another view.
  const bar = bulkBar(page)
  await expect(bar.getByText('1 selected', { exact: true })).toBeVisible()
  await expect(bar.getByRole('button', { name: 'Compare with view', exact: true })).toBeEnabled()
  await expect(bar.getByRole('button', { name: 'Copy to view', exact: true })).toBeEnabled()
  await expect(selectAll(page)).toBeChecked({ indeterminate: true })
  // The click on the checkbox did not open the detail of its row.
  await expect(page.locator('[data-detail-mode]')).toHaveCount(0)
  expect(new URL(page.url()).pathname).toBe(`${billing}/configurations`)

  // The header checkbox selects the rows of the filter.
  const shown = await shownRows(page)
  expect(shown).toBeGreaterThan(1)
  await selectAll(page).click()
  await expect(selectAll(page)).toBeChecked()
  await expect(bar.getByText(`${shown} selected`, { exact: true })).toBeVisible()

  // Another filter: none of its rows is selected, and the rows it hides stay selected.
  const filter = page.getByRole('textbox', { name: 'Filter annotations' })
  await filter.fill('northwind')
  await expect(page).toHaveURL(/[?&]q=northwind(&|$)/)
  await expect(selectRow(page, 'usg.northwind.invoicing')).toBeVisible()
  await expect(selectRow(page, 'usg.northwind.invoicing')).not.toBeChecked()
  await expect(selectAll(page)).not.toBeChecked()
  await expect(bar.getByText(`${shown} selected`, { exact: true })).toBeVisible()

  // The selection is in the page memory of the view: it is there after a visit to Annotations.
  await page.getByRole('link', { name: 'Annotations', exact: true }).first().click()
  await expect(page).toHaveURL(/\/acme\/billing\/default\/annotations$/)
  await page.getByRole('link', { name: 'Configurations', exact: true }).first().click()
  await expect(page).toHaveURL(/\/acme\/billing\/default\/configurations\?(.*&)?q=northwind(&|$)/)
  await expect(bar.getByText(`${shown} selected`, { exact: true })).toBeVisible()

  // The header checkbox speaks for the shown rows: some of all of them, and every one of the first filter.
  await filter.fill('')
  await expect(page).not.toHaveURL(/[?&]q=/)
  await expect(selectAll(page)).toBeChecked({ indeterminate: true })
  await filter.fill('contoso')
  await expect(selectRow(page, 'sbt.contoso')).toBeChecked()
  await expect(selectAll(page)).toBeChecked()

  await bar.getByRole('button', { name: 'Clear selection', exact: true }).click()
  await expect(bar).toHaveCount(0)
  await expect(selectRow(page, 'sbt.contoso')).not.toBeChecked()
  await expect(selectAll(page)).not.toBeChecked()
})

test('a Reader selects configurations and gets no bulk actions', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // northwind.portal: the mock user is a Reader
  await openApp(page, '/northwind/portal/default/configurations?q=contoso')

  await selectRow(page, 'sbt.contoso').click()
  await expect(selectRow(page, 'sbt.contoso')).toBeChecked()
  await expect(bulkBar(page)).toHaveCount(0)
})

test('compares the selected annotations with the other view', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // The grid is virtualized: the filter shows the two subjects.
  await openApp(page, `${billing}/annotations?q=key:sbt.northwind,sbt.initech`)
  await selectRow(page, 'sbt.northwind').click()
  await selectRow(page, 'sbt.initech').click()
  const bar = bulkBar(page)
  await expect(bar.getByText('2 selected', { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Compare with view', exact: true }).click()

  // The page of the selection, below the Compare views tab.
  await expectComparison(page, 'annotations')
  expect(query(page, 'key')).toEqual(['sbt.initech', 'sbt.northwind'])
  await expect(title(page, '2 annotations: default → staging')).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Compare views' })).toHaveAttribute('aria-selected', 'true')

  // One section per key, in key order: staging has another sbt.northwind and no sbt.initech.
  await expect(sections(page)).toHaveCount(2)
  await expect(heading(page, 'sbt.initech')).toHaveText(/^Subject\s*sbt\.initech\s*Only in default\s*\+\d+ −0$/)
  await expect(heading(page, 'sbt.northwind')).toHaveText(/^Subject\s*sbt\.northwind\s*Different\s*\+\d+ −\d+$/)
  await expect(chips(page)).toHaveText(['All 2', 'Different 1', 'Only in default 1'])

  // The diff is drawn side by side on a wide screen: staging is the old side, default the new one.
  const northwind = section(page, 'sbt.northwind')
  await expect(editors(page, 'sbt.northwind')).toHaveCount(2)
  await expect(northwind.getByRole('textbox', { name: 'sbt.northwind in staging', exact: true })).toContainText(
    '"Title": "Northwind subject"',
  )
  await expect(northwind.getByRole('textbox', { name: 'sbt.northwind in default', exact: true })).toContainText(
    '"Title": "Northwind subject"',
  )
  await expect(northwind.locator('.cm-changedLine').first()).toBeVisible()
  // A document that only the source has is all new, beside an empty old side.
  const initech = section(page, 'sbt.initech')
  await expect(initech.getByRole('textbox', { name: 'sbt.initech in staging', exact: true })).toHaveText('')
  await expect(initech.getByRole('textbox', { name: 'sbt.initech in default', exact: true })).toContainText(
    '"Title": "Initech subject"',
  )

  // Unified is one editor per document.
  await page.getByRole('group', { name: 'Diff layout' }).getByRole('tab', { name: 'Unified', exact: true }).click()
  await expect(editors(page, 'sbt.northwind')).toHaveCount(1)
  await expect(
    northwind.getByRole('textbox', {
      name: 'sbt.northwind in default, compared with sbt.northwind in staging',
      exact: true,
    }),
  ).toContainText('"Title": "Northwind subject"')
  // The document that staging lacks is compared with nothing: its one editor is the document of default.
  await expectWholeDocument(page, 'sbt.initech', 'default', 'cm-insertedLine', '"Title": "Initech subject"')

  // The chips filter the sections, and a second click on the pressed one shows all of them again.
  const onlyDefault = chip(page, 'Only in default 1')
  await onlyDefault.click()
  await expect(onlyDefault).toHaveAttribute('aria-pressed', 'true')
  await expect(sections(page)).toHaveCount(1)
  await expect(initech).toBeVisible()
  await onlyDefault.click()
  await expect(chip(page, 'All 2')).toHaveAttribute('aria-pressed', 'true')
  await expect(sections(page)).toHaveCount(2)

  // Collapse all closes every section, and a closed section has no editor.
  await page.getByRole('button', { name: 'Collapse all', exact: true }).click()
  await expect(sections(page).getByRole('textbox')).toHaveCount(0)
  await expect(northwind.getByRole('button', { expanded: false })).toBeVisible()
  await page.getByRole('button', { name: 'Expand all', exact: true }).click()
  await expect(editors(page, 'sbt.northwind')).toHaveCount(1)

  // Swap views is the same comparison from staging, with default as the target.
  await page.getByRole('link', { name: 'Swap views', exact: true }).click()
  await expect(page).toHaveURL(
    '/acme/billing/staging/modeling/compare/selection?kind=annotations&to=default&key=sbt.initech&key=sbt.northwind',
  )
  await expect(title(page, '2 annotations: staging → default')).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Target view' })).toHaveText('default')
  await expect(heading(page, 'sbt.initech')).toHaveText(/^Subject\s*sbt\.initech\s*Only in default\s*\+0 −\d+$/)
  await expect(heading(page, 'sbt.northwind')).toHaveText(/^Subject\s*sbt\.northwind\s*Different\s*\+\d+ −\d+$/)
})

test('compares the selected configurations, own and effective', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // Its own document is the same in both views, and what it inherits is not.
  const inherited = 'uxe.northwind.notifications.prod.push'
  await openApp(page, `${billing}/configurations?q=key:sbt.contoso,usg.northwind.invoicing,${inherited}`)
  for (const key of ['sbt.contoso', 'usg.northwind.invoicing', inherited]) {
    await selectRow(page, key).click()
  }
  const bar = bulkBar(page)
  await expect(bar.getByText('3 selected', { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Compare with view', exact: true }).click()

  await expectComparison(page, 'configurations')
  expect(query(page, 'key')).toEqual(['sbt.contoso', 'usg.northwind.invoicing', inherited])
  await expect(title(page, '3 configurations: default → staging')).toBeVisible()

  // The own documents are compared first: they are what a copy would move.
  const content = page.getByRole('group', { name: 'Configuration content' })
  await expect(content.getByRole('tab', { name: 'Own', exact: true })).toHaveAttribute('aria-selected', 'true')
  await expect(
    page.getByText(
      'Only the selected keys are compared. A copy moves what is green and never deletes: a document that only ' +
        'staging has stays.',
      { exact: true },
    ),
  ).toBeVisible()
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Different\s*\+1 −1$/)
  await expect(heading(page, 'usg.northwind.invoicing')).toHaveText(/^Usage\s*usg\.northwind\.invoicing\s*Identical$/)
  await expect(heading(page, inherited)).toHaveText(
    /^Unit of execution\s*uxe\.northwind\.notifications\.prod\.push\s*Identical\s*Effective differs$/,
  )
  await expect(chips(page)).toHaveText(['All 3', 'Different 1', 'Identical 2', 'Effective differs 1'])
  const contoso = section(page, 'sbt.contoso')
  await expect(contoso.getByRole('textbox', { name: 'sbt.contoso in staging', exact: true })).toContainText(
    '"region": "eu-west"',
  )
  await expect(contoso.getByRole('textbox', { name: 'sbt.contoso in default', exact: true })).toContainText(
    '"region": "us-east"',
  )
  // An identical document starts closed.
  await expect(section(page, 'usg.northwind.invoicing').getByRole('button', { expanded: false })).toBeVisible()
  await expect(editors(page, 'usg.northwind.invoicing')).toHaveCount(0)

  // Effective compares what the two views read, and the address says so.
  await content.getByRole('tab', { name: 'Effective', exact: true }).click()
  await expect(page).toHaveURL(/[?&]content=effective(&|$)/)
  await expect(content.getByRole('tab', { name: 'Effective', exact: true })).toHaveAttribute('aria-selected', 'true')
  await expect(
    page.getByText('What staging and default read. A copy moves only own configurations.', { exact: true }),
  ).toBeVisible()
  await expect(heading(page, inherited)).toHaveText(
    /^Unit of execution\s*uxe\.northwind\.notifications\.prod\.push\s*Different\s*\+\d+ −\d+$/,
  )
  await expect(section(page, inherited).getByRole('listitem')).toHaveText(
    'The own configurations are the same. The difference is inherited (ancestors, the template, key values), and a copy does not change it.',
  )
  await expect(chips(page)).toHaveText(['All 3', 'Different 2', 'Identical 1'])
  await expect(editors(page, inherited)).toHaveCount(2)
})

test('a long selection is compared by the id of its list, also after a reload of the tab', async ({
  page,
}, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // The keys of the executions with a configuration are too long for one address.
  await openApp(page, `${billing}/configurations?q=type:exe`)
  const shown = await shownRows(page)
  expect(shown).toBeGreaterThan(40)
  expect(shown).toBeLessThanOrEqual(100)
  await selectAll(page).click()
  const bar = bulkBar(page)
  await expect(bar.getByText(`${shown} selected`, { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Compare with view', exact: true }).click()

  await expectComparison(page, 'configurations')
  expect(query(page, 'key')).toEqual([])
  expect(query(page, 'set')).toEqual([expect.stringMatching(/^[0-9a-z]+$/)])
  await expect(title(page, `${shown} configurations: default → staging`)).toBeVisible()
  await expect(sections(page)).toHaveCount(shown)
  await expect(chip(page, `All ${shown}`)).toHaveAttribute('aria-pressed', 'true')

  // The browser tab keeps the list, so a reload finds it.
  await page.reload()
  await expect(title(page, `${shown} configurations: default → staging`)).toBeVisible()
  await expect(sections(page)).toHaveCount(shown)
})

test('More actions of an annotation compares it with the other view', async ({ page }, testInfo) => {
  await openApp(page, `${billing}/annotations/sbt.northwind`)
  await page.getByRole('button', { name: 'More actions' }).click()
  await page.getByRole('menuitem', { name: 'Compare with view', exact: true }).click()

  await expectComparison(page, 'annotations')
  expect(query(page, 'key')).toEqual(['sbt.northwind'])
  await expect(title(page, '1 annotation: default → staging')).toBeVisible()
  await expect(heading(page, 'sbt.northwind')).toHaveText(/^Subject\s*sbt\.northwind\s*Different\s*\+\d+ −\d+$/)
  // Side by side from lg, one editor below it.
  await expect(editors(page, 'sbt.northwind')).toHaveCount(isMobile(testInfo) ? 1 : 2)
  await expect(editors(page, 'sbt.northwind').last()).toContainText('"Title": "Northwind subject"')
})

test('More actions of a configuration has its link, its key and the comparison with the other view', async ({
  page,
}, testInfo) => {
  await openApp(page, `${billing}/configurations/sbt.contoso`)
  await page.getByRole('button', { name: 'More actions' }).click()
  await expect(page.getByRole('menuitem', { name: 'Copy link', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Copy key', exact: true })).toBeVisible()
  await page.getByRole('menuitem', { name: 'Compare with view', exact: true }).click()

  await expectComparison(page, 'configurations')
  expect(query(page, 'key')).toEqual(['sbt.contoso'])
  await expect(title(page, '1 configuration: default → staging')).toBeVisible()
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Different\s*\+1 −1$/)
  await expect(editors(page, 'sbt.contoso')).toHaveCount(isMobile(testInfo) ? 1 : 2)
  await expect(editors(page, 'sbt.contoso').last()).toContainText('"region": "us-east"')
})

test('a document that only one of the views has is drawn whole in the unified layout', async ({ page }, testInfo) => {
  // staging has no sbt.initech, and default has no cnt.contoso.us.
  await openApp(page, `${comparison}?kind=annotations&to=staging&key=sbt.initech&key=cnt.contoso.us`)
  await expect(title(page, '2 annotations: default → staging')).toBeVisible()
  await expect(heading(page, 'cnt.contoso.us')).toHaveText(/^Context\s*cnt\.contoso\.us\s*Only in staging\s*\+0 −\d+$/)
  await expect(heading(page, 'sbt.initech')).toHaveText(/^Subject\s*sbt\.initech\s*Only in default\s*\+\d+ −0$/)
  await expect(chips(page)).toHaveText(['All 2', 'Only in default 1', 'Only in staging 1'])

  // Below lg the layout is unified as the page opens. A wide screen draws the document beside an empty side until
  // Unified is chosen.
  if (!isMobile(testInfo)) {
    await expect(editors(page, 'cnt.contoso.us')).toHaveCount(2)
    await expect(editors(page, 'sbt.initech')).toHaveCount(2)
    await page.getByRole('group', { name: 'Diff layout' }).getByRole('tab', { name: 'Unified', exact: true }).click()
  }

  // What a copy would add to staging is green, and what only staging has, and a copy leaves there, is red.
  await expectWholeDocument(page, 'sbt.initech', 'default', 'cm-insertedLine', '"Title": "Initech subject"')
  await expectWholeDocument(page, 'cnt.contoso.us', 'staging', 'cm-deletedLine', '"Title": "Us context"')
})

test('Compare with view and Copy to view are off in a project with one view', async ({ page }, testInfo) => {
  const reason = 'The project has no other view'

  /** Both items stay in More actions, disabled, with the reason below their labels. */
  async function expectBlockedItems() {
    await page.getByRole('button', { name: 'More actions' }).click()
    for (const name of [/^Compare with view/, /^Copy to view/]) {
      const item = page.getByRole('menuitem', { name })
      await expect(item).toBeDisabled()
      await expect(item).toContainText(reason)
    }
    await page.keyboard.press('Escape')
  }

  // acme.shipping: the mock user is a Contributor, and the project has only its default view
  await openApp(page, '/acme/shipping/default/annotations/sbt.northwind')
  await expectBlockedItems()
  await goto(page, '/acme/shipping/default/configurations/sbt.contoso')
  await expectBlockedItems()

  if (!isMobile(testInfo)) {
    // A bulk bar says the reason once, beside its two buttons, where a keyboard reaches it.
    for (const list of ['configurations', 'annotations']) {
      await goto(page, `/acme/shipping/default/${list}?q=key:sbt.contoso`)
      await selectRow(page, 'sbt.contoso').click()
      const bar = bulkBar(page)
      for (const name of ['Compare with view', 'Copy to view']) {
        const button = bar.getByRole('button', { name, exact: true })
        await expect(button).toBeDisabled()
        await expect(button).toHaveAccessibleDescription(reason)
      }
      await expect(bar.getByText(reason, { exact: true })).toHaveCount(1)
    }
  }
})

test('a comparison without keys says how to start one, and when a long selection is gone', async ({ page }) => {
  await openApp(page, `${comparison}?kind=annotations&to=staging`)
  await expect(title(page, 'Nothing to compare')).toBeVisible()
  await expect(
    page.getByText('Select rows on Annotations or Configurations and choose Compare with view.', { exact: true }),
  ).toBeVisible()
  await expect(sections(page)).toHaveCount(0)

  // A long selection goes by the id of a list of keys, which only the browser tab that started it keeps.
  await goto(page, `${comparison}?kind=configurations&to=staging&set=1abc2de`)
  await expect(title(page, 'Nothing to compare')).toBeVisible()
  await expect(
    page.getByText('The selection is gone. A comparison of many rows opens only in the browser tab that started it.', {
      exact: true,
    }),
  ).toBeVisible()
})

// --- the copy to another view ---

test('copies the selected configurations to the other view after a check', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // Another own document than in staging, the same one, and one whose annotation staging lacks, below a context
  // that it lacks too.
  const missing = 'exe.northwind.reporting.test'
  const keys = ['sbt.contoso', 'usg.northwind.invoicing', missing]
  await openApp(page, `${billing}/configurations?q=key:${keys.join(',')}`)
  for (const key of keys) {
    await selectRow(page, key).click()
  }
  const bar = bulkBar(page)
  await expect(bar.getByText('3 selected', { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Copy to view', exact: true }).click()

  // The only other view is preselected, a document that it has is overwritten, and a missing annotation is copied.
  const dialog = copyDialog(page, 'Copy 3 configurations to another view')
  await expect(dialog.getByText('From acme/billing, view default.', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
  const existing = dialog.getByRole('radiogroup', { name: 'When staging already has it', exact: true })
  await expect(existing.getByRole('radio', { name: 'Overwrite', exact: true })).toBeChecked()
  await expect(existing.getByRole('radio', { name: 'Skip', exact: true })).not.toBeChecked()
  await expect(dialog.getByRole('checkbox', { name: 'Copy missing annotations too', exact: true })).toBeChecked()
  await expect(dialog.getByRole('button', { name: 'Copy', exact: true })).toBeEnabled()

  // Check writes nothing and shows the plan, ancestors first: the context is created before the execution below it.
  // The plan takes the focus: the Check button went from the footer while the check ran.
  await check(dialog, 'To create 3 · To update 1 · Unchanged 1.')
  await expect(dialog.locator(':focus').getByText('Checked: nothing was written', { exact: true })).toBeVisible()
  await expect(documents(dialog, 'Checked documents')).toHaveText([
    row('Configuration', 'sbt.contoso', 'To update'),
    row('Configuration', 'usg.northwind.invoicing', 'Unchanged'),
    row('Annotation', 'cnt.northwind.test', 'To create', `Missing ancestor of ${missing}.`),
    row('Annotation', missing, 'To create', 'Needed by its configuration.'),
    row('Configuration', missing, 'To create'),
  ])
  // Nothing of a plan is in staging yet, so nothing is linked.
  await expect(dialog.getByRole('link')).toHaveCount(0)

  // The button counts what the copy writes.
  await dialog.getByRole('button', { name: 'Copy 4 to staging', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '3 created, 1 updated, 1 unchanged.')
  await expect(documents(dialog, 'Copied documents')).toHaveText([
    row('Configuration', 'sbt.contoso', 'Updated'),
    row('Configuration', 'usg.northwind.invoicing', 'Unchanged'),
    row('Annotation', 'cnt.northwind.test', 'Created', `Missing ancestor of ${missing}.`),
    row('Annotation', missing, 'Created', 'Needed by its configuration.'),
    row('Configuration', missing, 'Created'),
  ])
  // The result takes the focus, and what was written links its document in staging.
  await expect(dialog.locator(':focus').getByText('Copied to staging', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('link')).toHaveCount(4)
  await expect(dialog.getByRole('link', { name: 'sbt.contoso', exact: true })).toHaveAttribute(
    'href',
    '/acme/billing/staging/configurations/sbt.contoso',
  )
  await expect(dialog.getByRole('link', { name: 'cnt.northwind.test', exact: true })).toHaveAttribute(
    'href',
    '/acme/billing/staging/annotations/cnt.northwind.test',
  )

  // The comparison of the same keys: staging has the documents of default now.
  await dialog.getByRole('button', { name: 'Compare with staging', exact: true }).click()
  await expectComparison(page, 'configurations')
  expect(query(page, 'key')).toEqual([missing, 'sbt.contoso', 'usg.northwind.invoicing'])
  await expect(title(page, '3 configurations: default → staging')).toBeVisible()
  await expect(heading(page, missing)).toHaveText(/^Execution\s*exe\.northwind\.reporting\.test\s*Identical$/)
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Identical$/)
  await expect(heading(page, 'usg.northwind.invoicing')).toHaveText(/^Usage\s*usg\.northwind\.invoicing\s*Identical$/)
  await expect(chips(page)).toHaveText(['All 3', 'Identical 3'])

  // The copy changed no row of default: back on Configurations the selection is as it was left.
  await page.getByRole('link', { name: 'Configurations', exact: true }).first().click()
  await expect(page).toHaveURL(/\/acme\/billing\/default\/configurations\?(.*&)?q=key/)
  await expect(bar.getByText('3 selected', { exact: true })).toBeVisible()
  for (const key of keys) {
    await expect(selectRow(page, key)).toBeChecked()
  }
})

test('copies the selected annotations, skipping or overwriting what the other view has', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // staging has another sbt.northwind and no sbt.initech.
  await openApp(page, `${billing}/annotations?q=key:sbt.northwind,sbt.initech`)
  await selectRow(page, 'sbt.northwind').click()
  await selectRow(page, 'sbt.initech').click()
  const bar = bulkBar(page)
  await expect(bar.getByText('2 selected', { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Copy to view', exact: true }).click()

  // A copy of annotations has no annotations to add: the checkbox is for configurations.
  const dialog = copyDialog(page, 'Copy 2 annotations to another view')
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
  await expect(dialog.getByRole('checkbox')).toHaveCount(0)

  // Skip leaves what staging has, and creates what it lacks.
  await dialog.getByRole('radio', { name: 'Skip', exact: true }).click()
  await dialog.getByRole('button', { name: 'Copy', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '1 created, 1 skipped.')
  await expect(documents(dialog, 'Copied documents')).toHaveText([
    row('Annotation', 'sbt.initech', 'Created'),
    row('Annotation', 'sbt.northwind', 'Skipped', 'It is already in staging.'),
  ])
  await expect(dialog.getByRole('link', { name: 'sbt.initech', exact: true })).toHaveAttribute(
    'href',
    '/acme/billing/staging/annotations/sbt.initech',
  )
  await expect(dialog.getByRole('link', { name: 'sbt.northwind', exact: true })).toHaveCount(0)
  await dialog.getByRole('button', { name: 'Done', exact: true }).click()
  await expect(dialog).toHaveCount(0)

  // The selection is kept, and the dialog opens with Overwrite again: it replaces the one that was skipped.
  await expect(bar.getByText('2 selected', { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Copy to view', exact: true }).click()
  await expect(dialog.getByRole('radio', { name: 'Overwrite', exact: true })).toBeChecked()
  await dialog.getByRole('button', { name: 'Copy', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '1 updated, 1 unchanged.')
  await expect(documents(dialog, 'Copied documents')).toHaveText([
    row('Annotation', 'sbt.initech', 'Unchanged'),
    row('Annotation', 'sbt.northwind', 'Updated'),
  ])

  await dialog.getByRole('button', { name: 'Compare with staging', exact: true }).click()
  await expectComparison(page, 'annotations')
  await expect(heading(page, 'sbt.initech')).toHaveText(/^Subject\s*sbt\.initech\s*Identical$/)
  await expect(heading(page, 'sbt.northwind')).toHaveText(/^Subject\s*sbt\.northwind\s*Identical$/)
  await expect(chips(page)).toHaveText(['All 2', 'Identical 2'])
})

test('copies what differs from the comparison, and then has nothing to copy', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The copy from the comparison is the same on phones.')
  await openApp(page, `${comparison}?kind=configurations&to=staging&key=sbt.contoso&key=usg.northwind.invoicing`)
  await expect(title(page, '2 configurations: default → staging')).toBeVisible()
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Different\s*\+1 −1$/)
  await expect(heading(page, 'usg.northwind.invoicing')).toHaveText(/^Usage\s*usg\.northwind\.invoicing\s*Identical$/)

  // Only a document that a copy would change can be copied: the page counts one, and one section has the button.
  const copyOne = { name: 'Copy to staging', exact: true }
  await expect(section(page, 'sbt.contoso').getByRole('button', copyOne)).toBeVisible()
  await expect(section(page, 'usg.northwind.invoicing').getByRole('button', copyOne)).toHaveCount(0)
  await page.getByRole('button', { name: 'Copy 1 to staging', exact: true }).click()

  // The target is the compared view: it is text here, not a choice.
  const dialog = copyDialog(page, 'Copy sbt.contoso to staging')
  await expect(dialog.getByText('From acme/billing, view default.', { exact: true })).toBeVisible()
  await expect(dialog.getByText('staging', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('combobox')).toHaveCount(0)
  await dialog.getByRole('button', { name: 'Copy', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '1 updated.')
  await expect(documents(dialog, 'Copied documents')).toHaveText([row('Configuration', 'sbt.contoso', 'Updated')])
  // The comparison is the page behind the dialog. It reads staging again while the dialog shows the result, and the
  // dialog is closed once it has: until then the button that opened the dialog is on, and gets the focus back. A
  // modal hides the page behind it from the roles, so the heading of the section is found by its element.
  await expect(dialog.getByRole('button', { name: /^Compare with/ })).toHaveCount(0)
  await expect(section(page, 'sbt.contoso').locator('h3')).toHaveText(/Identical$/)
  await dialog.getByRole('button', { name: 'Done', exact: true }).click()
  await expect(dialog).toHaveCount(0)

  // The page reads staging again: the section keeps its place and stays open, and nothing is left to copy.
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Identical$/)
  await expect(section(page, 'sbt.contoso').getByRole('button', { expanded: true })).toBeVisible()
  await expect(sections(page).getByRole('button', copyOne)).toHaveCount(0)
  await expect(chips(page)).toHaveText(['All 2', 'Identical 2'])
  const copyAll = page.getByRole('button', copyOne)
  await expect(copyAll).toBeDisabled()
  await expect(copyAll).toHaveAccessibleDescription('Nothing to copy')
  await expect(page.getByText('Nothing to copy', { exact: true })).toBeVisible()
  // The button that opened the dialog is off by now, and a disabled button takes no focus: the section of the copied
  // key has it instead of the page.
  await expect(heading(page, 'sbt.contoso').getByRole('button')).toBeFocused()
})

test('copies one key from its section of the comparison', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The copy from the comparison is the same on phones.')
  const missing = 'exe.northwind.reporting.test'
  await openApp(page, `${comparison}?kind=configurations&to=staging&key=sbt.contoso&key=${missing}`)
  await expect(heading(page, missing)).toHaveText(
    /^Execution\s*exe\.northwind\.reporting\.test\s*Own configuration only in default\s*\+\d+ −0$/,
  )
  await expect(section(page, missing).getByRole('listitem')).toHaveText('The annotation is not in staging')
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Different\s*\+1 −1$/)
  await expect(page.getByRole('button', { name: 'Copy 2 to staging', exact: true })).toBeEnabled()

  // The button of a section copies its key alone, with the annotation that its configuration needs.
  await section(page, missing).getByRole('button', { name: 'Copy to staging', exact: true }).click()
  const dialog = copyDialog(page, `Copy ${missing} to staging`)
  await check(dialog, 'To create 3.')
  await expect(documents(dialog, 'Checked documents')).toHaveText([
    row('Annotation', 'cnt.northwind.test', 'To create', `Missing ancestor of ${missing}.`),
    row('Annotation', missing, 'To create', 'Needed by its configuration.'),
    row('Configuration', missing, 'To create'),
  ])
  await dialog.getByRole('button', { name: 'Copy 3 to staging', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '3 created.')
  // The dialog is closed once the page behind it has read staging again, and its section lost its button.
  await expect(section(page, missing).locator('h3')).toHaveText(/Identical$/)
  await dialog.getByRole('button', { name: 'Done', exact: true }).click()
  await expect(dialog).toHaveCount(0)

  // Its section is identical now, with nothing missing in staging; the other key is as different as it was.
  await expect(heading(page, missing)).toHaveText(/^Execution\s*exe\.northwind\.reporting\.test\s*Identical$/)
  await expect(section(page, missing).getByRole('listitem')).toHaveCount(0)
  await expect(section(page, missing).getByRole('link', { name: 'Open in staging', exact: true })).toBeVisible()
  await expect(heading(page, 'sbt.contoso')).toHaveText(/^Subject\s*sbt\.contoso\s*Different\s*\+1 −1$/)
  await expect(chips(page)).toHaveText(['All 2', 'Different 1', 'Identical 1'])
  await expect(page.getByRole('button', { name: 'Copy 1 to staging', exact: true })).toBeEnabled()
  // The button that opened the dialog went with what it copied, so the dialog cannot give the focus back to it: the
  // heading of its section has the focus instead of the page.
  await expect(section(page, missing).getByRole('button', { name: 'Copy to staging', exact: true })).toHaveCount(0)
  await expect(heading(page, missing).getByRole('button')).toBeFocused()
})

test('More actions of an annotation copies it to the other view', async ({ page }) => {
  await openApp(page, `${billing}/annotations/sbt.northwind`)
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Copy to view', exact: true }).click()

  const dialog = copyDialog(page, 'Copy sbt.northwind to another view')
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
  await check(dialog, 'To update 1.')
  await expect(documents(dialog, 'Checked documents')).toHaveText([row('Annotation', 'sbt.northwind', 'To update')])
  await dialog.getByRole('button', { name: 'Copy 1 to staging', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '1 updated.')
  await expect(documents(dialog, 'Copied documents')).toHaveText([row('Annotation', 'sbt.northwind', 'Updated')])

  // The row of what was written opens the annotation in staging, which has the content of default now: its time
  // zone, and no description.
  await dialog.getByRole('link', { name: 'sbt.northwind', exact: true }).click()
  await expect(page).toHaveURL('/acme/billing/staging/annotations/sbt.northwind')
  await expect(dialog).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue('Northwind subject')
  await expect(page.getByRole('button', { name: 'Time zone', exact: true })).toContainText('Asia/Tokyo')
  await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toHaveValue('')
})

test('More actions of a configuration copies it to the other view', async ({ page }) => {
  await openApp(page, `${billing}/configurations/sbt.contoso`)
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Copy to view', exact: true }).click()

  const dialog = copyDialog(page, 'Copy sbt.contoso to another view')
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
  await expect(dialog.getByRole('checkbox', { name: 'Copy missing annotations too', exact: true })).toBeChecked()
  await check(dialog, 'To update 1.')
  await expect(documents(dialog, 'Checked documents')).toHaveText([row('Configuration', 'sbt.contoso', 'To update')])
  await dialog.getByRole('button', { name: 'Copy 1 to staging', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '1 updated.')
  await expect(documents(dialog, 'Copied documents')).toHaveText([row('Configuration', 'sbt.contoso', 'Updated')])

  // The row of what was written opens the configuration in staging, which holds the own document of default now.
  await dialog.getByRole('link', { name: 'sbt.contoso', exact: true }).click()
  await expect(page).toHaveURL('/acme/billing/staging/configurations/sbt.contoso')
  await expect(dialog).toHaveCount(0)
  await page.getByRole('radio', { name: 'JSON', exact: true }).click()
  const own = page.getByRole('textbox', { name: 'Own configuration', exact: true })
  await expect(own).toContainText('"region": "us-east"')
  await expect(own).not.toContainText('eu-west')
})

// The item of the menu that opened the dialog is gone when the dialog closes, so the dialog has nothing to give the
// focus back to, and the page would get it: the detail hands it to the button of its menu.
for (const [detail, path, key] of [
  ['an annotation', `${billing}/annotations/sbt.northwind`, 'sbt.northwind'],
  ['a configuration', `${billing}/configurations/sbt.contoso`, 'sbt.contoso'],
] as const) {
  test(`the copy dialog of ${detail} gives the focus back to More actions`, async ({ page }) => {
    await openApp(page, path)
    const more = page.getByRole('button', { name: 'More actions', exact: true })
    const dialog = copyDialog(page, `Copy ${key} to another view`)

    for (const close of [
      () => dialog.getByRole('button', { name: 'Cancel', exact: true }).click(),
      () => page.keyboard.press('Escape'),
    ]) {
      await more.click()
      await page.getByRole('menuitem', { name: 'Copy to view', exact: true }).click()
      await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
      await close()
      await expect(dialog).toHaveCount(0)
      await expect(more).toBeFocused()
      // Escape closed the dialog only: the detail behind it is still open.
      expect(new URL(page.url()).pathname).toBe(path)
    }
  })
}

test('the copy dialog of a bulk bar gives the focus back to Copy to view', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  // The button of the bar is still there when the dialog closes, so the dialog returns the focus by itself.
  for (const list of ['annotations', 'configurations']) {
    await openApp(page, `${billing}/${list}?q=key:sbt.contoso`)
    await selectRow(page, 'sbt.contoso').click()
    const copy = bulkBar(page).getByRole('button', { name: 'Copy to view', exact: true })
    const dialog = copyDialog(page, 'Copy sbt.contoso to another view')

    for (const close of [
      () => dialog.getByRole('button', { name: 'Cancel', exact: true }).click(),
      () => page.keyboard.press('Escape'),
    ]) {
      await copy.click()
      await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
      await close()
      await expect(dialog).toHaveCount(0)
      await expect(copy).toBeFocused()
      // Escape closed the dialog only: the selection behind it is kept.
      await expect(bulkBar(page).getByText('1 selected', { exact: true })).toBeVisible()
    }
  }
})

test('the copy dialog closes when the view changes under it, and opens for the view it is in', async ({ page }) => {
  await openApp(page, `${billing}/configurations/sbt.contoso`)
  const more = page.getByRole('button', { name: 'More actions', exact: true })
  const openCopy = async () => {
    await more.click()
    await page.getByRole('menuitem', { name: 'Copy to view', exact: true }).click()
  }
  const dialog = copyDialog(page, 'Copy sbt.contoso to another view')

  await openCopy()
  await expect(dialog.getByText('From acme/billing, view default.', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
  await dialog.getByRole('button', { name: 'Copy', exact: true }).click()
  await expectSummary(dialog, 'Copied to staging', '1 updated.')

  // The link of the written row opens the configuration in staging. The detail there is the same one, with the same
  // dialog, whose result and links are those of a copy from default: it is not left open over staging.
  await dialog.getByRole('link', { name: 'sbt.contoso', exact: true }).click()
  await expect(page).toHaveURL('/acme/billing/staging/configurations/sbt.contoso')
  await expect(dialog).toHaveCount(0)

  // Opened in staging, the dialog copies from staging: nothing of the copy from default is left in it, and the only
  // other view is the production view.
  await openCopy()
  await expect(dialog.getByText('From acme/billing, view staging.', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('default')
  await expect(dialog.getByText('default is the production view', { exact: true })).toBeVisible()
  await expect(dialog.getByText('Copied to staging', { exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('region', { name: 'Copied documents', exact: true })).toHaveCount(0)
  // default has the document that it gave to staging.
  await check(dialog, 'Unchanged 1.')
  await expect(documents(dialog, 'Checked documents')).toHaveText([row('Configuration', 'sbt.contoso', 'Unchanged')])

  // The browser's Back changes the view under the open dialog, with no click in it: the plan of a copy from staging
  // does not stay over the configuration of default, and the dialog starts anew there.
  await page.goBack()
  await expect(page).toHaveURL(/\/acme\/billing\/default\/configurations\/sbt\.contoso(\?|$)/)
  await expect(dialog).toHaveCount(0)
  await openCopy()
  await expect(dialog.getByText('From acme/billing, view default.', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')
  await expect(dialog.getByText('Checked: nothing was written', { exact: true })).toHaveCount(0)
})

test('a copy to the production view needs a check first', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  await openApp(page, '/acme/billing/staging/configurations?q=key:sbt.contoso')
  await selectRow(page, 'sbt.contoso').click()
  await bulkBar(page).getByRole('button', { name: 'Copy to view', exact: true }).click()

  // The only other view is the choice, also when it is the production view; the dialog then warns.
  const dialog = copyDialog(page, 'Copy sbt.contoso to another view')
  await expect(dialog.getByText('From acme/billing, view staging.', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('default')
  await expect(dialog.getByText('prod', { exact: true })).toBeVisible()
  await expect(dialog.getByText('default is the production view', { exact: true })).toBeVisible()
  await expect(dialog.getByText('The copy changes what production reads.', { exact: true })).toBeVisible()

  // Copy is off until a check showed what it would change, and the button says why.
  const hint = 'Check first: default is the production view.'
  const copy = dialog.getByRole('button', { name: 'Copy', exact: true })
  await expect(copy).toBeDisabled()
  await expect(copy).toHaveAccessibleDescription(hint)
  await expect(dialog.getByText(hint, { exact: true })).toBeVisible()
  await check(dialog, 'To update 1.')
  await expect(documents(dialog, 'Checked documents')).toHaveText([row('Configuration', 'sbt.contoso', 'To update')])
  await expect(dialog.getByText(hint, { exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: 'Copy 1 to default', exact: true })).toBeEnabled()

  // A check holds for what it checked: another option discards the plan, and the copy is off again.
  await dialog.getByRole('radio', { name: 'Skip', exact: true }).click()
  await expect(dialog.getByText('Checked: nothing was written', { exact: true })).toHaveCount(0)
  await expect(copy).toBeDisabled()
  await expect(dialog.getByText(hint, { exact: true })).toBeVisible()
  await check(dialog, 'To skip 1.')
  await expect(documents(dialog, 'Checked documents')).toHaveText([
    row('Configuration', 'sbt.contoso', 'To skip', 'default has its own configuration.'),
  ])
  await expect(dialog.getByRole('button', { name: 'Nothing to copy', exact: true })).toBeDisabled()

  await dialog.getByRole('radio', { name: 'Overwrite', exact: true }).click()
  await check(dialog, 'To update 1.')
  await dialog.getByRole('button', { name: 'Copy 1 to default', exact: true }).click()
  await expectSummary(dialog, 'Copied to default', '1 updated.')
  await expect(dialog.getByRole('link', { name: 'sbt.contoso', exact: true })).toHaveAttribute(
    'href',
    '/acme/billing/default/configurations/sbt.contoso',
  )
  await expect(dialog.getByRole('button', { name: 'Compare with default', exact: true })).toBeVisible()
})

test('Stop ends a copy after the key that is under way, and Try again copies the rest', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  test.slow()
  // The mock answers at once, so a copy would be over before a click. page.route sees none of its requests (the
  // service worker of the mock answers them), so the fetch of the page waits instead, while the test tells it to.
  await page.addInitScript(() => {
    const send = window.fetch
    window.fetch = async (...request) => {
      if (window.slowRequests) {
        await new Promise((resolve) => setTimeout(resolve, window.slowRequests))
      }
      return send(...request)
    }
  })

  // The executions with a configuration: about fifty keys, each a few requests.
  await openApp(page, `${billing}/configurations?q=type:exe`)
  const shown = await shownRows(page)
  expect(shown).toBeGreaterThan(40)
  await selectAll(page).click()
  const bar = bulkBar(page)
  await expect(bar.getByText(`${shown} selected`, { exact: true })).toBeVisible()
  await bar.getByRole('button', { name: 'Copy to view', exact: true }).click()
  const dialog = copyDialog(page, `Copy ${shown} configurations to another view`)
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toHaveText('staging')

  await page.evaluate(() => (window.slowRequests = 100))
  await dialog.getByRole('button', { name: 'Copy', exact: true }).click()

  // While it runs the dialog stays: no Close, no Escape, no options, only Stop and the progress.
  const stop = dialog.getByRole('button', { name: 'Stop', exact: true })
  await expect(stop).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('combobox', { name: 'Copy to', exact: true })).toBeDisabled()
  await expect(dialog.getByRole('radio', { name: 'Skip', exact: true })).toBeDisabled()
  await page.keyboard.press('Escape')
  await expect(dialog.getByRole('status')).toHaveText(new RegExp(`^Copied [1-9]\\d* of ${shown}…$`))
  await expect(dialog.getByRole('progressbar')).toBeVisible()
  await stop.click()
  await page.evaluate(() => (window.slowRequests = 0))

  // The key under way is finished, and the keys after it are rows that say so.
  await expectSummary(dialog, 'The copy to staging was stopped', /^\d+ created(, \d+ updated)?, \d+ skipped\.$/)
  const rows = documents(dialog, 'Copied documents')
  const notReached = rows.filter({ hasText: 'Not copied, the copy was stopped.' })
  await expect(rows.last()).toHaveText(/^\s*Configuration\s*exe\.\S+Skipped\s*Not copied, the copy was stopped\.\s*$/)
  const left = await notReached.count()
  expect(left).toBeGreaterThan(0)
  expect(left).toBeLessThan(shown)
  await expect(dialog.getByRole('status')).toHaveText('')
  await expect(dialog.getByRole('button', { name: /^Compare with/ })).toHaveCount(0)

  // Try again is back at the options. Copying again is safe: what the stopped run copied comes back unchanged.
  await dialog.getByRole('button', { name: 'Try again', exact: true }).click()
  await expect(dialog.getByRole('radio', { name: 'Overwrite', exact: true })).toBeChecked()
  await dialog.getByRole('button', { name: 'Copy', exact: true }).click()
  await expect(dialog.getByText('Copied to staging', { exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(notReached).toHaveCount(0)
  await expect(rows.first()).toHaveText(/^\s*Configuration\s*exe\.\S+Unchanged\s*$/)
  await expect(dialog.getByRole('button', { name: 'Compare with staging', exact: true })).toBeVisible()
})
