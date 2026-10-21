import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getResolvedConfiguration } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { effectiveContent } from '@/mock/configuration-handlers'
import { saveDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// ledgerline/erp follows docs/42for.net/platform/examples/versioned-monolith.md and example 6 of the Miro board.

const showcase = showcaseOf('ledgerline', 'erp')

/** The board's frame of the staging view, the whole view. */
const stagingFrame = [
  'sbt.v2025',
  'rst.billing',
  'rst.reporting',
  'cnt.v2025.contoso',
  'usg.v2025.billing',
  'usg.v2025.reporting',
  'exe.v2025.billing.contoso',
  'exe.v2025.reporting.contoso',
]

describe('ledgerline/erp', () => {
  it('has the catalog of the documentation and of the board', () => {
    const keys = showcase.keys('default')
    for (const key of [
      'sbt.v2025',
      'sbt.v2026',
      'rst.auth',
      'rst.billing',
      'rst.reporting',
      'cnt.v2025.production',
      'cnt.v2025.contoso',
      'cnt.v2026.production',
      'usg.v2025.auth',
      'usg.v2026.auth',
      'usg.v2025.billing',
      'usg.v2026.billing',
      'usg.v2026.reporting',
      'exe.v2025.auth.production',
      'exe.v2026.auth.production',
      'exe.v2025.billing.production',
      'exe.v2025.billing.contoso',
      'exe.v2026.billing.production',
      'exe.v2026.reporting.production',
      'unt.billing.month-end-close',
      'uxe.v2026.billing.production.month-end-close',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    // Reporting exists only in v2026: no other version has a build of it, so the old lines do not contain the module.
    expect([...keys].filter((key) => /^usg\.[^.]+\.reporting$/.test(key))).toEqual(['usg.v2026.reporting'])
    expect(
      [...keys].filter((key) => /^exe\.[^.]+\.reporting\./.test(key)).every((key) => key.startsWith('exe.v2026.')),
    ).toBe(true)
    // Contoso's pin serves billing only.
    expect([...keys].filter((key) => /^exe\.v2025\.[^.]+\.contoso$/.test(key))).toEqual(['exe.v2025.billing.contoso'])
  })

  it('extends the catalog with more modules, the pinned customers and the schema of the LTS channels', () => {
    const annotations = showcase.annotations('default')
    for (const module of ['inventory', 'hr', 'crm', 'document-archive', 'audit-log', 'warehouse']) {
      expect(annotations.has(`rst.${module}`), module).toBe(true)
    }
    for (const pin of ['cnt.v2025.woodgrove', 'cnt.v2026.alpine']) {
      expect(annotations.get(pin)?.Labels, pin).toContain('pin')
    }
    expect(showcase.trash('default').has('cnt.v2024.northwind')).toBe(true)
    expect(showcase.latest('default', 'schema:descendant:sbt.v2025:cnt')?.required).toEqual([
      'endpoint',
      'maintenanceWindow',
    ])
  })

  it('stores the documented values on the module, the versions, the builds, the channel and the rollout', () => {
    expect(showcase.own('default', 'rst.billing')).toMatchObject({ vat: true, creditNote: true })
    expect(showcase.own('default', 'sbt.v2025')).toMatchObject({ schemaVersion: 14 })
    expect(showcase.own('default', 'sbt.v2026')).toMatchObject({ schemaVersion: 18 })
    expect(showcase.own('default', 'usg.v2025.billing')).toEqual({ creditNote: false })
    expect(showcase.own('default', 'usg.v2026.billing')).toEqual({ creditNote: true })
    expect(showcase.own('default', 'cnt.v2025.contoso')).toMatchObject({
      maintenanceWindow: 'sun 01:00-03:00 Europe/Oslo',
      endpoint: 'https://contoso.v2025.erp.ledgerline.example',
    })
    // The stored document of the Contoso rollout contains only the extra.
    expect(showcase.own('default', 'exe.v2025.billing.contoso')).toEqual({ legacyExport: true })
    expect(showcase.versions('default', 'rst.billing')).toBeGreaterThanOrEqual(15)
  })

  it('keeps Contoso’s flag on its one rollout and the credit notes out of the v2025 line', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    expect(effectiveContent(db, scope, 'exe.v2025.billing.contoso')).toMatchObject({
      vat: true,
      creditNote: false,
      legacyExport: true,
      schemaVersion: 14,
      maintenanceWindow: 'sun 01:00-03:00 Europe/Oslo',
    })
    // The production rollout of v2025 stores nothing, so the Contoso document is the whole difference.
    expect(showcase.own('default', 'exe.v2025.billing.production')).toBeUndefined()
    expect(effectiveContent(db, scope, 'exe.v2025.billing.production')).toMatchObject({
      creditNote: false,
      legacyExport: false,
    })
    // v2026 production never sees the flag: Contoso is a context of a different subject.
    expect(effectiveContent(db, scope, 'exe.v2026.billing.production')).toMatchObject({
      creditNote: true,
      legacyExport: false,
      schemaVersion: 18,
    })
  })

  it('turns inherited features off with scalars on the customer rollouts', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    // The bank allows SAML only: the providers are flags, because an array would be unioned with the module's.
    expect(effectiveContent(db, scope, 'exe.v2025.auth.woodgrove')).toMatchObject({
      mfa: 'all',
      sso: { saml: true, oidc: false },
      ipAllowList: ['198.51.100.0/24'],
    })
    expect(effectiveContent(db, scope, 'exe.v2026.auth.production')).toMatchObject({ sso: { saml: true, oidc: true } })
    // The US channels charge sales tax instead of VAT.
    for (const key of ['exe.v2025.billing.litware', 'exe.v2026.billing.fourth-coffee']) {
      expect(effectiveContent(db, scope, key), key).toMatchObject({
        vat: false,
        salesTax: 'avalara',
        invoiceCurrency: 'USD',
      })
    }
    expect(effectiveContent(db, scope, 'exe.v2025.billing.woodgrove')).toMatchObject({ invoiceCurrency: 'GBP' })
    expect(effectiveContent(db, scope, 'exe.v2026.billing.alpine')).toMatchObject({ invoiceCurrency: 'CHF', vat: true })
    expect(effectiveContent(db, scope, 'exe.v2025.billing.production')).toMatchObject({ invoiceCurrency: 'NOK' })
    // Each country's payroll goes to its own authority in its own format.
    expect(effectiveContent(db, scope, 'uxe.v2026.hr.production.payroll-export')).toMatchObject({
      target: 'altinn',
      format: 'a-melding',
    })
    expect(effectiveContent(db, scope, 'uxe.v2025.hr.margies-travel.payroll-export')).toMatchObject({
      payrollCountry: 'DK',
      target: 'eindkomst',
      format: 'eindkomst-xml',
    })
    expect(effectiveContent(db, scope, 'uxe.v2026.hr.adventure-works.payroll-export')).toMatchObject({
      payrollCountry: 'FI',
      target: 'incomes-register',
      format: 'tulorekisteri-xml',
    })
  })

  it('labels the lines, ends the support of v2025 and freezes the Contoso rollout', () => {
    const annotations = showcase.annotations('default')
    const lts = annotations.get('sbt.v2025')!
    expect(lts.Labels).toContain('lts')
    expect(Date.parse(lts.ExpiresAt!)).toBeGreaterThan(Date.now())
    expect(annotationStatus(lts)).toBe('active')
    expect(annotations.get('sbt.v2026')?.Labels).toContain('current')
    expect(annotationStatus(annotations.get('exe.v2025.billing.contoso')!)).toBe('disabled')
    // v2024 is out of support but stays in the catalog; the v2027 preview starts later.
    expect(annotationStatus(annotations.get('sbt.v2024')!)).toBe('expired')
    expect(annotationStatus(annotations.get('cnt.v2024.production')!)).toBe('expired')
    expect(annotationStatus(annotations.get('sbt.v2027')!)).toBe('scheduled')
    expect(annotationStatus(annotations.get('exe.v2027.billing.preview')!)).toBe('scheduled')
  })

  it('requires the schema version on every version and gives every rollout the maintenance defaults', () => {
    expect(showcase.latest('default', 'schema:type:sbt')).toMatchObject({ required: ['schemaVersion'] })
    expect(showcase.latest('default', 'schema:descendant:rst.billing:usg')).toMatchObject({
      additionalProperties: { type: 'boolean' },
    })
    expect(showcase.latest('default', 'template:exe')).toMatchObject({
      backupBeforeMigration: true,
      releaseTag: '@annotation("/releaseTag", "Subject")',
    })
    // The template's binding reads the release tag of the version, so every version of every view has one.
    for (const view of ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']) {
      for (const [key, annotation] of showcase.annotations(view)) {
        if (key.startsWith('sbt.')) {
          expect(annotation.Values?.releaseTag, `${view} ${key}`).toEqual(expect.any(String))
        }
      }
    }
  })

  it('runs the month-end close of billing on v2026 production against the channel’s endpoint', () => {
    expect(showcase.annotations('default').get('unt.billing.month-end-close')).toMatchObject({
      UnitType: 'time',
      UnitDefinition: '30 1 1 * *',
    })
    const run = 'uxe.v2026.billing.production.month-end-close'
    expect(showcase.own('default', run)).toMatchObject({ timeZone: 'Europe/Oslo', ledgerApi: '@config("/endpoint")' })
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), run)).toMatchObject({
      endpoint: 'https://v2026.erp.ledgerline.example',
      lockPeriod: true,
    })
  })

  it('deletes the lifted pin of Northwind with its rollouts, and a rollout of Litware alone', () => {
    const trash = showcase.trash('default')
    expect(trash.get('cnt.v2024.northwind')).toEqual(
      expect.arrayContaining(['exe.v2024.billing.northwind', 'uxe.v2024.billing.northwind.month-end-close']),
    )
    expect(trash.get('cnt.v2024.northwind')?.at(-1)).toBe('cnt.v2024.northwind')
    expect(trash.get('exe.v2025.hr.litware')).toEqual(['exe.v2025.hr.litware'])
    // The version and its production channel remain until the version itself is retired.
    expect(showcase.keys('default').has('sbt.v2024')).toBe(true)
    expect(showcase.keys('default').has('cnt.v2024.production')).toBe(true)
  })

  it('edits no document after its annotation was deleted or its line went out of support', () => {
    const scope = showcaseScope('ledgerline', 'erp', 'default')!
    const lastEdit = (key: string) =>
      Date.parse(scope.documents.get(`configuration:${key}` as never)?.versions.at(-1)?.Modified.At ?? '')
    for (const tree of scope.trash) {
      for (const { AnnotationKey: key } of tree.annotations) {
        if (scope.documents.has(`configuration:${key}` as never)) {
          expect(lastEdit(key), key).toBeLessThan(Date.parse(tree.deletedAt))
        }
      }
    }
    const endOfSupport = Date.parse(showcase.annotations('default').get('sbt.v2024')!.ExpiresAt!)
    for (const key of showcase.keys('default')) {
      if (/^[a-z]{3}\.v2024(\.|$)/.test(key) && scope.documents.has(`configuration:${key}` as never)) {
        expect(lastEdit(key), key).toBeLessThan(endOfSupport)
      }
    }
  })

  it('follows the board in staging: the reporting backport on Contoso’s v2025 channel', () => {
    const keys = showcase.keys('staging')
    expect([...keys].toSorted()).toEqual(stagingFrame.toSorted())
    // Not in production: there, v2025 has no reporting.
    expect(showcase.keys('default').has('usg.v2025.reporting')).toBe(false)
    expect(showcase.keys('default').has('exe.v2025.reporting.contoso')).toBe(false)
    expect(showcase.annotations('staging').get('usg.v2025.reporting')?.Labels).toEqual(['backport'])
    expect(showcase.own('staging', 'exe.v2025.billing.contoso')).toEqual({ legacyExport: true })
    expect(annotationStatus(showcase.annotations('staging').get('exe.v2025.billing.contoso')!)).toBe('active')
  })

  it('brings e-invoicing and schema 19 in the release candidate, on only where the module is rolled out', () => {
    expect(showcase.own('release-candidate', 'rst.billing')).toMatchObject({ vat: true, eInvoice: false })
    expect(showcase.own('release-candidate', 'usg.v2025.billing')).toEqual({ creditNote: false })
    expect(showcase.own('release-candidate', 'exe.v2026.billing.production')).toEqual({ instances: 6, eInvoice: true })
    expect(showcase.versions('release-candidate', 'exe.v2026.billing.production')).toBe(2)
    expect(showcase.own('release-candidate', 'sbt.v2026')).toMatchObject({ schemaVersion: 19 })
    expect(showcase.latest('release-candidate', 'schema:type:sbt')).toMatchObject({
      required: ['schemaVersion', 'runtime'],
    })
    const db = unseededShowcaseDb()
    const scope = showcase.scope('release-candidate')
    for (const key of ['exe.v2026.billing.production', 'exe.v2026.billing.early-access']) {
      expect(effectiveContent(db, scope, key), key).toMatchObject({ eInvoice: true })
    }
    // Alpine is pinned to 2026.2.4, which has no e-invoicing, and the other customers do not have the module.
    for (const key of ['exe.v2026.billing.alpine', 'exe.v2026.billing.tailspin', 'exe.v2025.billing.contoso']) {
      expect(effectiveContent(db, scope, key), key).toMatchObject({ eInvoice: false })
    }
    const keys = showcase.keys('release-candidate')
    expect(keys.has('exe.v2026.e-invoicing.production')).toBe(true)
    expect(keys.has('exe.v2027.e-invoicing.preview')).toBe(true)
    expect(keys.has('exe.v2026.e-invoicing.alpine')).toBe(false)
    expect(keys.has('usg.v2025.e-invoicing')).toBe(false)
    expect(showcase.keys('default').has('rst.e-invoicing')).toBe(false)
    // A copy of production as it is now: the lifted pin is gone.
    expect(keys.has('cnt.v2024.northwind')).toBe(false)
    expect(keys.has('exe.v2025.billing.contoso')).toBe(true)
  })

  it('keeps development on developer channels and brings the bank statement import in feature-delivery1', () => {
    const development = showcase.annotations('development')
    expect([...development.keys()].filter((key) => key.startsWith('cnt.'))).toEqual([
      'cnt.v2026.dev',
      'cnt.v2026.qa',
      'cnt.v2027.dev',
    ])
    expect(showcase.own('development', 'cnt.v2026.dev')).toMatchObject({
      logLevel: 'debug',
      bankApi: 'https://sandbox.bank.example/v1',
    })
    // v2027 is built from main there, a nightly ahead of the published preview.
    expect(annotationStatus(development.get('sbt.v2027')!)).toBe('active')
    expect(development.get('sbt.v2027')).toMatchObject({
      Labels: ['next'],
      Values: { branch: 'main', releaseTag: '2027.0.0-preview.3.dev.14' },
    })

    const feature = showcase.annotations('feature-delivery1')
    expect(feature.get('unt.billing.bank-statement-import')).toMatchObject({ UnitType: 'file' })
    expect(feature.has('uxe.v2026.billing.tailspin.bank-statement-import')).toBe(true)
    expect(showcase.keys('default').has('unt.billing.bank-statement-import')).toBe(false)
    // The board's job run keeps its document next to the new feature.
    expect(showcase.own('feature-delivery1', 'uxe.v2026.billing.production.month-end-close')).toMatchObject({
      ledgerApi: '@config("/endpoint")',
    })
  })

  describe('through the mock API', () => {
    const server = setupServer(...mockHandlers)
    beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
    // The test setup clears the session after every test, so each test stores the project's database again.
    beforeEach(() => saveDb({ ...unseededShowcaseDb(), account: { AccessMap: { 'ledgerline.erp': 'Contributor' } } }))
    afterEach(() => server.resetHandlers())
    afterAll(() => server.close())

    const resolved = async (view: string, key: string) =>
      (await getResolvedConfiguration('ledgerline', 'erp', view, key)).Content

    it('resolves the release tags of the versions, the pinned build and the channel’s endpoint', async () => {
      expect(await resolved('default', 'exe.v2026.billing.production')).toMatchObject({ releaseTag: '2026.3.1' })
      expect(await resolved('default', 'exe.v2026.inventory.alpine')).toMatchObject({ releaseTag: '2026.2.4' })
      expect(await resolved('default', 'exe.v2026.billing.alpine')).toMatchObject({ releaseTag: '2026.2.4' })
      expect(await resolved('default', 'uxe.v2026.billing.production.month-end-close')).toMatchObject({
        ledgerApi: 'https://v2026.erp.ledgerline.example',
      })
      expect(await resolved('default', 'exe.v2025.audit-log.woodgrove')).toMatchObject({
        notify: 'erp-change@woodgrove.example',
      })
      expect(await resolved('release-candidate', 'exe.v2026.billing.production')).toMatchObject({
        releaseTag: '2026.4.0-rc.1',
      })
      expect(await resolved('development', 'exe.v2027.billing.dev')).toMatchObject({
        releaseTag: '2027.0.0-preview.3.dev.14',
      })
      // Secrets need the vault, so the mock returns them as written.
      expect(await resolved('default', 'rst.auth')).toMatchObject({
        signingKey: '@(erp.auth.jwt-signing-key, primaryVault)',
      })
    })

    it('resolves every configuration of every view', async () => {
      for (const view of ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']) {
        const keys = [...showcaseScope('ledgerline', 'erp', view)!.documents.keys()]
          .filter((id) => id.startsWith('configuration:'))
          .map((id) => id.slice('configuration:'.length))
          .filter((key) => showcase.keys(view).has(key))
        for (const key of keys) {
          await expect(resolved(view, key), `${view} ${key}`).resolves.toBeDefined()
        }
      }
    })
  })
})
