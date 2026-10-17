import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

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
]

for (const moved of movedPages) {
  test(`opens ${moved.from} on the page that took it over`, async ({ page }) => {
    await openApp(page, moved.from)
    await expect
      .poll(() => {
        const url = new URL(page.url())
        url.searchParams.delete('mock-scenario')
        return `${url.pathname}${url.search}${url.hash}`
      })
      .toBe(moved.to)
    if (moved.section) {
      // Keyboard and screen-reader users continue from the section, as after a followed anchor.
      await expect(page.locator(moved.section)).toBeInViewport()
      await expect(page.locator(moved.section)).toBeFocused()
    }
  })
}
