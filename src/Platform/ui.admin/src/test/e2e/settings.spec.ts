import { expect, test, type Page } from '@playwright/test'
import { goto, isMobile, openApp } from './support'

// The description of a project (424-78): an Administrator changes it in the Project card, every member reads it.
test('an Administrator describes the project, and the overview shows the description', async ({ page }) => {
  await openApp(page, '/acme/billing/settings')
  const description = page.getByRole('textbox', { name: 'Description', exact: true })
  const save = page.getByRole('button', { name: 'Save description' })
  await expect(description).toHaveValue(/^Billing of Acme's B2B customers\./)
  await expect(save).toBeDisabled()

  await description.fill('Invoices and payments of Acme.')
  await save.click()
  await expect(page.getByText('Description saved', { exact: true })).toBeVisible()

  await goto(page, '/acme/billing/settings')
  await expect(description).toHaveValue('Invoices and payments of Acme.')
  await expect(save).toBeDisabled()
  await goto(page, '/acme')
  await expect(page.getByText('Invoices and payments of Acme.', { exact: true })).toBeVisible()

  await goto(page, '/acme/billing/settings')
  await description.fill('')
  await save.click()
  await expect(page.getByText('Description saved', { exact: true })).toBeVisible()
  await goto(page, '/acme/billing/settings')
  await expect(description).toHaveValue('')
  await expect(save).toBeDisabled()
})

test('a Reader reads the description of the project without the form', async ({ page }) => {
  await openApp(page, '/northwind/portal/settings')
  await expect(
    page.getByText("Northwind's customer portal. A small catalog of customers and their responsibilities per context."),
  ).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Save description' })).toHaveCount(0)
})

// A word longer than the card breaks over the two lines of the card, so the clamp ends with an ellipsis.
test('a description without spaces fills two lines of the project card', async ({ page }) => {
  const long = 'x'.repeat(500)
  await openApp(page, '/acme/billing/settings')
  await page.getByRole('textbox', { name: 'Description', exact: true }).fill(long)
  await page.getByRole('button', { name: 'Save description' }).click()
  await expect(page.getByText('Description saved', { exact: true })).toBeVisible()

  await goto(page, '/acme')
  const description = page.getByTitle(long, { exact: true })
  await expect(description).toBeVisible()
  const lines = await description.evaluate((element) =>
    Math.round(element.getBoundingClientRect().height / parseFloat(getComputedStyle(element).lineHeight)),
  )
  expect(lines).toBe(2)
})

// Names, colors and icons of the annotation types (424-13): the project's Settings, overridden by Preferences.
const templates = '/acme/billing/default/modeling/templates'

/** Types a name and leaves the field: the names are saved on change, so they can contain spaces. */
async function setName(page: Page, field: string, value: string) {
  const input = page.getByRole('textbox', { name: field, exact: true })
  await input.fill(value)
  await input.press('Tab')
}

const stExe = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--st-exe').trim())

/** Saves the draft of the project settings (424-266): one Save for the time zone, the date format and the types. */
async function saveSettings(page: Page) {
  await page.getByRole('button', { name: 'Save project settings' }).click()
  await expect(page.getByText('Project settings saved', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save project settings' })).toBeDisabled()
}

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
  // Only the previews show the draft; the app keeps the saved settings until Save.
  expect(await stExe(page)).toBe('#047857')
  await saveSettings(page)
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
  await saveSettings(page)
  expect(await stExe(page)).toBe('#047857')
})

// The Type column of the configurations grid shows the type names of the settings, as the annotations grid does
// (424-34); only Schemas keeps the codes. On a phone the list shows the icon, named after the type.
test('the configurations grid shows the type names of the settings', async ({ page }, testInfo) => {
  await openApp(page, '/acme/billing/settings')
  await setName(page, 'Execution name', 'Job')
  await saveSettings(page)
  await goto(page, '/acme/billing/default/configurations?q=type:exe')

  if (isMobile(testInfo)) {
    await expect(page.getByRole('img', { name: 'Job' }).first()).toBeVisible()
    return
  }
  const row = page.getByRole('row', { name: /exe\./ }).first()
  await expect(row.getByRole('cell').nth(1)).toHaveText('Job')
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
    // The project's format is saved for everyone (424-266); a personal one applies at once.
    if (path.endsWith('/settings')) {
      await saveSettings(page)
    }
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

// The project settings are stored by the API for everyone in the project (424-266): an Administrator saves them in one
// draft, everyone else sees them read-only.
test('an Administrator saves the project settings, and they stay after a reload', async ({ page }) => {
  const save = page.getByRole('button', { name: 'Save project settings' })
  await openApp(page, '/acme/billing/settings')
  await expect(save).toBeDisabled()

  await page.getByRole('button', { name: 'Default time zone' }).click()
  await page.getByPlaceholder('Search time zones').fill('Tokyo')
  await page.getByRole('option', { name: /Asia\/Tokyo/ }).click()
  await page.getByRole('combobox', { name: 'Date format', exact: true }).click()
  await page.getByRole('option', { name: /\(dd\.mm\.yyyy\)$/ }).click()
  await setName(page, 'Subject name', 'Company')
  await expect(page.getByText('Unsaved changes', { exact: true })).toBeVisible()
  await saveSettings(page)

  await page.reload()
  await page.waitForLoadState('networkidle')
  await expect(page.getByRole('button', { name: 'Default time zone' })).toContainText('Asia/Tokyo')
  await expect(page.getByRole('combobox', { name: 'Date format', exact: true })).toContainText('dd.mm.yyyy')
  await expect(page.getByRole('textbox', { name: 'Subject name', exact: true })).toHaveValue('Company')
  await expect(page.getByTestId('type-preview-sbt')).toHaveText('Company')
  await expect(save).toBeDisabled()

  // Back to the defaults, which are sent as unset.
  await page.getByRole('button', { name: 'Use the default: UTC' }).click()
  await page.getByRole('button', { name: 'Use the default: yyyy-mm-dd' }).click()
  await page.getByRole('button', { name: 'Reset to built-in' }).click()
  await saveSettings(page)
  await goto(page, '/acme/billing/settings')
  await expect(page.getByRole('button', { name: 'Default time zone' })).toContainText('UTC')
  await expect(page.getByRole('combobox', { name: 'Date format', exact: true })).toContainText('yyyy-mm-dd')
  await expect(page.getByTestId('type-preview-sbt')).toHaveText('Subject')
})

for (const [role, path, key] of [
  ['A Contributor', '/acme/shipping/settings', 'acme.shipping'],
  ['A Reader', '/northwind/portal/settings', 'northwind.portal'],
] as const) {
  test(`${role} sees the project settings read-only`, async ({ page }) => {
    await openApp(page, path)
    // An Administrator of the project set them.
    await page.evaluate(
      (project) => window.storytellerMock!.changeProjectSettings(project, { DefaultTimeZone: 'Asia/Tokyo' }),
      key,
    )
    await goto(page, path)

    for (const title of ['Annotations', 'Dates', 'Annotation types']) {
      const card = page
        .locator('[data-slot="root"]')
        .filter({ has: page.getByRole('heading', { name: title, exact: true }) })
        .last()
      await expect(card.getByText('Administrator', { exact: true })).toBeVisible()
      await expect(card.getByText('Only Administrators change the project settings.', { exact: true })).toBeVisible()
    }
    await expect(page.getByRole('button', { name: 'Default time zone' })).toContainText('Asia/Tokyo')
    await expect(page.getByRole('button', { name: 'Default time zone' })).toBeDisabled()
    await expect(page.getByRole('combobox', { name: 'Date format', exact: true })).toBeDisabled()
    await expect(page.getByRole('textbox', { name: 'Execution name', exact: true })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Save project settings' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Reset to built-in' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Use the default: UTC' })).toHaveCount(0)
  })
}

test('a change by someone else meanwhile keeps the draft and loads the new settings on request', async ({ page }) => {
  await openApp(page, '/acme/billing/settings')
  await setName(page, 'Execution name', 'Job')

  // Someone else saves the settings: the Save of the draft names the version it read and gets a 412.
  await page.evaluate(() => window.storytellerMock!.changeProjectSettings('acme.billing', { DateFormat: 'dd.mm.yyyy' }))
  await page.getByRole('button', { name: 'Save project settings' }).click()
  await expect(
    page.getByText('Someone changed the project settings meanwhile; your changes were not saved', { exact: true }),
  ).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Execution name', exact: true })).toHaveValue('Job')

  await page.getByRole('button', { name: 'Load the new settings' }).click()
  await expect(page.getByRole('textbox', { name: 'Execution name', exact: true })).toHaveValue('')
  await expect(page.getByRole('combobox', { name: 'Date format', exact: true })).toContainText('dd.mm.yyyy')
  await expect(page.getByRole('button', { name: 'Save project settings' })).toBeDisabled()
})

// The settings this browser kept before the API stored them (424-266): an Administrator saves them for the project.
test('an Administrator saves the old settings of this browser for the project', async ({ page }) => {
  await page.addInitScript(() => {
    if (!sessionStorage.getItem('2s-admin:e2e-old-settings')) {
      sessionStorage.setItem('2s-admin:e2e-old-settings', '1')
      localStorage.setItem(
        '2s-admin:project-settings',
        JSON.stringify({ 'acme.billing': { defaultTimeZone: 'Asia/Tokyo', types: { exe: { label: 'Job' } } } }),
      )
    }
  })
  await openApp(page, '/acme/billing/settings')
  const offer = page.getByText('Save them for the whole project?')
  await expect(offer).toBeVisible()

  await page.getByRole('button', { name: 'Save for the project' }).click()
  await expect(page.getByText('Project settings saved', { exact: true })).toBeVisible()
  await expect(offer).toHaveCount(0)
  expect(await page.evaluate(() => localStorage.getItem('2s-admin:project-settings'))).toBeNull()

  await goto(page, '/acme/billing/settings')
  await expect(page.getByRole('button', { name: 'Default time zone' })).toContainText('Asia/Tokyo')
  await expect(page.getByTestId('type-preview-exe')).toHaveText('Job')
  await expect(offer).toHaveCount(0)
})
