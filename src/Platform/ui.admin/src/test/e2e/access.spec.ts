import { readFile } from 'node:fs/promises'
import { expect, test } from '@playwright/test'
import { goto, openApp } from './support'

test('invites a member to the project from its Members page', async ({ page }) => {
  await openApp(page, '/acme/billing/members')
  const invitations = page.locator('#invitations')
  await expect(invitations.getByRole('heading', { name: 'Invitations' })).toBeVisible()
  // the page's main action, in the top bar (icon-only on a phone, with the same name), not above the table (424-41)
  await expect(invitations.getByRole('button', { name: 'Invite' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Invite', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Invite people' })
  await dialog.getByPlaceholder('ada@example.com').fill('marie@example.com')
  await page.keyboard.press('Enter')
  await dialog.getByRole('button', { name: 'Send invitations' }).click()

  await expect(dialog).toBeHidden()
  await expect(invitations.getByText('marie@example.com').first()).toBeVisible()
})

test('invites several people typed with spaces and commas, with the same role', async ({ page }) => {
  await openApp(page, '/acme/billing/members')
  await page.getByRole('button', { name: 'Invite', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Invite people' })
  const emails = dialog.getByPlaceholder('ada@example.com')
  // the last address is not followed by a separator: Send takes it too
  await emails.pressSequentially('Marie@example.com grace.h@example.com,ada.l@example.com')
  await expect(dialog.getByText('marie@example.com', { exact: true })).toBeVisible()
  await expect(dialog.getByText('grace.h@example.com', { exact: true })).toBeVisible()
  await expect(emails).toHaveValue('ada.l@example.com')
  await dialog.getByRole('combobox', { name: 'Role' }).click()
  await page.getByRole('option', { name: 'Contributor', exact: true }).click()
  await expect(emails).toHaveValue('ada.l@example.com')
  // near the top edge: Send does not move between the press and the release (no tag is added on blur)
  const send = dialog.getByRole('button', { name: 'Send invitations' })
  const box = (await send.boundingBox())!
  await send.click({ position: { x: box.width / 2, y: 3 } })

  await expect(dialog).toBeHidden()
  await expect(page.getByText('3 invitations sent', { exact: true })).toBeVisible()
  const invitations = page.locator('#invitations')
  for (const email of ['marie@example.com', 'grace.h@example.com', 'ada.l@example.com']) {
    const row = invitations.getByRole('row').filter({ hasText: email })
    await expect(row).toBeVisible()
    await expect(row).toContainText('Contributor')
  }
})

test('invites a pasted list to the organization from its Members page', async ({ page }) => {
  await openApp(page, '/acme/members')
  await page.getByRole('button', { name: 'Invite', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Invite people' })
  await expect(dialog).toContainText('To acme.')
  // a list copied from a mail client: the display names go, the addresses become tags
  await dialog.getByPlaceholder('ada@example.com').evaluate((input, text) => {
    const clipboardData = new DataTransfer()
    clipboardData.setData('text/plain', text)
    input.dispatchEvent(new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }))
  }, 'Marie Curie <marie@example.com>, "Hopper, Grace" <grace.h@example.com>;\nmarie@example.com')
  await expect(dialog.getByText('marie@example.com', { exact: true })).toHaveCount(1)
  await expect(dialog.getByText('grace.h@example.com', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: 'Send invitations' }).click()

  await expect(dialog).toBeHidden()
  await expect(page.getByText('2 invitations sent', { exact: true })).toBeVisible()
  const invitations = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Invitations' }) })
  await expect(invitations.getByText('marie@example.com')).toBeVisible()
  await expect(invitations.getByText('grace.h@example.com')).toBeVisible()
})

test('creates a machine and downloads its certificate', async ({ page }) => {
  // The project has to enable a kind with a certificate first; API keys stay the default.
  await openApp(page, '/acme/billing/settings')
  await page.getByRole('checkbox', { name: 'Certificate', exact: true }).click()
  await expect(page.getByText('Default certificate lifetime (days)')).toBeVisible()
  await page.getByRole('button', { name: 'Save machine policy' }).click()
  await expect(page.getByText('Machine policy saved', { exact: true })).toBeVisible()

  // The machine picks the certificate kind.
  await goto(page, '/acme/billing/machines')
  await page.getByRole('button', { name: 'New machine' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New machine access' })
  await dialog.getByRole('combobox', { name: 'Credential kind' }).click()
  await page.getByRole('option', { name: 'Certificate', exact: true }).click()
  await expect(dialog.getByText('Certificate lifetime (days)')).toBeVisible()
  await dialog.getByRole('button', { name: 'Create' }).click()

  const secret = page.getByRole('dialog').filter({ hasText: 'Shown only once' })
  await expect(secret).toBeVisible()
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    secret.getByRole('button', { name: 'Download .pfx' }).click(),
  ])
  expect(download.suggestedFilename()).toMatch(/\.pfx$/)
  expect((await readFile(await download.path())).length).toBeGreaterThan(0)

  // The credentials cannot be dismissed before they are acknowledged.
  await expect(secret.getByRole('button', { name: 'Done' })).toBeDisabled()
  await secret.getByRole('checkbox', { name: 'I have stored these credentials' }).check()
  await secret.getByRole('button', { name: 'Done' }).click()
  await expect(secret).toBeHidden()
})

test('shared certificates follow CertificateAndApiKey, and a machine can use one instead of its own', async ({
  page,
}) => {
  // The seeded project enables only API keys, so its shared certificate is disabled. The shared certificates are a
  // section of the Machines page (424-35).
  await openApp(page, '/acme/billing/machines')
  const certificates = page.locator('#shared-certificates')
  await expect(certificates.getByRole('heading', { name: 'Shared certificates' })).toBeVisible()
  await expect(certificates.getByText('Shared certificates are disabled')).toBeVisible()
  await expect(certificates.getByRole('button', { name: 'Issue certificate' })).toBeDisabled()

  await goto(page, '/acme/billing/settings')
  await page.getByRole('checkbox', { name: 'CertificateAndApiKey', exact: true }).click()
  await page.getByRole('button', { name: 'Save machine policy' }).click()
  await expect(page.getByText('Machine policy saved', { exact: true })).toBeVisible()

  await goto(page, '/acme/billing/machines')
  await expect(certificates.getByRole('button', { name: 'Issue certificate' })).toBeEnabled()
  await expect(certificates.getByText('Shared certificates are disabled')).toBeHidden()

  // A CertificateAndApiKey machine with a shared certificate gets only its API key.
  await page.getByRole('button', { name: 'New machine' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New machine access' })
  await dialog.getByRole('combobox', { name: 'Credential kind' }).click()
  await page.getByRole('option', { name: 'CertificateAndApiKey', exact: true }).click()
  await expect(dialog.getByText('Certificate lifetime (days)')).toBeVisible()
  await dialog.getByRole('checkbox', { name: 'Use a shared certificate of the project' }).check()
  await expect(dialog.getByText('Certificate lifetime (days)')).toBeHidden()
  await dialog.getByRole('button', { name: 'Create' }).click()

  const secret = page.getByRole('dialog').filter({ hasText: 'Shown only once' })
  await expect(secret.getByText('Uses a shared certificate')).toBeVisible()
  await expect(secret.getByRole('button', { name: 'Download .pfx' })).toHaveCount(0)
  await secret.getByRole('checkbox', { name: 'I have stored these credentials' }).check()
  await secret.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByText('shared', { exact: true })).toBeVisible()
})
