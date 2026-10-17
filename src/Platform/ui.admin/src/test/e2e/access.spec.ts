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
  await dialog.getByRole('button', { name: 'Send invitations' }).click()

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
  // The seeded project enables API keys and Federated only, so its shared certificate is disabled. The shared
  // certificates are a section of the Machines page (424-35).
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

// Workload identity federation (424-46): Administrators trust issuers on the Machines page, Contributors create
// Federated machines with rules; such a machine has no secret and no Reset.
test('an Administrator adds a trusted issuer from a template', async ({ page }) => {
  await openApp(page, '/acme/billing/machines')
  const issuers = page.locator('#trusted-issuers')
  await expect(issuers.getByRole('heading', { name: 'Trusted issuers' })).toBeVisible()
  await expect(issuers.getByRole('row').filter({ hasText: 'repository_owner_id' })).toContainText('github')

  await issuers.getByRole('button', { name: 'Add trusted issuer' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a trusted issuer' })
  const issuer = dialog.getByRole('textbox', { name: 'Issuer' })
  const lifetime = dialog.getByLabel('Max token lifetime (seconds)')

  // A template fills the issuer, the lifetime and the claims every token must carry; its placeholders block Add.
  await dialog.getByRole('combobox', { name: 'Template' }).click()
  await page.getByRole('option', { name: 'Vercel' }).click()
  await expect(issuer).toHaveValue('https://oidc.vercel.com/<team-slug>')
  await expect(dialog.getByText('Replace <team-slug> with the value of your platform.')).toBeVisible()
  await expect(lifetime).toHaveValue(/^7,?200$/)
  await expect(dialog.getByRole('textbox', { name: 'Claim 1' })).toHaveValue('owner_id')
  await expect(dialog.getByText('Enter the accepted value of owner_id.')).toBeVisible()
  await expect(dialog.getByRole('textbox', { name: 'Name' })).toHaveValue('vercel')
  await dialog.getByRole('combobox', { name: 'Template' }).click()
  await page.getByRole('option', { name: 'Azure managed identity' }).click()
  await expect(issuer).toHaveValue('https://login.microsoftonline.com/<tenant-id>/v2.0')
  await expect(lifetime).toHaveValue(/^86,?400$/)
  await expect(dialog.getByRole('textbox', { name: 'Claim 1' })).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: 'Add issuer' })).toBeDisabled()

  await dialog.getByRole('combobox', { name: 'Template' }).click()
  await page.getByRole('option', { name: 'Kubernetes' }).click()
  await expect(issuer).toHaveValue('')
  await expect(lifetime).toHaveValue('')
  await expect(dialog.getByText('oidcIssuerProfile.issuerUrl')).toBeVisible()
  await expect(dialog.getByRole('textbox', { name: 'Name' })).toHaveValue('kubernetes')
  await expect(dialog.getByRole('button', { name: 'Add issuer' })).toBeDisabled()

  await issuer.fill('https://westeurope.oic.prod-aks.azure.com/tenant/cluster/')
  await lifetime.fill('7200')
  await dialog.getByRole('button', { name: 'Add claim', exact: true }).click()
  await dialog.getByRole('textbox', { name: 'Claim 1' }).fill('/kubernetes.io/namespace')
  await dialog.getByRole('textbox', { name: 'Value 1' }).fill('billing')
  await dialog.getByRole('button', { name: 'Add issuer' }).click()

  await expect(dialog).toBeHidden()
  const row = issuers.getByRole('row').filter({ hasText: 'kubernetes' })
  await expect(row).toContainText('https://westeurope.oic.prod-aks.azure.com/tenant/cluster/')
  await expect(row).toContainText('2 h')
  await expect(row).toContainText('/kubernetes.io/namespace = billing')

  // The issuer of a trusted issuer cannot change once it is created.
  await row.getByRole('button', { name: 'Edit kubernetes' }).click()
  const edit = page.getByRole('dialog', { name: 'Edit trusted issuer kubernetes' })
  await expect(edit.getByRole('textbox', { name: 'Issuer' })).toHaveAttribute('readonly', '')
  await expect(edit.getByRole('textbox', { name: 'Name' })).toBeDisabled()
})

test('a Contributor creates a Federated machine and gets its setup instead of a secret', async ({ page }) => {
  // The user contributes to acme.shipping, which enables Federated and trusts GitHub Actions. Contributors don't see
  // the trusted issuers.
  await openApp(page, '/acme/shipping/machines')
  await expect(page.locator('#trusted-issuers')).toHaveCount(0)

  await page.getByRole('button', { name: 'New machine' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New machine access' })
  await dialog.getByRole('combobox', { name: 'Credential kind' }).click()
  await page.getByRole('option', { name: 'Federated', exact: true }).click()
  await expect(dialog.getByText('Certificate lifetime (days)')).toBeHidden()

  // The only trusted issuer is picked and the audience is the project's token endpoint.
  const rule = dialog.getByRole('group', { name: 'Rule 1' })
  await expect(rule.getByRole('combobox', { name: 'Trusted issuer' })).toContainText('github')
  await expect(rule.getByRole('textbox', { name: 'Audience' })).toHaveValue(/\/api\/v1\/acme\/shipping\/access\/token$/)
  await expect(dialog.getByRole('button', { name: 'Create' })).toBeDisabled()
  await rule.getByRole('textbox', { name: 'Subject' }).fill('repo:acme/shipping:environment:production')
  await dialog.getByRole('button', { name: 'Create' }).click()

  const setup = page.getByRole('dialog', { name: 'Federated machine created' })
  await expect(setup.getByText('This machine has no secret')).toBeVisible()
  await expect(setup.getByRole('region', { name: 'Token exchange (curl)' })).toContainText('-d client_id=mach-')
  await expect(setup.getByRole('region', { name: 'GitHub Actions step' })).toContainText('id-token: write')
  await expect(page.getByText('Shown only once')).toHaveCount(0)
  await setup.getByRole('button', { name: 'Done' }).click()
  await expect(setup).toBeHidden()

  const row = page.getByRole('row').filter({ hasText: 'Federated' })
  await expect(row).toHaveCount(1)
  await expect(row.getByRole('button', { name: 'Reset' })).toHaveCount(0)
  await expect(row.getByRole('button', { name: /^Delete / })).toBeVisible()
})

test('the federation rules of a machine are edited on its detail', async ({ page }) => {
  await openApp(page, '/acme/billing/machines')
  // The API key machine has a Reset, the Federated one has none.
  await expect(page.getByRole('row').filter({ hasText: 'mach-invoicing-worker' })).toContainText('API key')
  await expect(
    page.getByRole('row').filter({ hasText: 'mach-invoicing-worker' }).getByRole('button', { name: 'Reset' }),
  ).toBeVisible()
  const federated = page.getByRole('row').filter({ hasText: 'mach-release-pipeline' })
  await expect(federated).toContainText('Federated')
  await expect(federated.getByRole('button', { name: 'Reset' })).toHaveCount(0)

  await federated.getByRole('link', { name: 'mach-release-pipeline' }).click()
  const rules = page.getByRole('region', { name: 'Federation rules' })
  await expect(rules).toContainText('repo:acme@65/billing@74:environment:production')
  await expect(rules).toContainText('repository_id')

  await rules.getByRole('button', { name: 'Edit rules' }).click()
  const dialog = page.getByRole('dialog', { name: 'Federation rules' })
  await dialog.getByRole('textbox', { name: 'Subject' }).fill('repo:acme@65/billing@74:environment:staging')
  await dialog.getByRole('button', { name: 'Save rules' }).click()
  await expect(dialog).toBeHidden()
  await expect(rules).toContainText('repo:acme@65/billing@74:environment:staging')
  await expect(rules.getByText('Not the token endpoint')).toHaveCount(0)

  // An audience other than the token endpoint the API names (New machine behind a proxy) is flagged, and a rule added
  // later takes it over, so the machine keeps one audience.
  const proxied = 'https://admin.example.com/api/v1/acme/billing/access/token'
  await rules.getByRole('button', { name: 'Edit rules' }).click()
  await dialog.getByRole('textbox', { name: 'Audience' }).fill(proxied)
  await dialog.getByRole('button', { name: 'Save rules' }).click()
  await expect(dialog).toBeHidden()
  await expect(rules.getByText('Not the token endpoint')).toBeVisible()

  await rules.getByRole('button', { name: 'Edit rules' }).click()
  await dialog.getByRole('button', { name: 'Add rule' }).click()
  await expect(dialog.getByRole('group', { name: 'Rule 2' }).getByRole('textbox', { name: 'Audience' })).toHaveValue(
    proxied,
  )
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(dialog).toBeHidden()
})

test('an Administrator deletes a trusted issuer, and New machine then links to the section', async ({ page }) => {
  await openApp(page, '/acme/billing/machines')
  const issuers = page.locator('#trusted-issuers')
  await issuers.getByRole('button', { name: 'Delete github' }).click()
  const confirm = page.getByRole('dialog', { name: 'Delete github?' })
  await expect(confirm.getByRole('button', { name: 'Delete' })).toBeDisabled()
  await confirm.getByRole('textbox').fill('github')
  await confirm.getByRole('button', { name: 'Delete' }).click()
  await expect(confirm).toBeHidden()
  await expect(page.getByText('Trusted issuer github deleted', { exact: true })).toBeVisible()
  await expect(issuers.getByText('No trusted issuers')).toBeVisible()

  // The rules of the Federated machine still name it, and match no token.
  await page.getByRole('row').filter({ hasText: 'mach-release-pipeline' }).getByRole('link').click()
  await expect(page.getByRole('region', { name: 'Federation rules' }).getByText('issuer deleted')).toBeVisible()

  // Without trusted issuers, New machine sends the Administrator to the section.
  await goto(page, '/acme/billing/machines')
  await page.getByRole('button', { name: 'New machine' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New machine access' })
  await dialog.getByRole('combobox', { name: 'Credential kind' }).click()
  await page.getByRole('option', { name: 'Federated', exact: true }).click()
  await expect(dialog.getByText('No trusted issuers')).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Create' })).toBeDisabled()
  await dialog.getByRole('link', { name: 'Add a trusted issuer' }).press('Enter')

  await expect(page).toHaveURL(/\/acme\/billing\/machines#trusted-issuers$/)
  await expect(dialog).toHaveCount(0)
  // The closing dialog gives the focus back to New machine a moment after it is gone, unless it is told not to.
  await page.evaluate(() => new Promise((resolve) => setTimeout(resolve, 250)))
  await expect(issuers).toBeFocused()
  await expect(issuers).toBeInViewport()
  // So the keyboard continues in the section.
  await page.keyboard.press('Tab')
  await expect(issuers.getByRole('button', { name: 'Add trusted issuer' })).toBeFocused()
})
