import { expect, test, type Page } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// Names, colors and icons of the annotation types (424-13): the project's Settings, overridden by Preferences.
const templates = '/acme/billing/default/templates'

/** Types a name and leaves the field: the names are saved on change, so they can contain spaces. */
async function setName(page: Page, field: string, value: string) {
  const input = page.getByRole('textbox', { name: field, exact: true })
  await input.fill(value)
  await input.press('Tab')
}

const stExe = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--st-exe').trim())

test('type names, colors and icons come from the project settings and Preferences', async ({ page }) => {
  await openApp(page, '/acme/billing/settings')
  expect(await stExe(page)).toBe('#047857')

  await setName(page, 'Execution name', 'Job')
  await page.getByRole('combobox', { name: 'Execution color', exact: true }).click()
  await page.getByRole('option', { name: 'Red' }).click()
  await page.getByRole('button', { name: 'Execution icon', exact: true }).click()
  await page.getByPlaceholder('Search icons…').fill('Rocket')
  await page.getByRole('option', { name: 'Rocket' }).click()

  await expect(page.getByTestId('type-preview-exe')).toHaveText('Job')
  await expect(page.getByRole('textbox', { name: 'Execution plural', exact: true })).toHaveAttribute(
    'placeholder',
    'Jobs',
  )
  expect(await stExe(page)).toBe('#b91c1c')

  await goto(page, templates)
  await expect(page.getByText('Job', { exact: true })).toBeVisible()
  await expect(page.getByText('Execution', { exact: true })).toHaveCount(0)

  // A personal name wins over the project's; the project's color stays.
  await goto(page, '/account')
  await setName(page, 'Execution name', 'Run')
  await goto(page, templates)
  await expect(page.getByText('Run', { exact: true })).toBeVisible()
  expect(await stExe(page)).toBe('#b91c1c')

  await goto(page, '/account')
  await page.getByRole('button', { name: 'Reset types' }).click()
  await goto(page, templates)
  await expect(page.getByText('Job', { exact: true })).toBeVisible()

  // Back to the built-in names and colors.
  await goto(page, '/acme/billing/settings')
  await page.getByRole('button', { name: 'Reset to built-in' }).click()
  await expect(page.getByTestId('type-preview-exe')).toHaveText('Execution')
  expect(await stExe(page)).toBe('#047857')
})

// The Type column of the configurations grid shows the type names of the settings, as the annotations grid does
// (424-34); only Schemas keeps the codes. On a phone the list shows the icon, named after the type.
test('the configurations grid shows the type names of the settings', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/settings')
  await setName(page, 'Execution name', 'Job')
  await goto(page, '/acme/billing/default/configurations?q=type:exe')

  if (isMobile(testInfo)) {
    await expect(page.getByRole('img', { name: 'Job' }).first()).toBeVisible()
    return
  }
  const row = page.getByRole('row', { name: /exe\./ }).first()
  await expect(row.getByRole('cell').first()).toHaveText('Job')
  await expect(page.getByRole('cell', { name: 'exe', exact: true })).toHaveCount(0)
})

// The date format (424-23): yyyy-mm-dd until the project's Settings change it, overridden by Preferences.
test('dates follow the date format of the project settings and Preferences', async ({ page }) => {
  // The shared certificates are a section of the Machines page (424-35).
  const certificates = '/acme/billing/machines'
  const expires = page
    .locator('#shared-certificates')
    .getByRole('row')
    .filter({ hasText: 'ci-pipeline' })
    .getByRole('cell')
    .nth(3)

  async function pick(path: string, option: string | RegExp) {
    await goto(page, path)
    await page.getByRole('combobox', { name: 'Date format', exact: true }).click()
    await page.getByRole('option', { name: option }).click()
    await goto(page, certificates)
  }

  await openApp(page, certificates)
  await expect(expires).toHaveText(/^\d{4}-\d{2}-\d{2}$/)
  const [year, month, day] = (await expires.textContent())!.trim().split('-')

  await pick('/acme/billing/settings', /\(dd\.mm\.yyyy\)$/)
  await expect(expires).toHaveText(`${day}.${month}.${year}`)

  // A personal format wins over the project's.
  await pick('/account', /\(mm\/dd\/yyyy\)$/)
  await expect(expires).toHaveText(`${month}/${day}/${year}`)

  await pick('/account', 'Project default')
  await expect(expires).toHaveText(`${day}.${month}.${year}`)
})
