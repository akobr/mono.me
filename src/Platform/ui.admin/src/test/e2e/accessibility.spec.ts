import { expect, test } from '@playwright/test'
import { createZip, textFile } from '../../app/mock/zip'
import { expectAccessible, openApp, openBulkCreate, signedOut } from './support'

// Accessibility of the main screens (spec section 15): no serious or critical axe violations (WCAG 2.1 AA), in the
// light and the dark theme.
const screens: [string, string][] = [
  ['organizations', '/orgs'],
  ['annotations', '/acme/billing/default/annotations'],
  ['annotation detail', '/acme/billing/default/annotations/exe.northwind.invoicing.prod'],
  ['configuration', '/acme/billing/default/configurations/exe.northwind.invoicing.prod'],
  ['templates', '/acme/billing/default/modeling/templates/exe'],
  ['schemas', '/acme/billing/default/modeling/schemas'],
  ['graph', '/acme/billing/default/graph'],
  ['export and import', '/acme/billing/default/modeling/export-import'],
  ['compare views', '/acme/billing/default/modeling/compare'],
  ['views with nothing to compare', '/acme/shipping/views'],
  ['members', '/acme/billing/members'],
  ['machines', '/acme/billing/machines'],
  ['federated machine detail', '/acme/billing/machines/mach-release-pipeline'],
  ['settings', '/acme/billing/settings'],
  ['key values', '/acme/billing/default/key-values'],
  ['key value detail', '/acme/billing/default/key-values/db.password'],
  ['secrets settings', '/acme/billing/settings#secrets'],
  ['monitor usage', '/acme/billing/monitor'],
  ['monitor audit', '/acme/billing/monitor?tab=audit'],
  ['account', '/account'],
]

for (const colorScheme of ['light', 'dark'] as const) {
  test.describe(colorScheme, () => {
    test.use({ colorScheme })

    for (const [name, path] of screens) {
      test(`${name} has no serious accessibility violations`, async ({ page }) => {
        await openApp(page, path)
        await expectAccessible(page)
      })
    }

    test('the bulk create dialog has no serious accessibility violations', async ({ page }, testInfo) => {
      await openApp(page, '/acme/billing/default/annotations')
      const dialog = await openBulkCreate(page, testInfo)
      // New, Exists and Duplicate rows, and an invalid line with its message
      await dialog
        .getByRole('textbox', { name: 'Keys' })
        .fill('northwind.billing\nexe.cyberdyne.invoicing.prod\nrst.invoicing\nusg.northwind.billing\nexe.a.b')
      await expect(dialog.getByRole('button', { name: 'Remove invalid lines' })).toBeVisible()
      await expect(dialog.getByRole('region', { name: 'Review' }).getByRole('row')).toHaveCount(6)
      await expectAccessible(page)
    })

    test('the invite dialog has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/acme/billing/members')
      await page.getByRole('button', { name: 'Invite', exact: true }).click()
      const dialog = page.getByRole('dialog', { name: 'Invite people' })
      // a valid and an invalid tag, and the error of the field
      const emails = dialog.getByPlaceholder('ada@example.com')
      await emails.pressSequentially('ada@example.com grace.example.com ')
      await expect(dialog.getByText('Not an email: grace.example.com')).toBeVisible()
      await expectAccessible(page)

      // The selected invalid tag (ArrowLeft, as before removing it) keeps its line-through but takes the tag's own
      // text color: the error color lacks contrast on the selected background in the dark theme.
      await emails.press('ArrowLeft')
      const tag = dialog.locator('[data-slot="item"]').filter({ hasText: 'grace.example.com' })
      await expect(tag).toHaveAttribute('data-state', 'active')
      const style = await tag.locator('span.line-through').evaluate((text) => ({
        decoration: getComputedStyle(text).textDecorationLine,
        color: getComputedStyle(text).color,
        tagColor: getComputedStyle(text.closest('[data-slot="item"]')!).color,
      }))
      expect(style.decoration).toBe('line-through')
      expect(style.color).toBe(style.tagColor)
      await expectAccessible(page)
    })

    // A Reader gets the lock of Modeling instead of its tabs (424-38); the lock is checked, not a loading state.
    test('the locked Modeling page of a reader has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/northwind/portal/default/modeling/export-import')
      await expect(page.getByRole('heading', { name: 'Contributors only' })).toBeVisible()
      await expect(page.getByRole('tab')).toHaveCount(0)
      await expectAccessible(page)
    })

    // A Contributor gets the lock of Administrators on Export & Import (424-66), under the tabs, none of them selected.
    test('the Export & Import lock of a contributor has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/acme/shipping/default/modeling/export-import')
      await expect(page.getByRole('heading', { name: 'Administrators only' })).toBeVisible()
      await expect(page.getByRole('tab')).toHaveCount(4)
      await expectAccessible(page)
    })

    test('onboarding has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/onboarding', 'new-user')
      await expectAccessible(page)
    })

    test('sign-in has no serious accessibility violations', async ({ page }) => {
      await signedOut(page)
      await openApp(page, '/login')
      await expectAccessible(page)
    })

    // Workload identity federation (424-46): the dialogs of trusted issuers and federated machines.
    test('the trusted issuer dialog has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/acme/billing/machines')
      await page.getByRole('button', { name: 'Add trusted issuer' }).click()
      await expect(page.getByRole('dialog', { name: 'Add a trusted issuer' }).getByText('Required claims')).toBeVisible()
      await expectAccessible(page)
    })

    test('the new machine dialog with federation rules has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/acme/billing/machines')
      await page.getByRole('button', { name: 'New machine' }).first().click()
      const dialog = page.getByRole('dialog', { name: 'New machine access' })
      await dialog.getByRole('combobox', { name: 'Credential kind' }).click()
      await page.getByRole('option', { name: 'Federated', exact: true }).click()
      await expect(dialog.getByRole('group', { name: 'Rule 1' })).toBeVisible()
      await expectAccessible(page)
    })

    // The Composition view of the Effective tab (424-36), with a step selected and its paths open; the mock composes
    // flat, so it has depth-1 nodes only.
    test('the composition of a configuration has no serious accessibility violations', async ({ page }) => {
      await openApp(
        page,
        '/acme/billing/default/configurations/exe.northwind.invoicing.prod/effective?show=composition',
      )
      // the first marker is the hidden spacer of the gutter
      await expect(page.locator('.cm-composition-marker').last()).toBeVisible()
      const template = page.locator('li[data-step]').filter({ hasText: 'Template · Execution' })
      await template.getByRole('button', { name: /^Show the lines of step/ }).click()
      await template.locator('summary').click()
      await expect(page.locator('.cm-line.cm-composition-selected').first()).toBeVisible()
      await expectAccessible(page)
    })

    // The management scopes of a new machine (424-57): picked by an Owner, disabled for a Contributor.
    for (const [role, path] of [
      ['an Owner', '/acme/billing/machines'],
      ['a Contributor', '/acme/shipping/machines'],
    ] as const) {
      test(`the new machine dialog of ${role} has no serious accessibility violations`, async ({ page }) => {
        await openApp(page, path)
        await page.getByRole('button', { name: 'New machine' }).first().click()
        const transfer = page
          .getByRole('dialog', { name: 'New machine access' })
          .getByRole('checkbox', { name: 'Configuration.Transfer' })
        if (role === 'an Owner') {
          await transfer.check()
        } else {
          await expect(transfer).toBeDisabled()
        }
        await expectAccessible(page)
      })
    }

    // New configuration (424-42).
    test('the new configuration dialog has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/acme/billing/default/configurations')
      await page.getByRole('button', { name: 'New configuration' }).first().click()
      await page.getByRole('dialog').waitFor()
      await expectAccessible(page)
    })

    for (const [name, button, title] of [
      ['federation rules', 'Edit rules', 'Federation rules'],
      ['federated setup', 'Setup', 'Set up mach-release-pipeline'],
    ] as const) {
      test(`the ${name} dialog has no serious accessibility violations`, async ({ page }) => {
        await openApp(page, '/acme/billing/machines/mach-release-pipeline')
        await page.getByRole('button', { name: button, exact: true }).click()
        await expect(page.getByRole('dialog', { name: title })).toBeVisible()
        await expectAccessible(page)
      })
    }

    test('the result of an import has no serious accessibility violations', async ({ page }) => {
      await openApp(page, '/acme/billing/default/modeling/export-import')
      // an update, a create with a warning, a skip and an ignored file
      const archive = createZip([
        textFile('annotations/rst.invoicing.json', '{"Title":"Edited"}'),
        textFile('annotations/exe.newco.billing.prod.json', '{}'),
        textFile('configurations/rst.no-such-annotation.conf.json', '{"a":1}'),
        textFile('notes.txt', 'ignored'),
      ])
      const panel = page.getByRole('region', { name: 'Import', exact: true })
      await panel.locator('input[type="file"]').setInputFiles({
        name: 'edited.zip',
        mimeType: 'application/zip',
        buffer: Buffer.from(archive),
      })
      await panel.getByRole('button', { name: 'Check', exact: true }).click()
      await expect(page.getByText('Checked: nothing was written')).toBeVisible()
      await expectAccessible(page)
    })
  })
}
