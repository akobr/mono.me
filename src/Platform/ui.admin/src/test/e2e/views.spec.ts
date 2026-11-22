import { expect, test, type Page } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// The comparison of a selection of annotations or configurations with another view of the project (424-309, spec
// "Copy and Compare Annotations and Configurations Between Views"). In the default dataset acme/billing (Owner) has
// the views default and staging, whose annotations and configurations differ; acme/shipping (Contributor) and
// northwind/portal (Reader) have only their default view.
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

test('selects configurations by their checkboxes, also under a filter, and remembers the selection', async ({
  page,
}, testInfo) => {
  test.skip(isMobile(testInfo), 'The list below md has no checkboxes.')
  await openApp(page, `${billing}/configurations?q=contoso`)

  await selectRow(page, 'sbt.contoso').click()
  await expect(selectRow(page, 'sbt.contoso')).toBeChecked()

  // The Owner gets the bar, with the comparison on: the project has another view.
  const bar = bulkBar(page)
  await expect(bar.getByText('1 selected', { exact: true })).toBeVisible()
  await expect(bar.getByRole('button', { name: 'Compare with view', exact: true })).toBeEnabled()
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
  await expect(editors(page, 'sbt.initech')).toHaveCount(1)

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
  await expect(page.getByText('Only the selected keys are compared.', { exact: true })).toBeVisible()
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
  await expect(page.getByText('What staging and default read.', { exact: true })).toBeVisible()
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

test('Compare with view is off in a project with one view', async ({ page }, testInfo) => {
  /** The item stays in More actions, disabled, with the reason below its label. */
  async function expectBlockedItem() {
    await page.getByRole('button', { name: 'More actions' }).click()
    const item = page.getByRole('menuitem', { name: /^Compare with view/ })
    await expect(item).toBeDisabled()
    await expect(item).toContainText('The project has no other view')
    await page.keyboard.press('Escape')
  }

  // acme.shipping: the mock user is a Contributor, and the project has only its default view
  await openApp(page, '/acme/shipping/default/annotations/sbt.northwind')
  await expectBlockedItem()
  await goto(page, '/acme/shipping/default/configurations/sbt.contoso')
  await expectBlockedItem()

  if (!isMobile(testInfo)) {
    // The bulk bar says the reason beside its button, where a keyboard reaches it.
    await goto(page, '/acme/shipping/default/configurations?q=contoso')
    await selectRow(page, 'sbt.contoso').click()
    const bar = bulkBar(page)
    await expect(bar.getByRole('button', { name: 'Compare with view', exact: true })).toBeDisabled()
    await expect(bar.getByText('The project has no other view', { exact: true })).toBeVisible()
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
