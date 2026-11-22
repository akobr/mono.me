import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// Joining the exhibition after onboarding, on Organizations (424-271), in the default dataset: Ada has an account and is
// no member of the exhibition exhibition/exhibition of Grace Hopper.

const exhibitionPath = '/exhibition/exhibition/default/annotations'

/** The offer of the exhibition on /orgs: its card, under the heading "The exhibition". */
const offer = (page: Page) => page.locator('[data-exhibition-offer]')
const joinButton = (page: Page) => page.getByRole('button', { name: 'Join the exhibition', exact: true })

/** The header of an organization's card on /orgs: its link and the badge of the user's role in it. */
const organizationHeader = (page: Page, organization: string) =>
  page.getByRole('link', { name: organization, exact: true }).locator('..')

/** An annotation of the catalog: a row of the grid (desktop) or an item of the list (mobile). */
const listed = (page: Page, key: string, testInfo: TestInfo) =>
  (isMobile(testInfo) ? page.getByRole('listitem') : page.getByRole('row')).filter({ hasText: key }).first()

/** The POSTs to the access routes from now on, without the API's base. */
function recordPosts(page: Page): string[] {
  const posts: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path.includes('/access/')) {
      posts.push(path.replace(/^.*\/v1/, ''))
    }
  })
  return posts
}

test('a user with an account joins the exhibition from the organizations page and opens it as a Reader', async ({
  page,
}, testInfo) => {
  await openApp(page, '/orgs')
  await expect(offer(page).getByRole('heading', { name: 'The exhibition', exact: true, level: 2 })).toBeVisible()
  await expect(offer(page).getByText('Step into Fourth Wall Software, a fictional software house')).toBeVisible()
  await expect(
    offer(page).getByText(
      'Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.',
    ),
  ).toBeVisible()
  // Ada's organizations come first; the exhibition is not one of them yet.
  await expect(page.getByRole('link', { name: 'acme', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'exhibition', exact: true })).toHaveCount(0)

  const posts = recordPosts(page)
  await joinButton(page).click()

  await expect(page).toHaveURL(exhibitionPath)
  await expect(page.getByText('You joined the exhibition', { exact: true })).toBeVisible()
  expect(posts).toEqual(['/access/exhibition/join'])
  // A Reader browses the catalog and creates nothing.
  await expect(page.getByRole('button', { name: 'Reload the catalog' })).toBeEnabled()
  await page.getByRole('textbox', { name: 'Filter annotations' }).fill('type:sbt')
  await expect(listed(page, 'sbt.northwind', testInfo)).toBeVisible()
  for (const name of ['New', 'New annotation', 'Bulk create']) {
    await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
  }

  // The membership is the project's only, and the offer is gone.
  await goto(page, '/orgs')
  await expect(organizationHeader(page, 'exhibition').getByText('Project member', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'exhibition Reader', exact: true })).toHaveAttribute(
    'href',
    exhibitionPath,
  )
  await expect(organizationHeader(page, 'acme').getByText('Owner', { exact: true })).toBeVisible()
  await expect(joinButton(page)).toHaveCount(0)
})

test('the organizations page offers no exhibition to its member, or where the deployment has none', async ({
  page,
}) => {
  // Organizations asks for the exhibition once per page load: turn it off, then open the page again.
  await openApp(page, '/orgs')
  await expect(joinButton(page)).toBeVisible()
  await page.evaluate(() => window.storytellerMock!.setExhibition(null))
  await goto(page, '/orgs')
  await expect(page.getByRole('link', { name: 'acme', exact: true })).toBeVisible()
  await expect(offer(page)).toHaveCount(0)

  await page.evaluate(() =>
    window.storytellerMock!.setExhibition({ Organization: 'exhibition', Project: 'exhibition' }),
  )
  await goto(page, '/orgs')
  await expect(joinButton(page)).toBeVisible()

  // A member: joined behind the page's back, as in another tab.
  await page.evaluate(async () => {
    const response = await fetch('/api/v1/access/exhibition/join', { method: 'POST' })
    if (!response.ok) {
      throw new Error(`The join answered ${response.status}.`)
    }
  })
  await goto(page, '/orgs')
  await expect(page.getByRole('link', { name: 'exhibition Reader', exact: true })).toBeVisible()
  await expect(offer(page)).toHaveCount(0)
})

test('the showcase has no exhibition to offer', async ({ page }) => {
  await openApp(page, '/orgs', 'showcase')
  await expect(page.getByRole('heading', { name: 'Organizations', exact: true, level: 1 })).toBeVisible()
  await expect(page.getByText('Project member', { exact: true }).first()).toBeVisible()
  await expect(offer(page)).toHaveCount(0)
})
