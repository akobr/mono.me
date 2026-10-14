import { expect, test } from '@playwright/test'
import { openApp, signedOut } from './support'

test('a new user is sent to sign in, creates an organization and lands in the project', async ({ page }) => {
  await signedOut(page)
  await openApp(page, '/', 'new-user')

  // A signed-out visit goes straight to the sign-in redirect (AuthKit; the mock returns through /callback), and a
  // signed-in user without an account continues to onboarding.
  await expect(page).toHaveURL(/\/onboarding/)
  expect(await page.evaluate(() => sessionStorage.getItem('2s-admin:mock-session'))).toBe('signed-in')
  await page.getByRole('textbox', { name: 'Organization' }).fill('globex')
  await page.getByRole('textbox', { name: 'First project' }).fill('portal')
  await page.getByRole('button', { name: 'Create and continue' }).click()

  await expect(page).toHaveURL(/\/globex\/portal\/default\/annotations/)
  await expect(page.getByText('No annotations in this view')).toBeVisible()
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
