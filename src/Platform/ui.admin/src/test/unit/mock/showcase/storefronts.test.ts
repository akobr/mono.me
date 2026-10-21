import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getResolvedConfiguration } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { effectiveContent } from '@/mock/configuration-handlers'
import { saveDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'
import { storefronts } from '@/mock/showcase/projects/storefronts'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// velvet-retail/storefronts, a new example of the showcase (spec section 9.13): three brand storefronts across
// European markets, where the currency, the VAT, the carriers and the payment methods differ by market.

const showcase = showcaseOf('velvet-retail', 'storefronts')
const scopeOf = (view: string) => showcaseScope('velvet-retail', 'storefronts', view)!
const ofType = (keys: Set<string>, code: string) => [...keys].filter((key) => key.startsWith(`${code}.`)).toSorted()
const core = ['checkout', 'payment-provider', 'shipping', 'tax-engine']
const views = ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']
/** The age of a time stamp in days; NaN without one, so that a missing stamp fails. */
const daysAgo = (at: string | null | undefined) => (Date.now() - Date.parse(at ?? '')) / 86_400_000

describe('velvet-retail/storefronts', () => {
  it('names the types after the commerce model of section 8', () => {
    expect(
      Object.fromEntries(Object.entries(storefronts.settings.types).map(([code, type]) => [code, type.label])),
    ).toEqual({
      rst: 'Capability',
      unt: 'Feed',
      sbt: 'Storefront',
      usg: 'Subscription',
      cnt: 'Market',
      exe: 'Market setup',
      uxe: 'Feed run',
    })
  })

  it('has the storefronts, markets, capabilities and feeds of the model', () => {
    const keys = showcase.keys('default')
    expect(ofType(keys, 'sbt')).toEqual(['sbt.kidzone', 'sbt.urban-step', 'sbt.velvet'])
    expect(ofType(keys, 'rst')).toEqual([
      'rst.checkout',
      'rst.gift-cards',
      'rst.payment-provider',
      'rst.reviews',
      'rst.search',
      'rst.shipping',
      'rst.tax-engine',
    ])
    expect(ofType(keys, 'unt')).toEqual([
      'unt.payment-provider.settlement-import',
      'unt.search.product-feed',
      'unt.shipping.tracking-sync',
    ])
    expect(new Set(ofType(keys, 'cnt').map((key) => key.split('.')[2]))).toEqual(
      new Set(['de-de', 'at-de', 'cs-cz', 'pl-pl', 'fr-fr', 'en-gb', 'it-it']),
    )
    // Every market of a storefront sets up the four core capabilities.
    for (const market of ofType(keys, 'cnt')) {
      const [, storefront, name] = market.split('.')
      for (const capability of core) {
        expect(keys.has(`exe.${storefront}.${capability}.${name}`), `${capability} in ${market}`).toBe(true)
      }
    }
  })

  it('keeps the currency and the locale on the market and the VAT rate on the tax setup', () => {
    expect(showcase.own('default', 'cnt.velvet.de-de')).toMatchObject({ currency: 'EUR', locale: 'de-DE' })
    expect(showcase.own('default', 'cnt.velvet.at-de')).toMatchObject({ currency: 'EUR', locale: 'de-AT' })
    expect(showcase.own('default', 'cnt.velvet.cs-cz')).toMatchObject({ currency: 'CZK', locale: 'cs-CZ' })
    expect(showcase.own('default', 'cnt.urban-step.pl-pl')).toMatchObject({ currency: 'PLN', locale: 'pl-PL' })
    expect(showcase.own('default', 'cnt.velvet.en-gb')).toMatchObject({ currency: 'GBP', locale: 'en-GB' })
    const vat = { 'de-de': 19, 'at-de': 20, 'cs-cz': 21, 'pl-pl': 23, 'fr-fr': 20, 'en-gb': 20, 'it-it': 22 }
    for (const [market, rate] of Object.entries(vat)) {
      expect(showcase.own('default', `exe.velvet.tax-engine.${market}`)?.vatRate, market).toBe(rate)
    }
    expect(showcase.own('default', 'exe.velvet.tax-engine.en-gb')).toMatchObject({ filing: 'mtd' })
  })

  it('sets up the carriers, the payment methods and a provider key from the vault per market', () => {
    expect(showcase.own('default', 'exe.velvet.shipping.pl-pl')?.carriers).toContain('inpost')
    // KidZone ships to Zásilkovna's pick-up points only in Czechia.
    expect(showcase.own('default', 'exe.kidzone.shipping.cs-cz')?.carriers).toEqual(['zasilkovna'])
    expect(showcase.own('default', 'exe.velvet.payment-provider.pl-pl')).toMatchObject({
      methods: ['blik', 'przelewy24', 'card', 'paypal'],
      merchantAccount: 'VelvetEU',
      apiKey: '@(velvet.adyen-live-api-key, primaryVault)',
    })
    expect(showcase.versions('default', 'exe.velvet.payment-provider.pl-pl')).toBe(3)
    expect(showcase.own('default', 'exe.urban-step.payment-provider.en-gb')).toMatchObject({
      merchantAccount: 'UrbanStepUK',
    })
    // The carrier collects cash on delivery, so it is a fee of the checkout and not a method of Adyen.
    expect(showcase.own('default', 'exe.velvet.payment-provider.cs-cz')?.methods).toEqual([
      'card',
      'bank-transfer',
      'apple-pay',
    ])
    expect(showcase.own('default', 'exe.velvet.checkout.cs-cz')).toEqual({ cashOnDeliveryFee: 39 })
    expect(showcase.own('default', 'exe.velvet.shipping.cs-cz')).toMatchObject({ cashOnDelivery: true })
  })

  it('titles the setups and feed runs by country and the settlement imports by merchant account', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('exe.velvet.checkout.en-gb')?.Title).toBe('Velvet checkout in the United Kingdom')
    expect(annotations.get('uxe.velvet.search.fr-fr.product-feed')?.Title).toBe('Velvet product feed in France')
    expect(annotations.get('uxe.velvet.payment-provider.de-de.settlement-import')?.Title).toBe(
      'Velvet EU settlement import',
    )
    expect(annotations.get('uxe.urban-step.payment-provider.en-gb.settlement-import')?.Title).toBe(
      'Urban Step UK settlement import',
    )
  })

  it('stores the currency and the base URL of the market and the contacts of the values for the bindings', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    expect(effectiveContent(db, scope, 'exe.velvet.payment-provider.cs-cz')).toMatchObject({
      currency: 'CZK',
      settlementCurrency: '@config("/currency")',
    })
    expect(effectiveContent(db, scope, 'exe.velvet.search.fr-fr')).toMatchObject({
      baseUrl: 'https://www.velvet-fashion.example/fr-fr',
      productUrlBase: '@config("/baseUrl")',
    })
    // The ancestors hold the values that the @annotation bindings read.
    const annotations = showcase.annotations('default')
    expect(showcase.own('default', 'usg.velvet.checkout')).toMatchObject({
      replyTo: '@annotation("/supportEmail", "Subject")',
    })
    expect(annotations.get('sbt.velvet')?.Values).toMatchObject({ supportEmail: 'service@velvet-fashion.example' })
    expect(showcase.own('default', 'exe.velvet.shipping.de-de')).toMatchObject({
      exceptionAlertsTo: '@annotation("/marketManager", "Context")',
    })
    expect(annotations.get('cnt.velvet.de-de')?.Values).toMatchObject({
      marketManager: 'lena.vogt@velvet-retail.example',
    })
  })

  it('gives every market setup the checkout defaults of the template', () => {
    expect(showcase.latest('default', 'template:exe')).toMatchObject({ guestCheckout: true, withdrawalPeriodDays: 14 })
    // The log level belongs to the stage, so it lives on the market and not in the template.
    expect(showcase.latest('default', 'template:exe')).not.toHaveProperty('logLevel')
    // Urban Step keeps the checkout of the platform in Germany: no document of its own.
    const key = 'exe.urban-step.checkout.de-de'
    expect(showcase.own('default', key)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), key)).toMatchObject({
      guestCheckout: true,
      orderTimeoutMinutes: 20,
      currency: 'EUR',
      locale: 'de-DE',
      logLevel: 'warn',
    })
  })

  it('requires the currency and the locale on markets and the VAT rate on tax setups', () => {
    expect(showcase.latest('default', 'schema:type:cnt')).toMatchObject({ required: ['currency', 'locale'] })
    expect(showcase.latest('default', 'schema:descendant:rst.tax-engine:exe')).toMatchObject({ required: ['vatRate'] })
  })

  it('has the long history of the checkout and short ones of markets and setups', () => {
    expect(showcase.versions('default', 'rst.checkout')).toBe(17)
    // Tuned every few weeks since the platform went live 400 days ago.
    const first = scopeOf('default').documents.get('configuration:rst.checkout')!.versions[0]!
    expect(daysAgo(first.Modified.At)).toBeGreaterThan(300)
    expect(daysAgo(first.Modified.At)).toBeLessThan(400)
    expect(showcase.versions('default', 'cnt.velvet.de-de')).toBe(3)
    expect(showcase.own('default', 'cnt.velvet.de-de')).toMatchObject({ freeShippingFrom: 49 })
    expect(showcase.versions('default', 'exe.velvet.shipping.de-de')).toBe(2)
  })

  it('schedules the Italian launch, ended the Polish gift-card promotion and paused Urban Step’s German reviews', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('cnt.velvet.it-it')).toBe('scheduled')
    for (const capability of core) {
      expect(status(`exe.velvet.${capability}.it-it`), capability).toBe('scheduled')
    }
    expect(status('exe.velvet.gift-cards.pl-pl')).toBe('expired')
    expect(showcase.own('default', 'exe.velvet.gift-cards.pl-pl')).toMatchObject({ bonus: { percent: 10 } })
    // The promotion ran for about two months.
    const promotion = annotations.get('exe.velvet.gift-cards.pl-pl')!
    expect(daysAgo(promotion.Created?.At) - daysAgo(promotion.ExpiresAt)).toBeCloseTo(60)
    expect(status('exe.urban-step.reviews.de-de')).toBe('disabled')
    // A subscription under notice stays active until it ends.
    expect(status('usg.urban-step.reviews')).toBe('active')
    expect(annotations.get('usg.urban-step.reviews')?.ExpiresAt).toBeDefined()
  })

  it('starts with KidZone France in Deleted with its setups, and Velvet’s Polish reviews deleted alone', () => {
    const trees = scopeOf('default').trash
    expect(trees.map(({ rootKey, by }) => [rootKey, by]).toSorted()).toEqual([
      ['cnt.kidzone.fr-fr', 'margaret'],
      ['exe.velvet.reviews.pl-pl', 'edsger'],
    ])
    for (const tree of trees) {
      expect(daysAgo(tree.deletedAt), tree.rootKey).toBeGreaterThanOrEqual(3)
      expect(daysAgo(tree.deletedAt), tree.rootKey).toBeLessThanOrEqual(60)
    }
    expect(showcase.trash('default').get('cnt.kidzone.fr-fr')).toEqual(
      expect.arrayContaining([
        'exe.kidzone.tax-engine.fr-fr',
        'uxe.kidzone.shipping.fr-fr.tracking-sync',
        'cnt.kidzone.fr-fr',
      ]),
    )
    expect(showcase.trash('default').get('exe.velvet.reviews.pl-pl')).toEqual(['exe.velvet.reviews.pl-pl'])
    // The release candidate copies production as it is now, after the deletions.
    expect(showcase.keys('release-candidate').has('cnt.kidzone.fr-fr')).toBe(false)
    expect(showcase.keys('release-candidate').has('exe.velvet.reviews.pl-pl')).toBe(false)
  })

  it('runs each feed only where it applies', () => {
    const keys = [...showcase.keys('default')]
    // One settlement import per merchant account: the EU account at the German market, the UK one at the British.
    expect(keys.filter((key) => key.startsWith('uxe.') && key.endsWith('.settlement-import')).toSorted()).toEqual([
      'uxe.kidzone.payment-provider.de-de.settlement-import',
      'uxe.urban-step.payment-provider.de-de.settlement-import',
      'uxe.urban-step.payment-provider.en-gb.settlement-import',
      'uxe.velvet.payment-provider.de-de.settlement-import',
      'uxe.velvet.payment-provider.en-gb.settlement-import',
    ])
    // Velvet's fulfilment partner posts the tracking events to the order webhook, so Velvet has no tracking sync.
    expect(keys.some((key) => key.startsWith('uxe.velvet.') && key.endsWith('.tracking-sync'))).toBe(false)
    // KidZone does not subscribe to search: no subscription, no setup and no product feed.
    expect(keys.some((key) => key.includes('.kidzone.search'))).toBe(false)
    // Italy opens without search.
    expect(keys.some((key) => key.startsWith('exe.velvet.search.it-it'))).toBe(false)
  })

  it('adds buy now, pay later in two markets in feature delivery 1', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(ofType(keys, 'sbt')).toEqual(['sbt.velvet'])
    expect(ofType(keys, 'exe').filter((key) => key.includes('.buy-now-pay-later.'))).toEqual([
      'exe.velvet.buy-now-pay-later.at-de',
      'exe.velvet.buy-now-pay-later.de-de',
    ])
    expect(showcase.own('feature-delivery1', 'exe.velvet.buy-now-pay-later.de-de')).toMatchObject({
      apiKey: '@(velvet.klarna-test-api-key, primaryVault)',
    })
    expect(showcase.keys('default').has('rst.buy-now-pay-later')).toBe(false)
    // Klarna is new in these markets: production pays through Adyen without it.
    for (const market of ['de-de', 'at-de']) {
      expect(JSON.stringify(showcase.own('default', `exe.velvet.payment-provider.${market}`)), market).not.toContain(
        'klarna',
      )
    }
    // The preview hosts of the feature, with debug logging on the setups.
    expect(showcase.own('feature-delivery1', 'cnt.velvet.de-de')).toMatchObject({
      baseUrl: 'https://bnpl.preview.velvet-fashion.example/de-de',
    })
    expect(showcase.own('feature-delivery1', 'rst.payment-provider')?.webhookUrl).toBe(
      'https://payments.bnpl.preview.velvet-retail.example/webhooks/adyen',
    )
    const setup = 'exe.velvet.buy-now-pay-later.de-de'
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('feature-delivery1'), setup)).toMatchObject({
      logLevel: 'debug',
    })
  })

  it('brings the returns portal and the price display of tax setups in the release candidate', () => {
    expect(showcase.keys('release-candidate').has('exe.velvet.returns-portal.de-de')).toBe(true)
    expect(showcase.keys('default').has('rst.returns-portal')).toBe(false)
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.tax-engine:exe')).toMatchObject({
      required: ['vatRate', 'priceDisplay'],
    })
    expect(showcase.own('release-candidate', 'exe.kidzone.tax-engine.cs-cz')).toMatchObject({
      vatRate: 21,
      priceDisplay: 'gross',
    })
    expect(showcase.versions('release-candidate', 'rst.checkout')).toBe(18)
    expect(showcase.own('release-candidate', 'rst.checkout')?.expressCheckout).toContain('google-pay')
  })

  it('keeps development to Velvet’s sandbox, in German and the en-XA pseudo-locale', () => {
    const keys = showcase.keys('development')
    expect(ofType(keys, 'sbt')).toEqual(['sbt.velvet'])
    expect(ofType(keys, 'cnt')).toEqual(['cnt.velvet.de-de', 'cnt.velvet.en-xa'])
    expect(showcase.own('development', 'cnt.velvet.en-xa')).toMatchObject({
      locale: 'en-XA',
      logLevel: 'debug',
      baseUrl: 'https://dev.velvet-fashion.example/en-xa',
    })
    expect(showcase.own('development', 'exe.velvet.payment-provider.de-de')).toMatchObject({
      endpoint: 'https://checkout-test.adyen.example/v71',
      apiKey: '@(velvet.adyen-test-api-key, primaryVault)',
    })
    // The developers' debug logging reaches every market setup.
    for (const capability of core) {
      const setup = `exe.velvet.${capability}.de-de`
      expect(effectiveContent(unseededShowcaseDb(), showcase.scope('development'), setup).logLevel, setup).toBe('debug')
    }
  })

  it('tests the three storefronts in Germany, Czechia and the UK on staging, with test payments', () => {
    const keys = showcase.keys('staging')
    expect(ofType(keys, 'cnt')).toEqual([
      'cnt.kidzone.cs-cz',
      'cnt.urban-step.de-de',
      'cnt.velvet.cs-cz',
      'cnt.velvet.de-de',
      'cnt.velvet.en-gb',
    ])
    expect(showcase.own('staging', 'exe.kidzone.payment-provider.cs-cz')?.apiKey).toBe(
      '@(kidzone.adyen-test-api-key, primaryVault)',
    )
    expect(showcase.annotations('staging').get('sbt.kidzone')?.Values).toMatchObject({
      supportEmail: 'test-inbox+kidzone@velvet-retail.example',
    })
    // The tracking sync of KidZone Czechia is off for good, so staging mirrors production.
    for (const view of ['default', 'staging']) {
      const run = showcase.annotations(view).get('uxe.kidzone.shipping.cs-cz.tracking-sync')!
      expect(annotationStatus(run), view).toBe('disabled')
    }
  })

  it('keeps the production endpoints and mailboxes out of the test views', () => {
    for (const view of ['development', 'staging', 'feature-delivery1']) {
      const contents = [...scopeOf(view).documents.values()].map((document) => document.versions.at(-1)?.Content)
      const text = JSON.stringify(contents)
      expect(text, view).not.toContain('https://payments.velvet-retail.example')
      expect(text, view).not.toContain('sftp://reports.adyen.example')
      expect(text, view).not.toContain('"commerce-ops@velvet-retail.example"')
      expect(text, view).not.toContain('-live')
      const managers = [...showcase.annotations(view).values()].flatMap((annotation) =>
        annotation.Values?.marketManager ? [String(annotation.Values.marketManager)] : [],
      )
      expect(managers.length, view).toBeGreaterThan(0)
      expect(
        managers.every((manager) => manager.startsWith('test-inbox+')),
        view,
      ).toBe(true)
    }
    expect(showcase.own('staging', 'rst.payment-provider')?.webhookUrl).toBe(
      'https://payments.staging.velvet-retail.example/webhooks/adyen',
    )
    expect(showcase.own('staging', 'unt.payment-provider.settlement-import')?.source).toBe(
      'sftp://reports-test.adyen.example/settlements',
    )
    expect(showcase.latest('staging', 'template:uxe')?.alertEmail).toBe('commerce-ops+staging@velvet-retail.example')
    expect(showcase.annotations('staging').get('cnt.velvet.de-de')?.Values).toMatchObject({
      marketManager: 'test-inbox+velvet-de-de@velvet-retail.example',
    })
    expect(showcase.own('default', 'rst.payment-provider')?.webhookUrl).toBe(
      'https://payments.velvet-retail.example/webhooks/adyen',
    )
  })

  describe('through the mock API', () => {
    const server = setupServer(...mockHandlers)
    beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
    // The test setup clears the session after every test, so each test stores the project's database again.
    beforeEach(() =>
      saveDb({ ...unseededShowcaseDb(), account: { AccessMap: { 'velvet-retail.storefronts': 'Contributor' } } }),
    )
    afterEach(() => server.resetHandlers())
    afterAll(() => server.close())

    const resolved = async (view: string, key: string) =>
      (await getResolvedConfiguration('velvet-retail', 'storefronts', view, key)).Content

    it('resolves the bindings to the market, its manager and the storefront’s service address', async () => {
      expect(await resolved('default', 'exe.velvet.shipping.de-de')).toMatchObject({
        exceptionAlertsTo: 'lena.vogt@velvet-retail.example',
      })
      // Secrets need the vault, so the mock returns them as written.
      expect(await resolved('default', 'exe.velvet.payment-provider.cs-cz')).toMatchObject({
        settlementCurrency: 'CZK',
        apiKey: '@(velvet.adyen-live-api-key, primaryVault)',
      })
      expect(await resolved('default', 'exe.velvet.search.fr-fr')).toMatchObject({
        productUrlBase: 'https://www.velvet-fashion.example/fr-fr',
      })
      expect(await resolved('default', 'usg.velvet.checkout')).toMatchObject({
        replyTo: 'service@velvet-fashion.example',
      })
      expect(await resolved('staging', 'usg.velvet.checkout')).toMatchObject({
        replyTo: 'test-inbox+velvet@velvet-retail.example',
      })
      expect(await resolved('development', 'exe.velvet.shipping.de-de')).toMatchObject({
        exceptionAlertsTo: 'test-inbox+velvet-de-de@velvet-retail.example',
      })
    })

    it('resolves every configuration of every view', async () => {
      for (const view of views) {
        const keys = [...scopeOf(view).documents.keys()]
          .filter((id) => id.startsWith('configuration:'))
          .map((id) => id.slice('configuration:'.length))
          .filter((key) => showcase.keys(view).has(key))
        expect(keys.length, view).toBeGreaterThan(0)
        for (const key of keys) {
          await expect(resolved(view, key), `${view} ${key}`).resolves.toBeDefined()
        }
      }
    })
  })
})
