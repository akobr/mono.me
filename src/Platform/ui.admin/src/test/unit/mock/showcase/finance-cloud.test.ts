import { describe, expect, it } from 'vitest'
import type { AuditStamp } from '@42for.net/2splatform.sdk'
import { effectiveContent } from '@/mock/configuration-handlers'
import { showcaseScope } from '@/mock/showcase'
import { people } from '@/mock/showcase/people'
import { environmentViewNames } from '@/mock/showcase/views'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// fabrikam/finance-cloud is example 1 of the Miro board, a B2B SaaS without multi-tenancy (spec section 9.1): every
// customer has instances of its own, and an application runs in an instance only where it is activated.

const showcase = showcaseOf('fabrikam', 'finance-cloud')
const views = [...environmentViewNames]
const ofType = (keys: Iterable<string>, prefix: string) => [...keys].filter((key) => key.startsWith(prefix)).toSorted()

const at = (stamp: AuditStamp | undefined) => Date.parse(stamp?.At ?? '')
/** Who wrote the latest version of a key's configuration, and when. */
const lastEdit = (view: string, key: string) =>
  showcaseScope('fabrikam', 'finance-cloud', view)!.documents.get(`configuration:${key}`)?.versions.at(-1)?.Modified

/** Every string of a document, at any depth: where the bindings are. */
const strings = (value: unknown): string[] =>
  typeof value === 'string'
    ? [value]
    : value && typeof value === 'object'
      ? Object.values(value as Record<string, unknown>).flatMap(strings)
      : []

describe('fabrikam/finance-cloud', () => {
  it('has the board’s default frame: Contoso with demo and production, three applications and the nightly job', () => {
    const keys = showcase.keys('default')
    for (const key of [
      'sbt.contoso',
      'rst.invoicing',
      'rst.payments',
      'rst.reporting',
      'cnt.contoso.demo',
      'cnt.contoso.production',
      'usg.contoso.invoicing',
      'usg.contoso.payments',
      'usg.contoso.reporting',
      'exe.contoso.invoicing.demo',
      'exe.contoso.invoicing.production',
      'exe.contoso.payments.production',
      'exe.contoso.reporting.production',
      'unt.reporting.nightly',
      'uxe.contoso.reporting.production.nightly',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    // Exactly the board's Contoso: the demo runs invoicing only, and the only JobRun is production reporting.
    expect(ofType(keys, 'cnt.contoso.')).toEqual(['cnt.contoso.demo', 'cnt.contoso.production'])
    expect(ofType(keys, 'usg.contoso.')).toEqual([
      'usg.contoso.invoicing',
      'usg.contoso.payments',
      'usg.contoso.reporting',
    ])
    expect(ofType(keys, 'exe.contoso.')).toEqual([
      'exe.contoso.invoicing.demo',
      'exe.contoso.invoicing.production',
      'exe.contoso.payments.production',
      'exe.contoso.reporting.production',
    ])
    expect(ofType(keys, 'uxe.contoso.')).toEqual(['uxe.contoso.reporting.production.nightly'])
  })

  it('has twelve customers, eight applications and six scheduled jobs', () => {
    const keys = showcase.keys('default')
    const all = new Set([...keys, ...[...showcase.trash('default').values()].flat()])
    expect(ofType(all, 'sbt.')).toHaveLength(12)
    expect(ofType(keys, 'rst.')).toEqual([
      'rst.bank-sync',
      'rst.customer-portal',
      'rst.expenses',
      'rst.invoicing',
      'rst.payments',
      'rst.payroll',
      'rst.reporting',
      'rst.tax-filing',
    ])
    expect(ofType(keys, 'unt.')).toEqual([
      'unt.bank-sync.statement-import',
      'unt.invoicing.dunning-run',
      'unt.payments.settlement-export',
      'unt.payroll.payslip-run',
      'unt.reporting.nightly',
      'unt.tax-filing.vat-submission',
    ])
    const annotations = showcase.annotations('default')
    for (const key of ofType(keys, 'unt.')) {
      expect(annotations.get(key), key).toMatchObject({
        UnitType: 'time',
        UnitDefinition: expect.stringMatching(/^\S+ \S+ \S+ \S+ \S+$/),
      })
    }
    expect(annotations.get('unt.reporting.nightly')).toMatchObject({ UnitDefinition: '0 2 * * *' })
    // About 50 usages, 70 AppRuns and 30 JobRuns.
    expect(ofType(all, 'usg.').length).toBeGreaterThanOrEqual(40)
    expect(ofType(all, 'exe.').length).toBeGreaterThanOrEqual(60)
    expect(ofType(all, 'uxe.').length).toBeGreaterThanOrEqual(25)
  })

  it('keeps the customers’ facts in values and the instance settings in configuration', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('sbt.contoso')).toMatchObject({
      Values: { crmId: 'CRM-10017', supportTier: 'premium', country: 'NL', accountManager: 'Grace Hopper' },
      Labels: ['mid-market', 'emea'],
    })
    for (const key of ofType(annotations.keys(), 'sbt.')) {
      const customer = annotations.get(key)!
      expect(customer.Values, key).toMatchObject({
        crmId: expect.stringMatching(/^CRM-\d+$/),
        supportTier: expect.stringMatching(/^(standard|premium|enterprise)$/),
        country: expect.stringMatching(/^[A-Z]{2}$/),
        accountManager: expect.any(String),
      })
      expect(['smb', 'mid-market', 'enterprise']).toContain(customer.Labels?.[0])
      expect(['emea', 'americas', 'apac']).toContain(customer.Labels?.[1])
    }

    expect(showcase.own('default', 'cnt.contoso.production')).toEqual({
      hostname: 'contoso.finance-cloud.fabrikam.example',
      region: 'westeurope',
      productVersion: '2026.9.3',
      databaseTier: 'general-purpose-4',
    })
    expect(showcase.own('default', 'cnt.contoso.demo')).toMatchObject({
      hostname: 'contoso-demo.finance-cloud.fabrikam.example',
    })
    for (const key of ofType(annotations.keys(), 'cnt.').filter((instance) => instance !== 'cnt.litware.trial')) {
      expect(showcase.own('default', key)?.productVersion, key).toMatch(/^2026\.9\.\d$/)
    }
    expect(showcase.versions('default', 'cnt.contoso.production')).toBe(4)
    expect(showcase.versions('default', 'rst.invoicing')).toBe(18)
  })

  it('requires a region and a host name on instances, and a tax provider on invoicing AppRuns', () => {
    expect(showcase.latest('default', 'schema:type:cnt')).toMatchObject({ required: ['region', 'hostname'] })
    expect(showcase.latest('default', 'schema:descendant:rst.invoicing:exe')).toMatchObject({
      required: ['taxProvider'],
    })
    // A production AppRun names the tax engine of its legal entity's country; the others inherit the sandbox.
    expect(showcase.own('default', 'exe.woodgrove-bank.invoicing.production')).toMatchObject({
      taxProvider: 'sovos',
      taxProviderEnvironment: 'production',
    })
    expect(showcase.own('default', 'exe.adventure-works.invoicing.production-eu')).toMatchObject({
      taxProvider: 'fonoa',
    })
    expect(showcase.own('default', 'exe.adventure-works.invoicing.production-us')).toMatchObject({
      taxProvider: 'avalara',
    })
    expect(showcase.own('default', 'exe.contoso.invoicing.demo')).toBeUndefined()
    expect(
      effectiveContent(unseededShowcaseDb(), showcase.scope('default'), 'exe.contoso.invoicing.demo'),
    ).toMatchObject({ taxProvider: 'avalara', taxProviderEnvironment: 'sandbox' })
  })

  it('gives every AppRun the logging, retry and telemetry defaults of the template', () => {
    expect(showcase.latest('default', 'template:exe')).toEqual({
      logLevel: 'info',
      retries: 3,
      telemetry: { endpoint: 'https://otel-collector.ops.fabrikam.example:4318', sampling: 0.1 },
    })
    // Reporting has no AppRun document: it runs on the product defaults and the template.
    const reporting = 'exe.contoso.reporting.production'
    expect(showcase.own('default', reporting)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), reporting)).toMatchObject({
      logLevel: 'info',
      retries: 3,
      exportFormats: ['xlsx', 'pdf', 'csv'],
      hostname: 'contoso.finance-cloud.fabrikam.example',
    })
  })

  it('binds to values the customer has, to the instance’s host name and to secrets in the vault', () => {
    const db = unseededShowcaseDb()
    const annotations = showcase.annotations('default')
    const found = { annotation: 0, config: 0, secret: 0 }
    for (const key of annotations.keys()) {
      for (const value of strings(showcase.own('default', key))) {
        const annotation = /^@annotation\("\/(\w+)", "Subject"\)$/.exec(value)
        const config = /^@config\("\/(\w+)"\)$/.exec(value)
        if (annotation) {
          found.annotation++
          const customer = annotations.get(`sbt.${key.split('.')[1]}`)
          expect(customer?.Values?.[annotation[1]!], `${key} ${value}`).toBeDefined()
        } else if (config) {
          found.config++
          expect(effectiveContent(db, showcase.scope('default'), key)[config[1]!], `${key} ${value}`).toBeDefined()
        } else if (value.startsWith('@')) {
          found.secret++
          expect(value, key).toMatch(/^@\([a-z0-9-]+\.[a-z0-9-]+, primaryVault\)$/)
        }
      }
    }
    expect(found.annotation).toBeGreaterThan(0)
    expect(found.config).toBeGreaterThan(0)
    expect(found.secret).toBeGreaterThan(0)
  })

  it('shows the lifecycle of the spec: an expired trial, a scheduled go-live, a disabled AppRun in UAT', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('cnt.litware.trial')).toBe('expired')
    expect(status('exe.litware.invoicing.trial')).toBe('expired')
    expect(status('cnt.woodgrove-bank.production')).toBe('scheduled')
    // The go-live brings the AppRuns and JobRuns of the instance with it.
    const goLive = [...annotations.keys()].filter((key) => /^(exe|uxe)\.woodgrove-bank\.[^.]+\.production/.test(key))
    expect(goLive).toHaveLength(11)
    expect(goLive.every((key) => status(key) === 'scheduled')).toBe(true)
    expect(status('cnt.woodgrove-bank.uat')).toBe('active')
    expect(status('exe.wide-world-importers.bank-sync.uat')).toBe('disabled')
    // Proseware gave notice on payroll: still active, with a planned end.
    expect(status('usg.proseware.payroll')).toBe('active')
    expect(annotations.get('usg.proseware.payroll')).toMatchObject({
      ExpiresAt: expect.any(String),
      Labels: ['notice-given'],
    })
  })

  it('has the churned customer in Deleted with everything it had, and a JobRun deleted on its own', () => {
    const trash = showcase.trash('default')
    const churned = trash.get('sbt.margies-travel')!
    expect(churned).toEqual(
      expect.arrayContaining([
        'cnt.margies-travel.production',
        'cnt.margies-travel.demo',
        'usg.margies-travel.invoicing',
        'exe.margies-travel.payments.production',
        'uxe.margies-travel.invoicing.production.dunning-run',
      ]),
    )
    expect(churned).toHaveLength(11)
    expect(churned.at(-1)).toBe('sbt.margies-travel')
    expect(trash.get('uxe.lucerne-publishing.payments.production.settlement-export')).toEqual([
      'uxe.lucerne-publishing.payments.production.settlement-export',
    ])
    expect([...showcase.keys('default')].some((key) => key.includes('margies-travel'))).toBe(false)
  })

  it('dates every document between its annotation’s creation and the day it was deleted or expired', () => {
    for (const view of views) {
      const { annotations, trash, documents } = showcaseScope('fabrikam', 'finance-cloud', view)!
      for (const annotation of [...annotations, ...trash.flatMap((tree) => tree.annotations)]) {
        const first = documents.get(`configuration:${annotation.AnnotationKey}`)?.versions[0]
        if (first) {
          expect(at(first.Modified), `${view} ${annotation.AnnotationKey}`).toBeGreaterThanOrEqual(
            at(annotation.Created),
          )
        }
      }
    }
    // Nobody edited the churned customer's documents after it went to Deleted, nor the trial's after it ended.
    let frozen = 0
    for (const tree of showcaseScope('fabrikam', 'finance-cloud', 'default')!.trash) {
      for (const { AnnotationKey: key } of tree.annotations) {
        const edit = lastEdit('default', key)
        if (edit) {
          frozen++
          expect(at(edit), key).toBeLessThan(Date.parse(tree.deletedAt))
        }
      }
    }
    expect(frozen).toBeGreaterThanOrEqual(7)
    const annotations = showcase.annotations('default')
    for (const key of ['cnt.litware.trial', 'exe.litware.expenses.trial']) {
      expect(at(lastEdit('default', key)), key).toBeLessThan(Date.parse(annotations.get(key)!.ExpiresAt!))
    }
  })

  it('schedules every JobRun on a CRON that agrees with its AppRun', () => {
    // Contoso moves the board's nightly job later and gives it larger batches and more time.
    expect(showcase.own('default', 'uxe.contoso.reporting.production.nightly')).toEqual({
      cron: '30 3 * * *',
      batchSize: 4000,
      timeoutMinutes: 120,
    })
    const found = { quarterly: 0, semiMonthly: 0 }
    for (const view of views) {
      for (const key of ofType(showcase.keys(view), 'uxe.')) {
        const cron = showcase.own(view, key)?.cron
        if (cron !== undefined) {
          expect(cron, `${view} ${key}`).toMatch(/^\S+ \S+ \S+ \S+ \S+$/)
        }
        const [, customer, application, instance] = key.split('.')
        const appRun = showcase.own(view, `exe.${customer}.${application}.${instance}`)
        // A quarterly filer submits in four months of the year, a semi-monthly payroll twice a month.
        if (appRun?.filingFrequency === 'quarterly') {
          found.quarterly++
          expect(cron, `${view} ${key}`).toMatch(/^\d+ \d+ \d+ \d+,\d+,\d+,\d+ \*$/)
        }
        if (appRun?.payFrequency === 'semi-monthly') {
          found.semiMonthly++
          expect(cron, `${view} ${key}`).toMatch(/^\d+ \d+ \d+,\d+ \* \*$/)
        }
      }
    }
    expect(found.quarterly).toBeGreaterThanOrEqual(4)
    expect(found.semiMonthly).toBeGreaterThanOrEqual(2)
    // Coho Winery lodges its BAS after the Australian quarters.
    const bas = 'uxe.coho-winery.tax-filing.production.vat-submission'
    expect(showcase.own('default', bas)).toEqual({ cron: '0 9 21 1,4,7,10 *' })
    expect(showcase.annotations('default').get(bas)?.Title).toBe('BAS submission on Coho Winery production')
    expect(showcase.own('default', 'uxe.proseware.payroll.production.payslip-run')).toMatchObject({
      cron: '0 5 13,28 * *',
    })
  })

  it('rehearses a customer’s setup against its own providers’ sandboxes, in its currency and units', () => {
    expect(showcase.own('default', 'exe.woodgrove-bank.invoicing.uat')).toMatchObject({
      taxProvider: 'sovos',
      taxProviderEnvironment: 'sandbox',
      invoiceCurrency: 'GBP',
    })
    expect(showcase.own('default', 'exe.adventure-works.invoicing.uat')).toMatchObject({
      taxProvider: 'fonoa',
      invoiceCurrency: 'EUR',
    })
    expect(showcase.own('default', 'exe.lucerne-publishing.invoicing.training')).toMatchObject({
      taxProvider: 'fonoa',
      invoiceCurrency: 'CHF',
    })
    expect(showcase.own('default', 'rst.invoicing')).not.toHaveProperty('defaultCurrency')
    const db = unseededShowcaseDb()
    const effective = (key: string) => effectiveContent(db, showcase.scope('default'), key)
    // A demo invoices in the customer's base currency, against the product's shared sandbox.
    expect(effective('exe.tailspin-toys.invoicing.demo')).toMatchObject({
      baseCurrency: 'USD',
      taxProvider: 'avalara',
      taxProviderEnvironment: 'sandbox',
    })
    expect(effective('exe.litware.expenses.production')).toMatchObject({ mileage: { unit: 'mi', rate: 0.7 } })
    expect(effective('exe.proseware.expenses.production')).toMatchObject({ mileage: { unit: 'km', rate: 0.72 } })
    // Adventure Works' Dutch payroll keeps its data in the EU, on Amsterdam's time.
    expect(showcase.own('default', 'cnt.adventure-works.production-eu')).toMatchObject({ dataResidency: 'eu' })
    expect(showcase.own('default', 'cnt.adventure-works.uat')).toMatchObject({ dataResidency: 'eu' })
    expect(showcase.annotations('default').get('cnt.adventure-works.uat')?.TimeZone).toBe('Europe/Amsterdam')
    expect(showcase.own('staging', 'exe.adventure-works.payroll.staging')).toMatchObject({ payrollCountry: 'NL' })
  })

  it('licenses enterprise applications by their use, and lets the right people write the documents', () => {
    expect(showcase.own('default', 'usg.woodgrove-bank.invoicing')).toMatchObject({ edition: 'enterprise', users: 300 })
    expect(showcase.own('default', 'usg.woodgrove-bank.tax-filing')).toMatchObject({ users: 12 })
    const portal = showcase.own('default', 'usg.woodgrove-bank.customer-portal')
    expect(portal).toMatchObject({ invoicesPerMonth: 30_000 })
    expect(portal).not.toHaveProperty('users')

    // The vault secrets are Margaret's; the customer's documents are its account manager's.
    const { documents } = showcaseScope('fabrikam', 'finance-cloud', 'default')!
    const authors = new Set<string | undefined>()
    for (const [id, document] of documents) {
      const last = document.versions.at(-1)!
      authors.add(last.Modified.By?.Id)
      if (JSON.stringify(last.Content).includes('primaryVault')) {
        expect(last.Modified.By?.Id, id).toBe(people.margaret.id)
      }
    }
    expect(authors.size).toBeGreaterThanOrEqual(4)
    expect(lastEdit('default', 'sbt.lucerne-publishing')?.By?.Id).toBe(people.ada.id)
    expect(lastEdit('default', 'sbt.proseware')?.By?.Id).toBe(people.grace.id)
  })

  it('activates fewer applications in a demo than the customer bought, and schedules no job there', () => {
    const keys = showcase.keys('default')
    const demos = ofType(keys, 'cnt.').filter((key) => key.endsWith('.demo'))
    expect(demos.length).toBeGreaterThanOrEqual(4)
    for (const demo of demos) {
      const customer = demo.split('.')[1]!
      const running = ofType(keys, `exe.${customer}.`).filter((key) => key.endsWith('.demo'))
      expect(running.length, demo).toBeGreaterThan(0)
      expect(running.length, demo).toBeLessThan(ofType(keys, `usg.${customer}.`).length)
    }
    expect(ofType(keys, 'uxe.').some((key) => key.split('.')[3] === 'demo')).toBe(false)
  })

  it('follows the board in development: Contoso’s new dev instance and its demo, payments on the demo', () => {
    const keys = showcase.keys('development')
    for (const key of [
      'sbt.contoso',
      'rst.invoicing',
      'rst.payments',
      'cnt.contoso.dev',
      'cnt.contoso.demo',
      'usg.contoso.invoicing',
      'usg.contoso.payments',
      'exe.contoso.invoicing.dev',
      'exe.contoso.invoicing.demo',
      'exe.contoso.payments.demo',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    expect(ofType(keys, 'sbt.')).toEqual(['sbt.contoso'])
    expect(ofType(keys, 'cnt.')).toEqual(['cnt.contoso.demo', 'cnt.contoso.dev'])
    expect(keys.has('exe.contoso.payments.dev')).toBe(false)
    // It adds the reporting job on the dev instance, every quarter of an hour.
    expect(ofType(keys, 'uxe.')).toEqual(['uxe.contoso.reporting.dev.nightly'])
    expect(showcase.own('development', 'uxe.contoso.reporting.dev.nightly')).toMatchObject({ cron: '*/15 * * * *' })
    expect(showcase.own('development', 'cnt.contoso.dev')).toMatchObject({
      hostname: 'contoso-dev.finance-cloud.fabrikam.example',
      productVersion: '2026.10.0-dev.14',
      logLevel: 'debug',
    })
    expect(showcase.latest('development', 'template:exe')).toMatchObject({ logLevel: 'debug', retries: 0 })
    expect(showcase.own('development', 'exe.contoso.payments.demo')).toMatchObject({
      merchantAccount: 'ContosoTEST',
      apiKey: '@(contoso.adyen-test-key, primaryVault)',
    })
  })

  it('upgrades every running instance to 2026.10.0 in the release candidate and then requires the version', () => {
    const keys = showcase.keys('release-candidate')
    // A close copy of production: the same catalog, without what production has in Deleted.
    expect(keys).toEqual(showcase.keys('default'))
    for (const key of ofType(keys, 'cnt.').filter((instance) => instance !== 'cnt.litware.trial')) {
      expect(showcase.own('release-candidate', key)?.productVersion, key).toBe('2026.10.0')
      expect(showcase.own('default', key)?.productVersion, key).not.toBe('2026.10.0')
    }
    expect(showcase.own('release-candidate', 'cnt.litware.trial')?.productVersion).toBe('2026.8.3')
    expect(showcase.versions('release-candidate', 'cnt.contoso.production')).toBe(5)
    expect(showcase.latest('release-candidate', 'schema:type:cnt')).toMatchObject({
      required: ['region', 'hostname', 'productVersion'],
    })
    expect(showcase.versions('release-candidate', 'rst.invoicing')).toBe(19)
    expect(showcase.own('release-candidate', 'rst.invoicing')?.eInvoiceFormats).toContain('ksef')
    expect(showcase.own('default', 'rst.invoicing')?.eInvoiceFormats).not.toContain('ksef')
  })

  it('stages the next release for three customers and Fabrikam’s QA customer, against the sandboxes', () => {
    const keys = showcase.keys('staging')
    expect(ofType(keys, 'sbt.')).toEqual([
      'sbt.adventure-works',
      'sbt.contoso',
      'sbt.fabrikam-qa',
      'sbt.woodgrove-bank',
    ])
    for (const key of ofType(keys, 'cnt.')) {
      expect(key.split('.')[2], key).toMatch(/^staging/)
      expect(showcase.own('staging', key)?.productVersion, key).toBe('2026.10.0-rc.3')
    }
    for (const key of ofType(keys, 'exe.').filter((appRun) => appRun.includes('.payments.'))) {
      expect(showcase.own('staging', key)?.merchantAccount, key).toMatch(/TEST$/)
    }
    expect(showcase.own('staging', 'exe.woodgrove-bank.tax-filing.staging')).toMatchObject({
      authority: 'hmrc',
      authorityEnvironment: 'sandbox',
    })
    expect(showcase.own('staging', 'exe.woodgrove-bank.invoicing.staging')).toMatchObject({
      taxProvider: 'sovos',
      taxProviderEnvironment: 'sandbox',
    })
    // The release candidate build brings the template, product defaults and instance schema of 2026.10.0.
    expect(showcase.latest('staging', 'template:exe')).toMatchObject({ logFormat: 'json' })
    expect(showcase.own('staging', 'rst.invoicing')?.eInvoiceFormats).toContain('ksef')
    expect(showcase.latest('staging', 'schema:type:cnt')).toMatchObject({
      required: ['region', 'hostname', 'productVersion'],
    })
  })

  it('delivers AI reconciliation to two customers that import their bank statements in production', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(ofType(keys, 'sbt.')).toEqual(['sbt.adventure-works', 'sbt.wide-world-importers'])
    expect(ofType(keys, 'exe.').filter((key) => key.includes('.ai-reconciliation.'))).toEqual([
      'exe.adventure-works.ai-reconciliation.production-us',
      'exe.wide-world-importers.ai-reconciliation.production',
    ])
    // A small subset of production plus the new application: everything else is in the default view too.
    const production = showcase.keys('default')
    expect([...keys].filter((key) => !key.includes('ai-reconciliation') && !production.has(key))).toEqual([])
    const fd1 = showcase.annotations('feature-delivery1')
    expect(fd1.get('usg.adventure-works.ai-reconciliation')).toMatchObject({ Labels: ['pilot'] })
    // The sales orders are the customer's, whatever the view shows of them.
    for (const key of ofType(keys, 'usg.').filter((usage) => production.has(usage))) {
      expect(fd1.get(key)?.Values, key).toEqual(showcase.annotations('default').get(key)?.Values)
    }
    expect(
      effectiveContent(
        unseededShowcaseDb(),
        showcase.scope('feature-delivery1'),
        'exe.adventure-works.ai-reconciliation.production-us',
      ),
    ).toMatchObject({ autoMatch: true, autoMatchAbove: 0.97, proposeAbove: 0.8 })
    expect(showcase.annotations('feature-delivery1').get('unt.ai-reconciliation.match-run')).toMatchObject({
      UnitType: 'event',
      UnitDefinition: 'bank-sync.statement-imported',
    })
    expect(showcase.latest('feature-delivery1', 'schema:descendant:rst.ai-reconciliation:exe')).toMatchObject({
      required: ['autoMatchAbove'],
    })
    for (const view of ['default', 'development', 'staging', 'release-candidate']) {
      expect(showcase.keys(view).has('rst.ai-reconciliation'), view).toBe(false)
    }
  })
})
