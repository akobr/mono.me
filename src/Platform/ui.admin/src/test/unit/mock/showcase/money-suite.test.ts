import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getResolvedConfiguration } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { effectiveContent } from '@/mock/configuration-handlers'
import { saveDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'
import { getDirectAncestors } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// bluefin/money-suite follows docs/42for.net/platform/examples/b2c-finance.md and example 4 of the Miro board.

const showcase = showcaseOf('bluefin', 'money-suite')
const document = (view: string, id: string) => showcaseScope('bluefin', 'money-suite', view)!.documents.get(id as never)
const ofAlex = (keys: Set<string>) => [...keys].filter((key) => key.split('.')[1] === 'alex').toSorted()
const ofType = (keys: Set<string>, code: string) => [...keys].filter((key) => key.startsWith(`${code}.`))

/** The board's default frame: Alex, the four products, three hats, four entitlements, nine product uses, one batch. */
const boardDefault = [
  'sbt.alex',
  'rst.accounts',
  'rst.payments',
  'rst.cards',
  'rst.investments',
  'cnt.alex.harbor-studio',
  'cnt.alex.bright-forge',
  'cnt.alex.self',
  'usg.alex.accounts',
  'usg.alex.payments',
  'usg.alex.cards',
  'usg.alex.investments',
  'exe.alex.accounts.self',
  'exe.alex.accounts.bright-forge',
  'exe.alex.accounts.harbor-studio',
  'exe.alex.payments.self',
  'exe.alex.payments.bright-forge',
  'exe.alex.payments.harbor-studio',
  'exe.alex.cards.self',
  'exe.alex.cards.bright-forge',
  'exe.alex.investments.self',
  'unt.accounts.monthly-statement',
  'uxe.alex.accounts.bright-forge.monthly-statement',
]

/** The board's release candidate: Harbor Studio gains cards and Bright Forge a credit line; no private hat. */
const boardReleaseCandidate = [
  'sbt.alex',
  'rst.cards',
  'rst.lending',
  'cnt.alex.harbor-studio',
  'cnt.alex.bright-forge',
  'usg.alex.cards',
  'usg.alex.lending',
  'exe.alex.cards.harbor-studio',
  'exe.alex.lending.bright-forge',
]

describe('bluefin/money-suite', () => {
  it('has the board’s default frame, and Alex exactly as drawn', () => {
    const keys = showcase.keys('default')
    for (const key of boardDefault) {
      expect(keys.has(key), key).toBe(true)
    }
    expect(ofAlex(keys)).toEqual(ofAlex(new Set(boardDefault)))
  })

  it('has a product use only where the documentation’s table has a cell', () => {
    const keys = showcase.keys('default')
    // Harbor Studio has no cards; the card entitlement is personal and exists anyway.
    expect(keys.has('exe.alex.cards.harbor-studio')).toBe(false)
    expect(keys.has('usg.alex.cards')).toBe(true)
    // Investments exist only for the private hat, for Alex and for everyone else.
    expect(ofType(keys, 'exe').filter((key) => key.split('.')[2] === 'investments')).not.toEqual([])
    expect(ofType(keys, 'exe').filter((key) => key.split('.')[2] === 'investments' && !key.endsWith('.self'))).toEqual(
      [],
    )
    expect(keys.has('rst.lending')).toBe(false)
  })

  it('stores Bright Forge’s whole difference on its payments use, and the history says who set the two-eyes rule', () => {
    expect(showcase.own('default', 'exe.alex.payments.bright-forge')).toEqual({
      limit: 250000,
      currency: 'NOK',
      vatInvoice: true,
      approval: 'two-eyes-above-50000',
    })
    const versions = document('default', 'configuration:exe.alex.payments.bright-forge')!.versions
    expect(versions.at(-1)?.Modified.By).toMatchObject({ Name: 'Hedy Lamarr' })
    expect(versions.at(-2)?.Content).not.toHaveProperty('approval')

    // Harbor Studio has a supplier-payment profile with an approval rule of its own.
    const harbor = showcase.own('default', 'exe.alex.payments.harbor-studio')
    expect(harbor).toMatchObject({ profile: 'supplier-payments', limit: expect.any(Number), currency: 'NOK' })
    expect(harbor?.approval).not.toBe('two-eyes-above-50000')
  })

  it('keeps the product default on Alex’s private payments, and the identity on the subject', () => {
    const db = unseededShowcaseDb()
    expect(showcase.own('default', 'exe.alex.payments.self')).toBeUndefined()
    const personal = effectiveContent(db, showcase.scope('default'), 'exe.alex.payments.self')
    expect(personal).toMatchObject({ limit: 50000, currency: 'NOK' })
    expect(personal).not.toHaveProperty('vatInvoice')

    expect(showcase.own('default', 'sbt.alex')).toMatchObject({
      customerNumber: '100042',
      language: 'nb',
      contact: { email: expect.any(String), phone: expect.any(String) },
    })
    // Written once, on the subject; every product use inherits the language and the customer number.
    expect(effectiveContent(db, showcase.scope('default'), 'exe.alex.payments.bright-forge')).toMatchObject({
      customerNumber: '100042',
      language: 'nb',
      limit: 250000,
    })
    expect(showcase.own('default', 'usg.alex.payments')?.services).toEqual(expect.arrayContaining(['vat-invoice']))
  })

  it('describes the companies, and records the legal form person on the private hat', () => {
    expect(showcase.own('default', 'cnt.alex.bright-forge')).toMatchObject({
      legalName: 'Bright Forge AS',
      organizationNumber: expect.any(String),
      jurisdiction: 'NO',
      settlementAccount: expect.any(String),
    })
    expect(showcase.own('default', 'cnt.alex.self')).toEqual({ jurisdiction: 'NO', legalForm: 'person' })
    for (const key of ofType(showcase.keys('default'), 'cnt')) {
      expect(showcase.own('default', key)?.jurisdiction, key).toMatch(/^[A-Z]{2}$/)
    }
  })

  it('requires a limit and a currency on payments uses, and a jurisdiction on every hat', () => {
    expect(showcase.latest('default', 'schema:descendant:rst.payments:exe')).toMatchObject({
      required: ['limit', 'currency'],
    })
    expect(showcase.latest('default', 'schema:descendant:sbt.alex:cnt')).toMatchObject({ required: ['jurisdiction'] })
    expect(showcase.latest('default', 'schema:type:cnt')).toMatchObject({ required: ['jurisdiction'] })
  })

  it('extends the catalog with more people of the same kind, products and batches', () => {
    const keys = showcase.keys('default')
    const subjects = ofType(keys, 'sbt')
    expect(subjects.length).toBeGreaterThanOrEqual(38)
    // Every person has the private hat; some have one or two companies.
    for (const subject of subjects) {
      expect(keys.has(`cnt.${subject.slice(4)}.self`), subject).toBe(true)
    }
    const companies = (name: string) => ofType(keys, 'cnt').filter((key) => key.startsWith(`cnt.${name}.`)).length - 1
    expect(subjects.filter((subject) => companies(subject.slice(4)) === 1).length).toBeGreaterThan(5)
    expect(subjects.filter((subject) => companies(subject.slice(4)) === 2).length).toBeGreaterThanOrEqual(2)
    for (const key of [
      'rst.savings',
      'rst.insurance',
      'unt.cards.annual-fee',
      'unt.investments.rebalance',
      'uxe.ingrid.cards.fjordlys-design.annual-fee',
      'uxe.ingrid.investments.self.rebalance',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    expect(showcase.annotations('default').get('unt.accounts.monthly-statement')).toMatchObject({
      UnitType: 'time',
      UnitDefinition: '0 6 1 * *',
    })
  })

  it('turns on only the payments services that the entitlement allows', () => {
    const keys = showcase.keys('default')
    const companyPayments = ofType(keys, 'exe').filter((key) => /^exe\.[^.]+\.payments\.(?!self$)/.test(key))
    expect(companyPayments.length).toBeGreaterThan(5)
    for (const key of companyPayments) {
      const person = key.split('.')[1]!
      const own = showcase.own('default', key)
      if (own?.vatInvoice) {
        expect(showcase.own('default', `usg.${person}.payments`)?.services, key).toContain('vat-invoice')
      }
    }
  })

  it('never creates an annotation before its ancestors, in any view', () => {
    for (const view of ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']) {
      const annotations = showcase.annotations(view)
      for (const [key, annotation] of annotations) {
        for (const ancestor of getDirectAncestors(key)) {
          const created = Date.parse(annotations.get(ancestor)!.Created!.At!)
          expect(Date.parse(annotation.Created!.At!), `${view} ${key} after ${ancestor}`).toBeGreaterThanOrEqual(
            created,
          )
        }
      }
    }
    // The pilot's runs are younger than their batch, and each names its customer.
    const runs = ofType(showcase.keys('feature-delivery1'), 'uxe').map((key) =>
      showcase.annotations('feature-delivery1').get(key)!,
    )
    expect(new Set(runs.map((run) => run.Title)).size).toBe(runs.length)
  })

  it('writes the bank details and the phone numbers in the format of the country', () => {
    const mod97 = (digits: string) => [...digits].reduce((rest, digit) => (rest * 10 + Number(digit)) % 97, 0)
    const ibanLength: Record<string, number> = { SE: 24, DK: 18, FI: 18 }
    const phone: Record<string, RegExp> = {
      NO: /^\+47 [49]\d{2} \d{2} \d{3}$/,
      SE: /^\+46 7\d \d{3} \d{2} \d{2}$/,
      DK: /^\+45 \d{2} \d{2} \d{2} \d{2}$/,
      FI: /^\+358 [45]0 \d{3} \d{4}$/,
    }
    for (const view of ['default', 'staging']) {
      for (const key of [...ofType(showcase.keys(view), 'cnt'), ...ofType(showcase.keys(view), 'sbt')]) {
        const own = showcase.own(view, key)!
        const account = (own.settlementAccount as string | undefined)?.replaceAll(/[ .]/g, '')
        if (account && /^[A-Z]{2}/.test(account)) {
          expect(account.length, key).toBe(ibanLength[account.slice(0, 2)])
          const numeric = [...(account.slice(4) + account.slice(0, 4))]
            .map((char) => (/[A-Z]/.test(char) ? String(char.charCodeAt(0) - 55) : char))
            .join('')
          expect(mod97(numeric), key).toBe(1)
        } else if (account) {
          // A Norwegian account number: eleven digits, the last a modulus 11 check digit.
          const weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2]
          const sum = weights.reduce((total, weight, index) => total + weight * Number(account[index]), 0)
          expect(account, key).toHaveLength(11)
          expect((11 - (sum % 11)) % 11, key).toBe(Number(account[10]))
        }
        const contact = own.contact as { phone: string } | undefined
        if (contact) {
          // The person's country is the jurisdiction of their private hat.
          const country = showcase.own(view, `cnt.${key.slice(4)}.self`)?.jurisdiction as string
          expect(contact.phone, key).toMatch(phone[country]!)
        }
      }
    }
  })

  it('shows the lifecycle of the documentation’s project', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('exe.sigrid.cards.self')).toBe('expired')
    expect(status('usg.nora.savings')).toBe('scheduled')
    expect(status('exe.nora.savings.self')).toBe('scheduled')
    expect(status('exe.eirik.payments.nordkapp-sjomat')).toBe('disabled')
    expect(annotations.get('exe.eirik.payments.nordkapp-sjomat')?.Description).toContain('KYC')

    // Anders closed the account: the person is in Deleted with both hats; a travel insurance was removed alone.
    const trash = showcase.trash('default')
    expect(trash.get('sbt.anders')).toEqual(
      expect.arrayContaining([
        'cnt.anders.self',
        'cnt.anders.vik-media',
        'exe.anders.payments.vik-media',
        'sbt.anders',
      ]),
    )
    expect(trash.get('exe.linnea.insurance.self')).toEqual(['exe.linnea.insurance.self'])
    expect(showcase.keys('default').has('usg.linnea.insurance')).toBe(true)
  })

  it('follows the board in the release candidate: cards for Harbor Studio and the new lending product', () => {
    const keys = showcase.keys('release-candidate')
    expect(ofAlex(keys)).toEqual(ofAlex(new Set(boardReleaseCandidate)))
    expect(ofType(keys, 'rst').toSorted()).toEqual(['rst.cards', 'rst.lending'])
    // No private hat, no batch and no batch run in this view.
    expect(ofType(keys, 'cnt').filter((key) => key.endsWith('.self'))).toEqual([])
    expect([...ofType(keys, 'unt'), ...ofType(keys, 'uxe')]).toEqual([])
    expect(showcase.own('release-candidate', 'exe.alex.cards.harbor-studio')).toMatchObject({ cardType: 'company' })
    expect(showcase.own('release-candidate', 'exe.alex.lending.bright-forge')).toMatchObject({ currency: 'NOK' })

    // The next release: a newer card product, a card limit required on every card, a schema for lending.
    expect(showcase.versions('release-candidate', 'rst.cards')).toBe(showcase.versions('default', 'rst.cards') + 1)
    // A copy of production on its own endpoints: production's latest card version keeps its date and its content.
    const production = (view: string) => document(view, 'configuration:rst.cards')!.versions[2]!
    expect(production('release-candidate').Content).toEqual({
      ...production('default').Content,
      processor: expect.objectContaining({ endpoint: 'https://issuing.rc.bluefin.example' }),
    })
    const at = (view: string) => Date.parse(production(view).Modified.At!)
    expect(Math.abs(at('release-candidate') - at('default'))).toBeLessThan(60_000)
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.cards:exe')).toMatchObject({
      required: ['cardType', 'monthlyLimit'],
    })
    expect(showcase.latest('default', 'schema:descendant:rst.cards:exe')).toMatchObject({ required: ['cardType'] })
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.lending:exe')).toMatchObject({
      required: ['creditLimit', 'currency'],
    })
  })

  it('runs development on the sandbox rails with test people, and staging with the test companies', () => {
    const development = showcase.keys('development')
    expect(ofType(development, 'sbt').toSorted()).toEqual([
      'sbt.kari-nordmann',
      'sbt.ola-nordmann',
      'sbt.sven-svensson',
    ])
    expect(development.has('cnt.ola-nordmann.qa-testselskap')).toBe(true)
    expect(showcase.own('development', 'rst.payments')).toMatchObject({
      logLevel: 'debug',
      fakeClearing: true,
      endpoint: 'https://payments.sandbox.bluefin.example',
    })
    // The statements run every fifteen minutes, so a developer does not wait a month for one.
    expect(showcase.annotations('development').get('unt.accounts.monthly-statement')).toMatchObject({
      UnitDefinition: '*/15 * * * *',
    })
    expect(showcase.annotations('development').get('sbt.ola-nordmann')?.Values).toMatchObject({
      fixture: 'fixtures/people/ola-nordmann.json',
    })

    const staging = showcase.keys('staging')
    expect(ofType(staging, 'sbt').length).toBe(7)
    expect(staging.has('cnt.jens-hansen.qa-testfirma')).toBe(true)
    expect(showcase.own('staging', 'rst.payments')?.endpoint).toBe('https://payments.staging.bluefin.example')
  })

  it('delivers the round-up sweep to three pilot customers in feature-delivery1 only', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(keys.has('unt.savings.round-up-sweep')).toBe(true)
    expect(ofType(keys, 'uxe').toSorted()).toEqual([
      'uxe.alex.savings.self.round-up-sweep',
      'uxe.ingrid.savings.self.round-up-sweep',
      'uxe.jonas.savings.self.round-up-sweep',
    ])
    expect(showcase.own('feature-delivery1', 'rst.savings')).toHaveProperty('roundUps')
    expect(showcase.keys('default').has('unt.savings.round-up-sweep')).toBe(false)
  })

  describe('through the mock API', () => {
    const server = setupServer(...mockHandlers)
    beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
    // The test setup clears the session after every test, so each test stores the project's database again.
    beforeEach(() =>
      saveDb({ ...unseededShowcaseDb(), account: { AccessMap: { 'bluefin.money-suite': 'Contributor' } } }),
    )
    afterEach(() => server.resetHandlers())
    afterAll(() => server.close())

    const resolved = async (view: string, key: string) =>
      (await getResolvedConfiguration('bluefin', 'money-suite', view, key)).Content

    it('resolves the bindings to the facts of the hats and of the person', async () => {
      expect(await resolved('default', 'uxe.alex.accounts.bright-forge.monthly-statement')).toMatchObject({
        deliverTo: 'regnskap@brightforge.example',
      })
      expect(await resolved('default', 'exe.alex.cards.bright-forge')).toMatchObject({
        embossing: 'Bright Forge AS',
        screening: 'low',
      })
      expect(await resolved('default', 'uxe.ingrid.cards.fjordlys-design.annual-fee')).toMatchObject({
        debitAccount: '9235.61.44122',
      })
      expect(await resolved('default', 'uxe.ingrid.investments.self.rebalance')).toMatchObject({
        modelPortfolio: 'growth',
      })
      // Secrets need the vault, so the mock returns them as written.
      expect(await resolved('default', 'rst.cards')).toMatchObject({
        processor: { apiKey: '@(cards.processor-api-key, primaryVault)' },
      })
    })

    it('resolves every configuration of every view', async () => {
      for (const view of ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']) {
        const keys = [...showcaseScope('bluefin', 'money-suite', view)!.documents.keys()]
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
