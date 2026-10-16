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

// The unit tests compare the src of the logo only; this checks that the browser loads the 128 px 42 logo (424-32).
test('shows the 42 logo at the top of the navigation menu', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/default/annotations')

  const scope = isMobile(testInfo) ? await menu(page, testInfo) : page
  const logo = scope.getByRole('link', { name: '2S Admin home' }).locator('img')
  await expect(logo).toBeVisible()
  await expect.poll(() => logo.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBe(128)
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
const movedPages: { from: string; to: string; section?: string }[] = [
  { from: '/acme/billing/invitations', to: '/acme/billing/members#invitations', section: '#invitations' },
]

for (const moved of movedPages) {
  test(`opens ${moved.from} on the page that took it over`, async ({ page }) => {
    await openApp(page, moved.from)
    await expect(page).toHaveURL(moved.to)
    if (moved.section) {
      await expect(page.locator(moved.section)).toBeInViewport()
    }
  })
}
