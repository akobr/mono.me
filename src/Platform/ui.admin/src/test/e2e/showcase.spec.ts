import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { expectAccessible, goto, isMobile, openApp } from './support'

// The showcase dataset, the default of `npm run dev:mock` (424-77): fourteen projects with their own type names, the
// five environment views, deletions to restore, and one project Ada may only read.

/** The name of a type: a cell of the grid on a desktop, the label of the type icon in the list on a phone. */
const typeLabel = (page: Page, testInfo: TestInfo, name: string | RegExp) =>
  page.getByRole(isMobile(testInfo) ? 'img' : 'gridcell', { name, exact: true }).first()

const projects = [
  'finance-cloud',
  'fitness-cloud',
  'invoicing-suite',
  'money-suite',
  'streaming-platform',
  'erp',
  'vehicle-platform',
  'plant-fleet',
  'clinical-integrations',
  'white-label-app',
  'conferences',
  'live-ops',
  'storefronts',
  'guest-platform',
]

test('lists every showcase project, and Ada as a project member where she has no organization role', async ({
  page,
}) => {
  await openApp(page, '/orgs', 'showcase')

  for (const project of projects) {
    await expect(page.getByRole('link').filter({ hasText: project })).toBeVisible()
  }
  // bluefin, harbor-industrial, meridian-health and seabreeze-hotels
  await expect(page.getByText('Project member', { exact: true })).toHaveCount(4)
})

test('shows the type names of the project', async ({ page }, testInfo) => {
  await openApp(page, '/fabrikam/invoicing-suite/default/annotations?q=type:exe', 'showcase')
  await expect(typeLabel(page, testInfo, 'Deployment')).toBeVisible()

  await goto(page, '/fabrikam/invoicing-suite/default/annotations?q=type:sbt')
  await expect(typeLabel(page, testInfo, 'Customer')).toBeVisible()

  await goto(page, '/fabrikam/fitness-cloud/default/annotations?q=type:cnt')
  await expect(typeLabel(page, testInfo, 'Member')).toBeVisible()
})

test('registers the five environment views, the default one as production', async ({ page }) => {
  await openApp(page, '/fabrikam/finance-cloud/views', 'showcase')

  const row = (view: string) =>
    page.getByRole('listitem').filter({ has: page.getByRole('link', { name: view, exact: true }) })
  for (const view of ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']) {
    await expect(row(view)).toBeVisible()
  }
  await expect(row('default').getByText('production', { exact: true })).toBeVisible()
  await expect(row('default').getByText('Production environment')).toBeVisible()
})

test('starts with deletions to restore', async ({ page }) => {
  await openApp(page, '/fabrikam/invoicing-suite/default/modeling/deleted', 'showcase')

  await expect(page.getByRole('row').filter({ hasText: 'cnt.trey-research.sandbox' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'exe.vanarsdel.portal.production' })).toBeVisible()
})

test('lets Ada only read the clinical integrations', async ({ page }, testInfo) => {
  await openApp(page, '/meridian-health/clinical-integrations/default/annotations?q=type:sbt', 'showcase')

  await expect(typeLabel(page, testInfo, 'Hospital')).toBeVisible()
  await expect(
    page.getByRole('button', { name: isMobile(testInfo) ? 'New annotation' : 'New', exact: true }),
  ).toHaveCount(0)

  await goto(page, '/meridian-health/clinical-integrations/default/modeling')
  await expect(page.getByRole('heading', { name: 'Contributors only' })).toBeVisible()
})

test('the annotations of a showcase project have no serious accessibility violations', async ({ page }, testInfo) => {
  await openApp(page, '/harbor-industrial/plant-fleet/default/annotations', 'showcase')
  await expect(typeLabel(page, testInfo, /^(Capability|Site)$/)).toBeVisible()
  await expectAccessible(page)
})
