import { readFile } from 'node:fs/promises'
import { expect, test } from '@playwright/test'
import { goto, openApp } from './support'

test('invites a member to the project', async ({ page }) => {
  await openApp(page, '/acme/billing/invitations')
  await page.getByRole('button', { name: 'Invite' }).first().click()

  const dialog = page.getByRole('dialog', { name: 'Invite people' })
  await dialog.getByPlaceholder('ada@example.com').fill('marie@example.com')
  await page.keyboard.press('Enter')
  await dialog.getByRole('button', { name: 'Send invitations' }).click()

  await expect(dialog).toBeHidden()
  await expect(page.getByText('marie@example.com').first()).toBeVisible()
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
