import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp, replaceEditorText } from './support'

/** The navigation menu: the docked sidebar, or on a phone the slideover behind the menu button. */
async function menu(page: Page, testInfo: TestInfo): Promise<Locator> {
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Open navigation' }).click()
    return page.getByRole('dialog')
  }
  return page.locator('nav').filter({ has: page.getByRole('link', { name: 'Organizations' }) })
}

test('keeps the sections of the view on the pages of the project, the organization and outside', async ({
  page,
}, testInfo) => {
  await openApp(page, '/acme/billing/staging/configurations')

  for (const [link, path] of [
    ['Settings', '/acme/billing/settings'],
    ['Overview', '/acme'],
    ['Organizations', '/orgs'],
  ] as const) {
    await (await menu(page, testInfo)).getByRole('link', { name: link, exact: true }).click()
    await expect(page).toHaveURL(path)

    const navigation = await menu(page, testInfo)
    await expect(navigation.getByRole('link', { name: 'Annotations', exact: true })).toHaveAttribute(
      'href',
      '/acme/billing/staging/annotations',
    )
    await expect(navigation.getByRole('link', { name: 'Settings', exact: true })).toHaveAttribute(
      'href',
      '/acme/billing/settings',
    )
    if (isMobile(testInfo)) {
      await page.keyboard.press('Escape')
    }
  }

  await (await menu(page, testInfo)).getByRole('link', { name: 'Annotations', exact: true }).click()
  await expect(page).toHaveURL('/acme/billing/staging/annotations')
})

test('updates the sections when another view is opened', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/staging/annotations')
  await goto(page, '/acme/billing/default/graph')

  await (await menu(page, testInfo)).getByRole('link', { name: 'Views', exact: true }).click()
  await expect(page).toHaveURL('/acme/billing/views')

  await expect((await menu(page, testInfo)).getByRole('link', { name: 'Annotations', exact: true })).toHaveAttribute(
    'href',
    '/acme/billing/default/annotations',
  )
})

/** The path, the query without the mock scenario of openApp, and the hash of the current page. */
function relativeUrl(page: Page): string {
  const url = new URL(page.url())
  url.searchParams.delete('mock-scenario')
  return `${url.pathname}${url.search}${url.hash}`
}

/** The section label of the view in the navigation menu ("View · default"). */
const viewLabel = (navigation: Locator, view: string) =>
  navigation.locator('[data-slot="label"]').filter({ hasText: `View · ${view}` })

/** The view button of the switcher in the top bar (hidden on a phone). */
const viewButton = (page: Page) => page.getByRole('navigation', { name: 'Context' }).getByRole('button').nth(2)

// The default view is the production one: a "prod" badge marks it in the menu and in the switcher (424-43).
test('marks the default view as production in the menu and the switcher', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')

  const label = viewLabel(await menu(page, testInfo), 'default')
  await expect(label).toBeVisible()
  await expect(label.getByText('prod', { exact: true })).toBeVisible()

  if (!isMobile(testInfo)) {
    await expect(viewButton(page)).toHaveAccessibleName(/^default\s*prod$/)
    await expect(viewButton(page).getByText('prod', { exact: true })).toBeVisible()
  }
})

test('does not mark another view, and marks the default one in the list of views', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/staging/annotations')

  const label = viewLabel(await menu(page, testInfo), 'staging')
  await expect(label).toBeVisible()
  await expect(label.getByText('prod', { exact: true })).toHaveCount(0)
  if (isMobile(testInfo)) {
    return
  }

  await expect(viewButton(page)).toHaveAccessibleName('staging')
  await viewButton(page).click()
  await expect(page.getByRole('menuitem', { name: /^staging$/ })).toBeVisible()
  await page.getByRole('menuitem', { name: /^default\s*prod$/ }).click()

  await expect(page).toHaveURL('/acme/billing/default/annotations')
  await expect(viewButton(page)).toHaveAccessibleName(/^default\s*prod$/)
  await expect(viewLabel(await menu(page, testInfo), 'default').getByText('prod', { exact: true })).toBeVisible()
})

// The Views page lists every view, so it has the room to name the default one in full.
test('marks the default view as production on the Views page', async ({ page }) => {
  await openApp(page, '/acme/billing/views')

  const row = (view: string) =>
    page.getByRole('listitem').filter({ has: page.getByRole('link', { name: view, exact: true }) })
  await expect(row('default').getByText('production', { exact: true })).toBeVisible()
  // Only the link names the view; the badge no longer says "default".
  await expect(row('default').getByText('default', { exact: true })).toHaveCount(1)
  await expect(row('staging').getByText('production', { exact: true })).toHaveCount(0)
})

// The unit tests compare the src of the logo only; this checks that the browser loads the 128 px 42 logo (424-32).
test('shows the 42 logo at the top of the navigation menu', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')

  const scope = isMobile(testInfo) ? await menu(page, testInfo) : page
  const logo = scope.getByRole('link', { name: '2S Admin home' }).locator('img')
  await expect(logo).toBeVisible()
  await expect.poll(() => logo.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBe(128)
})

// The project section after 424-35: Invitations, Certificates, Secrets, Audit and Usage are parts of other pages.
test('lists the project pages each role can open, with Monitor and the values store', async ({ page }, testInfo) => {
  const pages = ['views', 'members', 'machines', 'settings', 'monitor']
  const moved = ['invitations', 'certificates', 'secrets', 'audit', 'usage']

  for (const [project, role] of [
    ['/acme/billing', ['views', 'members', 'machines', 'settings', 'monitor']],
    ['/acme/shipping', ['views', 'machines', 'settings']],
    ['/northwind/portal', ['views', 'settings']],
  ] as const) {
    await openApp(page, `${project}/views`)
    const navigation = await menu(page, testInfo)
    await expect(navigation.getByRole('link', { name: 'Values store', exact: true })).toHaveAttribute(
      'href',
      `${project}/default/key-values`,
    )
    for (const section of [...pages, ...moved]) {
      await expect(navigation.locator(`a[href="${project}/${section}"]`)).toHaveCount(
        (role as readonly string[]).includes(section) ? 1 : 0,
      )
    }
    if (isMobile(testInfo)) {
      await page.keyboard.press('Escape')
    }
  }
})

// The top bar has no theme button (424-35); the command palette and the account page keep the theme.
test('changes the theme in the command palette, not in the top bar', async ({ page }) => {
  await openApp(page, '/acme/billing/default/annotations')
  await expect(page.getByRole('button', { name: 'Color mode' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Search…' }).click()
  for (const mode of ['Light', 'Dark', 'System']) {
    await expect(page.getByRole('option', { name: mode, exact: true })).toBeVisible()
  }
  await page.getByRole('option', { name: 'Dark', exact: true }).click()
  await expect(page.locator('html')).toHaveClass(/\bdark\b/)

  await goto(page, '/account')
  await expect(page.getByRole('tab', { name: 'Dark' })).toHaveAttribute('aria-selected', 'true')
})

// The pages merged into others (424-35) redirect, so saved links keep working; the anchor shows the moved section.
// The redirects keep the query of the old URL, here the mock scenario of openApp.
const movedPages: { from: string; to: string; section?: string }[] = [
  { from: '/acme/billing/invitations', to: '/acme/billing/members#invitations', section: '#invitations' },
  {
    from: '/acme/billing/certificates',
    to: '/acme/billing/machines#shared-certificates',
    section: '#shared-certificates',
  },
  { from: '/acme/billing/secrets', to: '/acme/billing/settings#secrets', section: '#secrets' },
  { from: '/acme/billing/audit', to: '/acme/billing/monitor?tab=audit' },
  { from: '/acme/billing/usage', to: '/acme/billing/monitor' },
  // The pages of a view that became the tabs of Modeling (424-38), with their details and their own query.
  { from: '/acme/billing/default/schemas', to: '/acme/billing/default/modeling/schemas' },
  {
    from: '/acme/billing/default/schemas/annotation/sbt.a/type/exe?tab=versions',
    to: '/acme/billing/default/modeling/schemas/annotation/sbt.a/type/exe?tab=versions',
  },
  { from: '/acme/billing/default/templates/exe', to: '/acme/billing/default/modeling/templates/exe' },
  { from: '/acme/billing/default/export-import', to: '/acme/billing/default/modeling/export-import' },
  { from: '/acme/billing/default/deleted?cascaded=1', to: '/acme/billing/default/modeling/deleted?cascaded=1' },
  { from: '/acme/billing/default/compare?to=staging', to: '/acme/billing/default/modeling/compare?to=staging' },
]

for (const moved of movedPages) {
  test(`opens ${moved.from} on the page that took it over`, async ({ page }) => {
    await openApp(page, moved.from)
    await expect.poll(() => relativeUrl(page)).toBe(moved.to)
    if (moved.section) {
      // Keyboard and screen-reader users continue from the section, as after a followed anchor.
      await expect(page.locator(moved.section)).toBeInViewport()
      await expect(page.locator(moved.section)).toBeFocused()
    }
  })
}

// The View section after 424-38: Templates, Schemas, Export & Import, Deleted and Compare views are the tabs of
// Modeling. Every role sees the entry; a Reader sees it with the lock.
test('lists Modeling in the View section of every role, locked for a reader', async ({ page }, testInfo) => {
  const moved = ['templates', 'schemas', 'compare', 'export-import', 'deleted']

  for (const [project, locked] of [
    ['/acme/billing', false],
    ['/acme/shipping', false],
    ['/northwind/portal', true],
  ] as const) {
    await openApp(page, `${project}/default/graph`)
    const navigation = await menu(page, testInfo)
    await expect(navigation.locator(`a[href^="${project}/default/"]`)).toHaveText([
      'Annotations',
      'Configurations',
      'Values store',
      'Graph',
      'Modeling',
    ])
    const entry = navigation.getByRole('link', { name: 'Modeling', exact: true })
    await expect(entry).toHaveAttribute('href', `${project}/default/modeling`)
    // the lock badge of the entry
    await expect(entry.locator('[data-slot="linkTrailingBadge"]')).toHaveCount(locked ? 1 : 0)
    for (const section of moved) {
      await expect(navigation.locator(`a[href^="${project}/default/${section}"]`)).toHaveCount(0)
    }
    if (isMobile(testInfo)) {
      await page.keyboard.press('Escape')
    }
  }
})

const modeling = '/acme/billing/default/modeling'

/** A tab of Modeling; exact, because "Schema" is not "Schemas" and the template detail has tabs of its own. */
const modelingTab = (page: Page, name: string) => page.getByRole('tab', { name, exact: true })

// Each tab of Modeling is a page with its own path (424-38), so a tab change is pushed to the history.
test('opens the tabs of Modeling with a click, and Back returns to the previous tab', async ({ page }) => {
  // Modeling opens on its first tab.
  await openApp(page, modeling)
  await expect.poll(() => relativeUrl(page)).toBe(`${modeling}/schemas`)
  await expect(page.getByRole('tab')).toHaveText([
    'Schemas',
    'Templates',
    'Export & Import',
    'Deleted',
    'Compare views',
  ])
  await expect(modelingTab(page, 'Schemas')).toHaveAttribute('aria-selected', 'true')

  for (const [name, path] of [
    ['Templates', 'templates'],
    ['Export & Import', 'export-import'],
    ['Deleted', 'deleted'],
    ['Compare views', 'compare'],
    ['Schemas', 'schemas'],
  ] as const) {
    await modelingTab(page, name).click()
    await expect.poll(() => new URL(page.url()).pathname).toBe(`${modeling}/${path}`)
    await expect(modelingTab(page, name)).toHaveAttribute('aria-selected', 'true')
  }

  await page.goBack()
  await expect.poll(() => new URL(page.url()).pathname).toBe(`${modeling}/compare`)
  await expect(modelingTab(page, 'Compare views')).toHaveAttribute('aria-selected', 'true')
})

test('opens the tabs of Modeling from the keyboard, and the opened tab keeps the focus in view', async ({ page }) => {
  await openApp(page, `${modeling}/schemas`)
  await modelingTab(page, 'Schemas').focus()

  // The arrow keys only move the focus; Enter opens the tab. Its page brings a new header, which takes the focus.
  await page.keyboard.press('ArrowRight')
  await expect(modelingTab(page, 'Templates')).toBeFocused()
  await expect.poll(() => new URL(page.url()).pathname).toBe(`${modeling}/schemas`)
  await page.keyboard.press('Enter')
  await expect.poll(() => new URL(page.url()).pathname).toBe(`${modeling}/templates`)
  await expect(modelingTab(page, 'Templates')).toHaveAttribute('aria-selected', 'true')
  await expect(modelingTab(page, 'Templates')).toBeFocused()

  for (const [name, path] of [
    ['Export & Import', 'export-import'],
    ['Deleted', 'deleted'],
    ['Compare views', 'compare'],
  ] as const) {
    await page.keyboard.press('ArrowRight')
    await page.keyboard.press('Enter')
    await expect.poll(() => new URL(page.url()).pathname).toBe(`${modeling}/${path}`)
    await expect(modelingTab(page, name)).toBeFocused()
    // On a phone the tabs scroll sideways, and the new header shows the open one.
    await expect(modelingTab(page, name)).toBeInViewport({ ratio: 1 })
  }
})

test('a link to the last tab of Modeling shows the tab without taking the focus', async ({ page }) => {
  await openApp(page, `${modeling}/compare`)
  await expect(modelingTab(page, 'Compare views')).toHaveAttribute('aria-selected', 'true')
  // On a phone the five tabs are wider than the screen; the header scrolls the open one into view.
  await expect(modelingTab(page, 'Compare views')).toBeInViewport({ ratio: 1 })
  await expect(modelingTab(page, 'Compare views')).not.toBeFocused()
})

test('a link opens its tab of Modeling, and a detail link opens the tab and the detail', async ({ page }, testInfo) => {
  await openApp(page, `${modeling}/deleted?cascaded=1`)
  await expect(modelingTab(page, 'Deleted')).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByRole('switch', { name: 'Show cascaded' })).toBeChecked()
  // Another tab opens without the query of this one.
  await modelingTab(page, 'Templates').click()
  await expect(page).toHaveURL(`${modeling}/templates`)

  await openApp(page, `${modeling}/templates/exe`)
  const detail = page.getByRole('region', { name: 'Execution template' })
  await expect(detail).toBeVisible()
  if (isMobile(testInfo)) {
    // On a phone the detail is full screen and hides the list with the tabs; Back shows them.
    await expect(modelingTab(page, 'Templates')).toBeHidden()
    await detail.getByRole('button', { name: 'Back' }).click()
    await expect(page).toHaveURL(`${modeling}/templates`)
  }
  await expect(modelingTab(page, 'Templates')).toHaveAttribute('aria-selected', 'true')
})

// acme/billing has the views default and staging; acme/shipping has only default, so it has nothing to compare.
test('Compare views is enabled only when the project has another view', async ({ page }) => {
  await openApp(page, `${modeling}/schemas`)
  await modelingTab(page, 'Compare views').click()
  // the only other view is preselected
  await expect(page).toHaveURL(`${modeling}/compare?to=staging`)
  await expect(modelingTab(page, 'Compare views')).toHaveAttribute('aria-selected', 'true')

  await goto(page, '/acme/shipping/default/modeling/schemas')
  await expect(modelingTab(page, 'Schemas')).toBeEnabled()
  await expect(modelingTab(page, 'Compare views')).toBeDisabled()
})

// Modeling is closed below Contributor (424-38): the Reader of northwind/portal gets the lock on every tab and detail.
test('a reader gets the lock of Modeling instead of its tabs', async ({ page }) => {
  const base = '/northwind/portal/default/modeling'
  // Modeling opens on Schemas for a Reader too.
  await openApp(page, base)
  await expect.poll(() => relativeUrl(page)).toBe(`${base}/schemas`)

  for (const path of ['schemas', 'templates/exe', 'export-import', 'deleted', 'compare']) {
    if (path !== 'schemas') {
      await goto(page, `${base}/${path}`)
    }
    await expect(page.getByRole('heading', { name: 'Contributors only' })).toBeVisible()
    await expect(page.getByRole('tab')).toHaveCount(0)
    // no tab page is mounted, so neither is a detail
    await expect(page.getByRole('region', { name: 'Execution template' })).toHaveCount(0)
  }
})

// A tab change changes the path, so a dirty editor asks first (424-38).
test('a dirty template asks before another tab of Modeling opens', async ({ page }, testInfo) => {
  test.skip(isMobile(testInfo), 'On a phone the detail is full screen and hides the list with the tabs.')
  await openApp(page, `${modeling}/templates/exe`)
  await page.getByRole('radio', { name: 'JSON' }).click()
  const editor = page.locator('.cm-content').first()
  await expect(editor).toContainText('"retries"')
  await replaceEditorText(page, '{\n  "retries": 9\n}')

  const confirm = page.getByRole('dialog', { name: 'Discard changes?' })
  await modelingTab(page, 'Schemas').click()
  await confirm.getByRole('button', { name: 'Keep editing' }).click()
  await expect(confirm).toBeHidden()
  await expect.poll(() => relativeUrl(page)).toBe(`${modeling}/templates/exe`)
  await expect(modelingTab(page, 'Templates')).toHaveAttribute('aria-selected', 'true')
  await expect(editor).toContainText('"retries": 9')

  await modelingTab(page, 'Schemas').click()
  await confirm.getByRole('button', { name: 'Discard', exact: true }).click()
  await expect(page).toHaveURL(`${modeling}/schemas`)
  await expect(modelingTab(page, 'Schemas')).toHaveAttribute('aria-selected', 'true')
})
