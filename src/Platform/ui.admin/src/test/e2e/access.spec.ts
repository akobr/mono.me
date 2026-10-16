import { readFile } from 'node:fs/promises'
import { expect, test } from '@playwright/test'
import { goto, openApp } from './support'

test('invites a member to the project from its Members page', async ({ page }) => {
  await openApp(page, '/acme/billing/members')
  const invitations = page.locator('#invitations')
  await expect(invitations.getByRole('heading', { name: 'Invitations' })).toBeVisible()
  await invitations.getByRole('button', { name: 'Invite' }).click()

  const dialog = page.getByRole('dialog', { name: 'Invite people' })
  await dialog.getByPlaceholder('ada@example.com').fill('marie@example.com')
  await page.keyboard.press('Enter')
  await dialog.getByRole('button', { name: 'Send invitations' }).click()

  await expect(dialog).toBeHidden()
  await expect(invitations.getByText('marie@example.com').first()).toBeVisible()
})

test('creates a machine and downloads its certificate', async ({ page }) => {
  // The project has to enable a kind with a certificate first; API keys stay the default.
  await openApp(page, '/acme/billing/settings')
  await page.getByRole('checkbox', { name: 'Certificate', exact: true }).click()
  await expect(page.getByText('Default certificate lifetime (days)')).toBeVisible()
  await page.getByRole('button', { name: 'Save policy' }).click()
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
  // The seeded project enables only API keys, so its shared certificate is disabled.
  await openApp(page, '/acme/billing/certificates')
  await expect(page.getByText('Shared certificates are disabled')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Issue certificate' })).toBeDisabled()

  await goto(page, '/acme/billing/settings')
  await page.getByRole('checkbox', { name: 'CertificateAndApiKey', exact: true }).click()
  await page.getByRole('button', { name: 'Save policy' }).click()
  await expect(page.getByText('Machine policy saved', { exact: true })).toBeVisible()

  await goto(page, '/acme/billing/certificates')
  await expect(page.getByRole('button', { name: 'Issue certificate' })).toBeEnabled()
  await expect(page.getByText('Shared certificates are disabled')).toBeHidden()

  // A CertificateAndApiKey machine with a shared certificate gets only its API key.
  await goto(page, '/acme/billing/machines')
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
