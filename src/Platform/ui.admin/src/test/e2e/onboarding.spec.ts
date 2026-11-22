import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import { goto, isMobile, openApp, signedOut } from './support'

// The onboarding wizard of a user without an account (424-267): Welcome, Residency, Security, Exhibition and
// Organization. The step and the choice of the exhibition are in the address, so a test opens a step by it.

const exhibitionPath = '/exhibition/exhibition/default/annotations'

/** The step list of the wizard; a step is named "Step <n>: <name>" in every layout. */
const stepList = (page: Page) => page.getByRole('navigation', { name: 'Onboarding steps' })
const step = (page: Page, name: string) => stepList(page).getByRole('button', { name, exact: true })
const currentStep = (page: Page) => stepList(page).locator('[aria-current="step"]')
const stepHeading = (page: Page, name: string) => page.getByRole('heading', { name, exact: true, level: 2 })
const next = (page: Page) => page.getByRole('button', { name: 'Next', exact: true })
const back = (page: Page) => page.getByRole('button', { name: 'Back', exact: true })
const joinBox = (page: Page) => page.getByRole('checkbox', { name: 'Show me the exhibition' })
/** The heading of the residency step. */
const residency = "Your platform's data stays in the EU"
const exhibitionOnly = (page: Page) => page.getByRole('button', { name: 'Continue with the exhibition only' })

/** The step and the choice in the address: its query without the mock scenario of openApp. */
function query(page: Page): Record<string, string> {
  const url = new URL(page.url())
  url.searchParams.delete('mock-scenario')
  return Object.fromEntries(url.searchParams)
}

/** Next, and the step it opens: a second click before the first one arrived would repeat the same step. */
async function nextTo(page: Page, heading: string): Promise<void> {
  await next(page).click()
  await expect(stepHeading(page, heading)).toBeVisible()
}

/** Walks from the first step to the last with Next. */
async function walkToOrganization(page: Page): Promise<void> {
  await expect(stepHeading(page, 'Meet the 2S Platform')).toBeVisible()
  await nextTo(page, residency)
  await nextTo(page, 'Security first')
  await nextTo(page, 'Join the exhibition')
  await nextTo(page, 'Create your organization')
}

/** Checks the box of step 4. The choice goes through the address, so the box follows a moment after the click. */
async function chooseExhibition(page: Page): Promise<void> {
  await joinBox(page).click()
  await expect(joinBox(page)).toBeChecked()
}

async function fillOrganization(form: Page | Locator, organization: string, project: string): Promise<void> {
  await form.getByRole('textbox', { name: 'Organization' }).fill(organization)
  await form.getByRole('textbox', { name: 'First project' }).fill(project)
}

/** The header of an organization's card on /orgs: its link and the badge of the user's role in it. */
const organizationHeader = (page: Page, organization: string) =>
  page.getByRole('link', { name: organization, exact: true }).locator('..')

/** An annotation of the catalog: a row of the grid (desktop) or an item of the list (mobile). */
const listed = (page: Page, key: string, testInfo: TestInfo) =>
  (isMobile(testInfo) ? page.getByRole('listitem') : page.getByRole('row')).filter({ hasText: key }).first()

test('a new user is sent to sign in, creates an organization and lands in the project', async ({ page }) => {
  await signedOut(page)
  await openApp(page, '/', 'new-user')

  // A signed-out visit goes straight to the sign-in redirect (AuthKit; the mock returns through /callback), and a
  // signed-in user without an account continues to onboarding.
  await expect(page).toHaveURL(/\/onboarding/)
  expect(await page.evaluate(() => sessionStorage.getItem('2s-admin:mock-session'))).toBe('signed-in')
  await expect(page.getByRole('heading', { name: 'Welcome, Ada Lovelace', level: 1 })).toBeVisible()
  await walkToOrganization(page)
  // The exhibition was not chosen on the way: the form is the only way to end.
  await expect(exhibitionOnly(page)).toBeHidden()
  await fillOrganization(page, 'globex', 'portal')
  await page.getByRole('button', { name: 'Create and continue' }).click()

  await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations/)
  await expect(page.getByText('No annotations in this view')).toBeVisible()
  await expect(page.getByText('You joined the exhibition')).toHaveCount(0)

  // The graph of the new view (424-130): no type legend over the empty state, and the actions of Annotations.
  await goto(page, '/globex/portal/default/graph')
  const graph = page.getByRole('application')
  await expect(graph.getByRole('heading', { name: 'No annotations in this view' })).toBeVisible()
  await expect(page.getByRole('group', { name: 'Annotation types' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /^Types/ })).toHaveCount(0)
  await expect(graph.getByRole('button', { name: 'New annotation', exact: true })).toBeVisible()
  await expect(graph.getByRole('button', { name: 'Bulk create', exact: true })).toBeVisible()
})

// The description of a project (424-78): onboarding sends it trimmed, and the overview shows it on the project's card.
test('the description of the first project shows on the organization overview', async ({ page }) => {
  await openApp(page, '/onboarding', 'new-user')
  await walkToOrganization(page)
  await fillOrganization(page, 'globex', 'portal')
  await page.getByRole('textbox', { name: 'Project description' }).fill('  The customer portal of Globex.  ')
  const created = page.waitForRequest(
    (request) => request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/access/account'),
  )
  await page.getByRole('button', { name: 'Create and continue' }).click()
  expect((await created).postDataJSON()).toMatchObject({ ProjectDescription: 'The customer portal of Globex.' })
  await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations/)

  await goto(page, '/globex')
  const description = page.getByText('The customer portal of Globex.', { exact: true })
  await expect(description).toBeVisible()
  await expect(description).toHaveAttribute('title', 'The customer portal of Globex.')
})

test('Next, Back and the step list move between the steps, which the address names', async ({ page }, testInfo) => {
  await openApp(page, '/onboarding', 'new-user')
  const names = [
    'Step 1: Welcome',
    'Step 2: Residency',
    'Step 3: Security',
    'Step 4: Exhibition',
    'Step 5: Organization',
  ]
  for (const [index, name] of names.entries()) {
    await expect(stepList(page).getByRole('button').nth(index)).toHaveAccessibleName(name)
  }
  await expect(stepList(page).getByRole('button')).toHaveCount(5)
  await expect(currentStep(page)).toHaveAccessibleName('Step 1: Welcome')
  await expect(stepHeading(page, 'Meet the 2S Platform')).toBeVisible()
  // The first step has nothing to go back to; the Back of the form is in the page, hidden with its step.
  await expect(back(page)).toBeHidden()
  expect(query(page)).toEqual({})

  // On a phone only the current step shows its name: the others are their numbers, under the same accessible names.
  const shown = async (name: string) => {
    const box = await stepList(page).getByText(name, { exact: true }).boundingBox()
    return (box?.width ?? 0) > 1
  }
  expect(await shown('Welcome')).toBe(true)
  expect(await shown('Residency')).toBe(!isMobile(testInfo))

  // A change of step moves the focus to its heading, so a screen reader announces the step.
  await next(page).click()
  await expect.poll(() => query(page)).toEqual({ step: 'residency' })
  await expect(currentStep(page)).toHaveAccessibleName('Step 2: Residency')
  await expect(stepHeading(page, residency)).toBeFocused()
  expect(await shown('Residency')).toBe(true)
  expect(await shown('Welcome')).toBe(!isMobile(testInfo))

  await next(page).click()
  await expect.poll(() => query(page)).toEqual({ step: 'security' })
  await expect(currentStep(page)).toHaveAccessibleName('Step 3: Security')
  await expect(stepHeading(page, 'Security first')).toBeFocused()

  await back(page).click()
  await expect.poll(() => query(page)).toEqual({ step: 'residency' })
  await expect(stepHeading(page, residency)).toBeFocused()
  await back(page).click()
  await expect.poll(() => query(page)).toEqual({})
  await expect(stepHeading(page, 'Meet the 2S Platform')).toBeFocused()

  // Every step is reachable from the list at any time.
  await step(page, 'Step 4: Exhibition').click()
  await expect.poll(() => query(page)).toEqual({ step: 'exhibition' })
  await expect(stepHeading(page, 'Join the exhibition')).toBeFocused()
  await expect(joinBox(page)).not.toBeChecked()
  await chooseExhibition(page)
  await expect.poll(() => query(page)).toEqual({ step: 'exhibition', join: '1' })

  // The last step has the buttons of its form instead of Next, and the focus goes to the first field.
  await step(page, 'Step 5: Organization').click()
  await expect.poll(() => query(page)).toEqual({ step: 'organization', join: '1' })
  await expect(currentStep(page)).toHaveAccessibleName('Step 5: Organization')
  await expect(stepHeading(page, 'Create your organization')).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Organization' })).toBeFocused()
  await expect(next(page)).toHaveCount(0)
  await expect(page.getByText('You are joining the exhibition.')).toBeVisible()

  // Unchecking takes the second way to end away again.
  await back(page).click()
  await expect(joinBox(page)).toBeChecked()
  await joinBox(page).click()
  await expect.poll(() => query(page)).toEqual({ step: 'exhibition' })
  await nextTo(page, 'Create your organization')
  await expect(exhibitionOnly(page)).toBeHidden()
})

test('the browser history moves between the steps, and a reload keeps the step and the choice', async ({ page }) => {
  await openApp(page, '/onboarding', 'new-user')
  await nextTo(page, residency)
  await nextTo(page, 'Security first')
  await nextTo(page, 'Join the exhibition')
  await chooseExhibition(page)
  await nextTo(page, 'Create your organization')
  const organization = page.getByRole('textbox', { name: 'Organization' })
  await expect(organization).toBeFocused()
  await organization.fill('globex')

  // Back to the exhibition: the choice came with the step.
  await page.goBack()
  await expect(stepHeading(page, 'Join the exhibition')).toBeVisible()
  await expect(joinBox(page)).toBeChecked()
  expect(query(page)).toEqual({ step: 'exhibition', join: '1' })

  // The form stays in the page while another step shows, so it keeps what was typed.
  await page.goForward()
  await expect(stepHeading(page, 'Create your organization')).toBeVisible()
  await expect(organization).toHaveValue('globex')
  await expect(exhibitionOnly(page)).toBeVisible()

  await page.goBack()
  await expect(stepHeading(page, 'Join the exhibition')).toBeVisible()
  await page.goBack()
  await expect(stepHeading(page, 'Security first')).toBeVisible()
  expect(query(page)).toEqual({ step: 'security' })
  await page.goForward()
  await expect(stepHeading(page, 'Join the exhibition')).toBeVisible()

  await page.reload()
  await expect(stepHeading(page, 'Join the exhibition')).toBeVisible()
  await expect(joinBox(page)).toBeChecked()
  await expect(currentStep(page)).toHaveAccessibleName('Step 4: Exhibition')
  expect(query(page)).toEqual({ step: 'exhibition', join: '1' })
})

// Every visited step is an entry of the history. After the end the guard answers each of them with "/" and the last
// context, so a back leaves the user where they are (spec 3.1, History).
test('the back of the browser after the end never shows the wizard again', async ({ page }) => {
  await openApp(page, '/onboarding', 'new-user')
  await walkToOrganization(page)
  await fillOrganization(page, 'globex', 'portal')
  await page.getByRole('button', { name: 'Create and continue' }).click()
  await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations$/)
  await expect(page.getByText('No annotations in this view')).toBeVisible()

  // The checks after a back see the page once it has settled; this sees a wizard that shows in between, by the
  // heading it starts with.
  await page.evaluate(() => {
    new MutationObserver((records) => {
      const added = records.flatMap((record) => [...record.addedNodes])
      if (added.some((node) => node.textContent?.includes('Welcome, Ada Lovelace'))) {
        document.documentElement.dataset.wizardShown = 'true'
      }
    }).observe(document.body, { childList: true, subtree: true })
  })

  // Exhibition, Security, Residency and Welcome; the entry of the last step became the project.
  for (let entry = 0; entry < 4; entry++) {
    await page.goBack()
    await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations$/)
    await expect(page.getByText('No annotations in this view')).toBeVisible()
    await expect(stepList(page)).toHaveCount(0)
  }
  expect(await page.evaluate(() => document.documentElement.dataset.wizardShown)).toBeUndefined()
})

test('a new user joins the exhibition with an own organization and opens it as a Reader', async ({
  page,
}, testInfo) => {
  await openApp(page, '/onboarding?step=exhibition', 'new-user')
  await expect(stepHeading(page, 'Join the exhibition')).toBeVisible()
  // The story of the exhibition; the description of its project, as the API gives it, is not shown.
  await expect(
    page.getByText('Step into Fourth Wall Software, a fictional software house', { exact: false }),
  ).toBeVisible()
  await expect(
    page.getByText(
      'Explore 30 annotations, configurations that resolve live, the graph that ties them together, and the schemas and templates behind them.',
    ),
  ).toBeVisible()
  await expect(
    page.getByText(
      'The exhibition of the 2S Platform. A small catalog of customers and their responsibilities per context.',
    ),
  ).toHaveCount(0)
  await expect(joinBox(page)).not.toBeChecked()
  await expect(
    page.getByText(
      'Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.',
    ),
  ).toBeVisible()

  await chooseExhibition(page)
  await nextTo(page, 'Create your organization')
  await fillOrganization(page, 'globex', 'portal')
  // The account first: the join would create a bare one, which createAccount refuses.
  const posts: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path.includes('/access/')) {
      posts.push(path.replace(/^.*\/v1/, ''))
    }
  })
  await page.getByRole('button', { name: 'Create and continue' }).click()

  // The wizard ends in the own project; the toast is the way to the exhibition.
  await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations$/)
  await expect(page.getByText('You joined the exhibition', { exact: true })).toBeVisible()
  expect(posts).toEqual(['/access/account', '/access/exhibition/join'])
  const open = page.getByRole('link', { name: 'Open the exhibition' })
  await expect(open).toHaveAttribute('href', exhibitionPath)
  await open.click()

  // A Reader browses the catalog and creates nothing.
  await expect(page).toHaveURL(exhibitionPath)
  await expect(page.getByRole('button', { name: 'Reload the catalog' })).toBeEnabled()
  await page.getByRole('textbox', { name: 'Filter annotations' }).fill('type:sbt')
  await expect(listed(page, 'sbt.northwind', testInfo)).toBeVisible()
  for (const name of ['New', 'New annotation', 'Bulk create']) {
    await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
  }

  // The membership is the project's only: no role in the organization of the exhibition.
  await goto(page, '/orgs')
  await expect(organizationHeader(page, 'globex').getByText('Owner', { exact: true })).toBeVisible()
  await expect(organizationHeader(page, 'exhibition').getByText('Project member', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'exhibition Reader', exact: true })).toHaveAttribute(
    'href',
    exhibitionPath,
  )
})

test('a new user joins only the exhibition and creates an organization later', async ({ page }) => {
  await openApp(page, '/onboarding?step=organization', 'new-user')
  await expect(stepHeading(page, 'Create your organization')).toBeVisible()
  // The second way to end comes with the choice.
  await expect(exhibitionOnly(page)).toBeHidden()

  await step(page, 'Step 4: Exhibition').click()
  await chooseExhibition(page)
  await nextTo(page, 'Create your organization')
  await expect(page.getByText('You are joining the exhibition.')).toBeVisible()
  // The first field has the focus. Leaving it empty for the button shows no error: the error would move the button
  // under the pointer, and the press would miss it.
  await expect(page.getByRole('textbox', { name: 'Organization' })).toBeFocused()
  await exhibitionOnly(page).focus()
  await expect(page.getByText('Enter a organization name.')).toBeHidden()
  await exhibitionOnly(page).click()

  // The join created the account. The user lands in the exhibition, so the toast needs no link to it.
  await expect(page).toHaveURL(exhibitionPath)
  await expect(page.getByText('You joined the exhibition', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open the exhibition' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'User menu for Ada Lovelace' })).toBeVisible()

  // With an account, onboarding is over: "/" opens the last context.
  await goto(page, '/onboarding')
  await expect(page).toHaveURL(exhibitionPath)
  await expect(stepList(page)).toHaveCount(0)

  // The own organization comes from /orgs.
  await goto(page, '/orgs')
  await expect(organizationHeader(page, 'exhibition').getByText('Project member', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'New organization' }).click()
  const dialog = page.getByRole('dialog', { name: 'New organization' })
  await fillOrganization(dialog, 'globex', 'portal')
  await dialog.getByRole('button', { name: 'Create organization' }).click()
  await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations$/)

  await goto(page, '/orgs')
  await expect(organizationHeader(page, 'globex').getByText('Owner', { exact: true })).toBeVisible()
  await expect(organizationHeader(page, 'exhibition').getByText('Project member', { exact: true })).toBeVisible()
})

test('a deployment without an exhibition has four steps', async ({ page }) => {
  // The wizard asks for the exhibition once per page load: turn it off, then open the page again. The hooks of the
  // mock come with its start, which the first wizard has behind it once it shows its steps.
  await openApp(page, '/onboarding', 'new-user')
  await expect(stepList(page).getByRole('button')).toHaveCount(5)
  await page.evaluate(() => window.storytellerMock!.setExhibition(null))
  await goto(page, '/onboarding')

  const names = ['Step 1: Welcome', 'Step 2: Residency', 'Step 3: Security', 'Step 4: Organization']
  for (const [index, name] of names.entries()) {
    await expect(stepList(page).getByRole('button').nth(index)).toHaveAccessibleName(name)
  }
  await expect(stepList(page).getByRole('button')).toHaveCount(4)

  await nextTo(page, residency)
  await nextTo(page, 'Security first')
  await nextTo(page, 'Create your organization')
  await expect(page).toHaveURL('/onboarding?step=organization')
  await expect(exhibitionOnly(page)).toBeHidden()

  // The address of the missing step is the first step, and a choice in the address joins nothing.
  await goto(page, '/onboarding?step=exhibition')
  await expect(stepHeading(page, 'Meet the 2S Platform')).toBeVisible()
  await expect(currentStep(page)).toHaveAccessibleName('Step 1: Welcome')
  await goto(page, '/onboarding?step=organization&join=1')
  await expect(stepHeading(page, 'Create your organization')).toBeVisible()
  await expect(page.getByText('You are joining the exhibition.')).toBeHidden()
  await expect(exhibitionOnly(page)).toBeHidden()
})

test('Sign out on a step of onboarding returns to the sign-in page', async ({ page }) => {
  await openApp(page, '/onboarding?step=security', 'new-user')
  await expect(stepHeading(page, 'Security first')).toBeVisible()
  await page.getByRole('button', { name: 'Sign out' }).click()

  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { name: 'Sign in to 2S Admin' })).toBeVisible()
})

test('the sign-in page offers AuthKit after signing out', async ({ page }) => {
  await openApp(page, '/orgs')
  await page.getByRole('button', { name: /^User menu for/ }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()

  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { name: 'Sign in to 2S Admin' })).toBeVisible()
  // The bare layout loads the 128 px 42 logo above the card (424-32).
  const logo = page.locator('img[src*="42logo-128"]')
  await expect(logo).toBeVisible()
  await expect.poll(() => logo.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBe(128)
  await page.getByRole('button', { name: 'Continue with AuthKit' }).click()
  await expect(page).toHaveURL(/\/orgs|\/acme/)
})
