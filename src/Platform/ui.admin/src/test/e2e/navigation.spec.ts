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
