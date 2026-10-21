import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getResolvedConfiguration } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { effectiveContent } from '@/mock/configuration-handlers'
import { saveDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'
import { guestPlatform } from '@/mock/showcase/projects/guest-platform'
import { ancestorKeyOf } from '@/utils/binding-language'
import { annotationStatus } from '@/utils/annotation-status'
import { hasAt } from '@/utils/json-pointer'
import { showcaseOf, unseededShowcaseDb } from './support'

// seabreeze-hotels/guest-platform is a new example (spec section 9.14): hotels are the subjects, their sales channels
// the contexts, and a channel setup is one module on one channel with its rate plans, commission and payment terms.

const showcase = showcaseOf('seabreeze-hotels', 'guest-platform')
const hotels = ['algarve-cliffs', 'lisbon-marina', 'madeira-gardens', 'porto-riverside']
const views = ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1']
const day = 86_400_000

const withPrefix = (keys: Set<string>, prefix: string) => [...keys].filter((key) => key.startsWith(prefix))

/** The active keys of the view that have an own configuration. */
const configured = (view: string) =>
  [...showcaseScope('seabreeze-hotels', 'guest-platform', view)!.documents.keys()]
    .filter((id) => id.startsWith('configuration:'))
    .map((id) => id.slice('configuration:'.length))
    .filter((key) => showcase.keys(view).has(key))

/** Every string in a document, for the bindings. */
const strings = (value: unknown): string[] =>
  typeof value === 'string'
    ? [value]
    : Array.isArray(value)
      ? value.flatMap(strings)
      : value && typeof value === 'object'
        ? Object.values(value).flatMap(strings)
        : []

describe('seabreeze-hotels/guest-platform', () => {
  it('has the hotels, channels, modules and sync jobs of the model', () => {
    const keys = showcase.keys('default')
    expect(withPrefix(keys, 'sbt.').toSorted()).toEqual(hotels.map((hotel) => `sbt.${hotel}`))
    for (const key of [
      'rst.booking-engine',
      'rst.rate-manager',
      'rst.mobile-key',
      'rst.housekeeping',
      'rst.upsell',
      'rst.loyalty',
      'unt.rate-manager.rate-push',
      'unt.booking-engine.availability-sync',
      'unt.loyalty.points-export',
      'cnt.lisbon-marina.direct-web',
      'cnt.lisbon-marina.mobile-app',
      'cnt.lisbon-marina.booking-com',
      'cnt.lisbon-marina.expedia',
      'cnt.lisbon-marina.corporate-gds',
      'usg.lisbon-marina.rate-manager',
      'exe.lisbon-marina.rate-manager.booking-com',
      'exe.lisbon-marina.booking-engine.direct-web',
      'exe.algarve-cliffs.mobile-key.mobile-app',
      'uxe.lisbon-marina.rate-manager.booking-com.rate-push',
      'uxe.lisbon-marina.booking-engine.direct-web.availability-sync',
      'uxe.porto-riverside.loyalty.direct-web.points-export',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    const annotations = showcase.annotations('default')
    expect(annotations.get('unt.rate-manager.rate-push')).toMatchObject({
      UnitType: 'time',
      UnitDefinition: '*/15 * * * *',
    })
    expect(annotations.get('unt.loyalty.points-export')).toMatchObject({ UnitType: 'file' })
    expect(annotations.get('sbt.madeira-gardens')?.TimeZone).toBe('Atlantic/Madeira')
    // The night audit is one file per hotel, so the points go out once per hotel and night.
    expect(withPrefix(keys, 'uxe.').filter((key) => key.endsWith('.points-export'))).toEqual([
      'uxe.lisbon-marina.loyalty.direct-web.points-export',
      'uxe.porto-riverside.loyalty.direct-web.points-export',
      'uxe.madeira-gardens.loyalty.direct-web.points-export',
    ])
  })

  it('names the types as spec section 8 does', () => {
    expect(
      Object.fromEntries(Object.entries(guestPlatform.settings.types).map(([code, type]) => [code, type!.label])),
    ).toEqual({
      rst: 'Module',
      unt: 'Sync job',
      sbt: 'Hotel',
      usg: 'License',
      cnt: 'Channel',
      exe: 'Channel setup',
      uxe: 'Sync run',
    })
  })

  it('sells on the OTAs and the GDS through the rate manager, and keeps the guest services in the app', () => {
    const keys = showcase.keys('default')
    const executions = withPrefix(keys, 'exe.')
    const external = executions.filter((key) => /\.(booking-com|expedia|corporate-gds|flash-sale)$/.test(key))
    expect(external.length).toBeGreaterThan(5)
    expect(external.every((key) => key.split('.')[2] === 'rate-manager')).toBe(true)
    expect(
      executions
        .filter((key) => /^exe\.[^.]+\.(mobile-key|housekeeping)\./.test(key))
        .every((key) => key.endsWith('.mobile-app')),
    ).toBe(true)
    // Madeira Gardens is not in the app yet, and Porto Riverside has no mobile keys.
    expect(withPrefix(keys, 'cnt.madeira-gardens.')).toEqual([
      'cnt.madeira-gardens.direct-web',
      'cnt.madeira-gardens.booking-com',
    ])
    expect(keys.has('usg.porto-riverside.mobile-key')).toBe(false)
    // The rate push serves the external channels only.
    expect(
      withPrefix(keys, 'uxe.')
        .filter((key) => key.endsWith('.rate-push'))
        .every((key) => !/\.(direct-web|mobile-app)\./.test(key)),
    ).toBe(true)
  })

  it('stores rate plans, commission, payment terms and the vault key on a channel setup', () => {
    expect(showcase.own('default', 'exe.lisbon-marina.rate-manager.booking-com')).toEqual({
      ratePlans: [
        { code: 'BAR', name: 'Best available rate' },
        { code: 'NRF', name: 'Non-refundable', discountPercent: 10, nonRefundable: true },
      ],
      paymentTerms: 'pay-at-hotel',
      // 15 % and three points for the preferred-partner programme.
      commission: 18,
      hotelCode: '@annotation("/propertyCode", "Subject")',
      apiKey: '@(seabreeze.booking-com.lisbon-marina, primaryVault)',
    })
    expect(showcase.versions('default', 'exe.lisbon-marina.rate-manager.booking-com')).toBe(4)
    expect(showcase.own('default', 'exe.porto-riverside.rate-manager.booking-com')).toMatchObject({ commission: 15 })
    expect(showcase.own('default', 'exe.algarve-cliffs.rate-manager.expedia')).toMatchObject({
      commission: 21,
      paymentTerms: 'virtual-card',
    })
    expect(showcase.own('default', 'exe.lisbon-marina.rate-manager.corporate-gds')).toMatchObject({
      paymentTerms: 'invoice',
      commission: 10,
    })
    // The direct channels pay no commission and need no channel key.
    const direct = showcase.own('default', 'exe.lisbon-marina.rate-manager.direct-web')!
    expect(direct).not.toHaveProperty('commission')
    expect(direct).not.toHaveProperty('apiKey')
    expect(showcase.versions('default', 'rst.rate-manager')).toBe(17)
  })

  it('resolves every binding of the effective configurations, at every level that inherits it', () => {
    const db = unseededShowcaseDb()
    for (const view of views) {
      const annotations = showcase.annotations(view)
      let bindings = 0
      for (const key of configured(view)) {
        const effective = effectiveContent(db, showcase.scope(view), key)
        for (const text of strings(effective)) {
          const annotation = /^@annotation\("(\/\w+)", "(\w+)"\)$/.exec(text)
          const config = /^@config\("(\/\w+)"\)$/.exec(text)
          if (text.startsWith('@annotation') || text.startsWith('@config')) {
            expect(annotation ?? config, `${view} ${key}: ${text}`).not.toBeNull()
            bindings++
          }
          if (annotation) {
            const [, pointer, type] = annotation
            // A binding names the hotel or the channel, so it reads the same facts wherever it is inherited.
            expect(['Subject', 'Context'], `${view} ${key}: ${text}`).toContain(type)
            const target = ancestorKeyOf(key, type!)
            expect(hasAt(annotations.get(target!)?.Values, pointer!), `${view} ${key}: ${text}`).toBe(true)
          }
          if (config) {
            expect(hasAt(effective, config[1]!), `${view} ${key}: ${text}`).toBe(true)
          }
        }
      }
      expect(bindings, view).toBeGreaterThan(5)
    }
  })

  it('requires rate plans on the rate manager’s setups, which inherit the cancellation policy', () => {
    expect(showcase.latest('default', 'schema:descendant:rst.rate-manager:exe')).toMatchObject({
      required: ['ratePlans'],
    })
    expect(showcase.latest('default', 'template:exe')).toMatchObject({ cancellation: { freeUntilHours: 48 } })
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    expect(effectiveContent(db, scope, 'exe.porto-riverside.rate-manager.booking-com')).toMatchObject({
      cancellation: { freeUntilHours: 48, penalty: 'first-night' },
      endpoint: 'https://supply-xml.booking-com.example/hotels/ota',
      touristTax: { perNightEur: 3, maxNights: 7 },
      ratePlans: expect.arrayContaining([expect.objectContaining({ code: 'NRF' })]),
    })
    // Corporate bookings cancel free until six hours before arrival.
    expect(effectiveContent(db, scope, 'exe.lisbon-marina.rate-manager.corporate-gds')).toMatchObject({
      cancellation: { freeUntilHours: 6, penalty: 'first-night' },
    })
    // Madeira Gardens reads the inventory from its own PMS; the others from the group's.
    expect(
      effectiveContent(db, scope, 'uxe.madeira-gardens.booking-engine.direct-web.availability-sync'),
    ).toMatchObject({
      pms: 'https://pms-madeira.seabreeze-hotels.example/api',
    })
    expect(effectiveContent(db, scope, 'uxe.lisbon-marina.booking-engine.direct-web.availability-sync')).toMatchObject({
      pms: 'https://pms.seabreeze-hotels.example/api',
    })
    // A city hotel's website keeps the upsell defaults of the module; the resort's app sells its own extras.
    expect(showcase.own('default', 'exe.porto-riverside.upsell.direct-web')).toBeUndefined()
    expect(effectiveContent(db, scope, 'exe.porto-riverside.upsell.direct-web')).toMatchObject({
      offers: ['early-check-in', 'late-check-out', 'airport-transfer', 'breakfast'],
      placement: 'booking-flow',
    })
    expect(effectiveContent(db, scope, 'exe.algarve-cliffs.upsell.mobile-app')).toMatchObject({
      offers: ['late-check-out', 'airport-transfer', 'room-upgrade', 'spa-package'],
      placement: 'pre-arrival',
    })
  })

  it('schedules the summer season, ended the flash sale and paused Expedia in Porto', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('unt.rate-manager.summer-season')).toBe('scheduled')
    expect(withPrefix(showcase.keys('default'), 'uxe.').filter((key) => key.endsWith('.summer-season'))).toEqual([
      'uxe.algarve-cliffs.rate-manager.direct-web.summer-season',
      'uxe.algarve-cliffs.rate-manager.booking-com.summer-season',
      'uxe.algarve-cliffs.rate-manager.expedia.summer-season',
    ])
    expect(status('uxe.algarve-cliffs.rate-manager.booking-com.summer-season')).toBe('scheduled')
    expect(
      showcase.own('default', 'uxe.algarve-cliffs.rate-manager.booking-com.summer-season')?.ratePlans,
    ).toContainEqual(expect.objectContaining({ code: 'SUMMER' }))
    // The plan opens with the summer sales, months before the stays of its window.
    const summer = annotations.get('unt.rate-manager.summer-season')!
    const salesDays = (Date.parse(summer.ExpiresAt!) - Date.parse(summer.ValidFrom!)) / day
    expect(salesDays).toBeGreaterThan(200)
    expect(showcase.own('default', 'unt.rate-manager.summer-season')).toMatchObject({
      stayFrom: '06-01',
      stayTo: '09-30',
    })
    // The flash sale ran for a week.
    for (const key of ['cnt.algarve-cliffs.flash-sale', 'exe.algarve-cliffs.rate-manager.flash-sale']) {
      expect(status(key), key).toBe('expired')
      const { ValidFrom, ExpiresAt } = annotations.get(key)!
      expect(Math.round((Date.parse(ExpiresAt!) - Date.parse(ValidFrom!)) / day), key).toBe(7)
    }
    expect(status('cnt.porto-riverside.expedia')).toBe('disabled')
    expect(status('uxe.porto-riverside.rate-manager.expedia.rate-push')).toBe('disabled')
    // The pilot license of the mobile keys and the negotiated corporate rates are active with a planned end; the GDS
    // connection itself stays open.
    for (const key of ['usg.algarve-cliffs.mobile-key', 'exe.lisbon-marina.rate-manager.corporate-gds']) {
      expect(status(key), key).toBe('active')
      expect(annotations.get(key)?.ExpiresAt, key).toBeDefined()
    }
    expect(annotations.get('cnt.lisbon-marina.corporate-gds')?.ExpiresAt).toBeUndefined()
  })

  it('dates the documents of the annotations that ended before their end', () => {
    const scope = showcaseScope('seabreeze-hotels', 'guest-platform', 'default')!
    const lastChange = (key: string) =>
      Date.parse(scope.documents.get(`configuration:${key}`)!.versions.at(-1)!.Modified.At!)
    const wing = scope.trash.find((tree) => tree.rootKey === 'sbt.algarve-cliffs-villas')!
    const documented = wing.annotations.filter((annotation) =>
      scope.documents.has(`configuration:${annotation.AnnotationKey}`),
    )
    expect(documented.length).toBeGreaterThan(5)
    for (const { AnnotationKey } of documented) {
      expect(lastChange(AnnotationKey), AnnotationKey).toBeLessThan(Date.parse(wing.deletedAt))
      // Still within the 30 days that keep the content.
      expect(showcase.own('default', AnnotationKey), AnnotationKey).toBeDefined()
    }
    const annotations = showcase.annotations('default')
    for (const key of ['cnt.algarve-cliffs.flash-sale', 'exe.algarve-cliffs.rate-manager.flash-sale']) {
      expect(lastChange(key), key).toBeLessThan(Date.parse(annotations.get(key)!.ValidFrom!))
      expect(showcase.own('default', key), key).toBeDefined()
    }
  })

  it('takes the payment the channel’s terms promise', () => {
    // The app is prepaid, so its booking engine takes the whole stay; the website takes the resorts' deposit.
    expect(showcase.own('default', 'exe.lisbon-marina.booking-engine.mobile-app')).toMatchObject({
      deposit: { percent: 100 },
    })
    expect(showcase.own('default', 'exe.lisbon-marina.booking-engine.direct-web')).toMatchObject({
      deposit: { percent: 0 },
    })
    expect(showcase.own('default', 'exe.algarve-cliffs.booking-engine.direct-web')).toMatchObject({
      deposit: { percent: 30 },
    })
    // A non-refundable rate, such as the website's advance purchase, is charged at booking.
    expect(showcase.own('default', 'rst.booking-engine')).toMatchObject({ chargeNonRefundableAtBooking: true })
  })

  it('starts with the villa wing deleted with its channels and setups, and a setup deleted alone', () => {
    const trash = showcase.trash('default')
    expect(trash.get('sbt.algarve-cliffs-villas')).toEqual(
      expect.arrayContaining([
        'cnt.algarve-cliffs-villas.booking-com',
        'usg.algarve-cliffs-villas.rate-manager',
        'exe.algarve-cliffs-villas.rate-manager.booking-com',
        'uxe.algarve-cliffs-villas.rate-manager.booking-com.rate-push',
        'sbt.algarve-cliffs-villas',
      ]),
    )
    expect(trash.get('exe.lisbon-marina.housekeeping.direct-web')).toEqual([
      'exe.lisbon-marina.housekeeping.direct-web',
    ])
    expect([...showcase.keys('default')].filter((key) => key.includes('algarve-cliffs-villas'))).toEqual([])
  })

  it('brings digital check-in, dynamic pricing and a tighter schema in the release candidate', () => {
    const keys = showcase.keys('release-candidate')
    expect(keys.has('exe.lisbon-marina.digital-check-in.mobile-app')).toBe(true)
    expect(keys.has('exe.algarve-cliffs.digital-check-in.direct-web')).toBe(true)
    expect(showcase.keys('default').has('rst.digital-check-in')).toBe(false)
    // The website keeps the module's camera scan; the app reads the passport's chip.
    expect(showcase.own('release-candidate', 'rst.digital-check-in')).toMatchObject({ idScan: 'camera' })
    expect(showcase.own('release-candidate', 'exe.algarve-cliffs.digital-check-in.direct-web')).toBeUndefined()
    expect(showcase.own('release-candidate', 'exe.algarve-cliffs.digital-check-in.mobile-app')).toEqual({
      idScan: 'nfc-passport',
    })
    expect(showcase.own('release-candidate', 'rst.rate-manager')).toHaveProperty('dynamicPricing.enabled', true)
    expect(showcase.own('default', 'rst.rate-manager')).not.toHaveProperty('dynamicPricing')
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.rate-manager:exe')).toMatchObject({
      required: ['ratePlans', 'paymentTerms'],
    })
    // Otherwise a close copy of production, without the deleted wing.
    expect(withPrefix(keys, 'sbt.').toSorted()).toEqual(hotels.map((hotel) => `sbt.${hotel}`))
    expect(keys.has('cnt.porto-riverside.expedia')).toBe(true)
  })

  it('develops on a test property with the OTA simulator, sandbox locks and debug logging', () => {
    const keys = showcase.keys('development')
    expect(withPrefix(keys, 'sbt.')).toEqual(['sbt.test-property'])
    expect(withPrefix(keys, 'cnt.')).toEqual([
      'cnt.test-property.direct-web',
      'cnt.test-property.mobile-app',
      'cnt.test-property.ota-simulator',
    ])
    expect(showcase.own('development', 'cnt.test-property.ota-simulator')).toMatchObject({ logLevel: 'debug' })
    expect(showcase.own('development', 'exe.test-property.mobile-key.mobile-app')).toMatchObject({
      lockPlatform: 'ble-simulator',
    })
    expect(showcase.own('development', 'exe.test-property.booking-engine.direct-web')).toMatchObject({
      sandboxPayments: true,
    })
  })

  it('keeps the production hosts, mailboxes, partners and keys out of staging and development', () => {
    // The group's production hosts and mail domain, the channels' and the partners' live systems, the live keys.
    const production =
      /(\/\/|@)(www\.|api\.|pms\.|pms-madeira\.|loyalty\.|gds\.crs\.)?seabreeze-hotels\.example|supply-xml\.booking-com|services\.expediapartnercentral|flash-escapes|atlantic-air-miles|@\(seabreeze\.(?!staging|dev)/
    expect(JSON.stringify(showcase.own('default', 'unt.loyalty.points-export'))).toMatch(production)
    for (const view of ['development', 'staging', 'feature-delivery1']) {
      const annotations = showcase.annotations(view)
      for (const key of showcase.keys(view)) {
        const text = JSON.stringify([showcase.own(view, key) ?? {}, annotations.get(key)?.Values ?? {}])
        expect(text, `${view} ${key}`).not.toMatch(production)
      }
    }
    expect(showcase.own('staging', 'unt.loyalty.points-export')).toMatchObject({
      target: 'sftp://loyalty.staging.seabreeze-hotels.example/outbound',
      partners: ['partner-sandbox'],
    })
    expect(showcase.own('staging', 'unt.booking-engine.availability-sync')).toMatchObject({
      pms: 'https://pms-staging.seabreeze-hotels.example/api',
    })
    expect(showcase.own('feature-delivery1', 'unt.rate-manager.parity-check')).toMatchObject({
      alertTo: 'revenue@fd1.seabreeze-hotels.example',
    })
    expect(showcase.own('staging', 'exe.lisbon-marina.rate-manager.expedia')).toMatchObject({
      apiKey: '@(seabreeze.staging.expedia.lisbon-marina, primaryVault)',
    })
    expect(showcase.annotations('staging').get('cnt.lisbon-marina.booking-com')?.Values).toEqual({
      listingId: 'TEST-SBLIS-BDC',
    })
  })

  it('delivers the rate parity check to Lisbon Marina on its OTAs', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(withPrefix(keys, 'sbt.')).toEqual(['sbt.lisbon-marina'])
    expect(withPrefix(keys, 'uxe.').filter((key) => key.endsWith('.parity-check'))).toEqual([
      'uxe.lisbon-marina.rate-manager.booking-com.parity-check',
      'uxe.lisbon-marina.rate-manager.expedia.parity-check',
    ])
    expect(showcase.own('feature-delivery1', 'uxe.lisbon-marina.rate-manager.expedia.parity-check')).toEqual({
      tolerancePercent: 2,
    })
    expect(showcase.keys('default').has('unt.rate-manager.parity-check')).toBe(false)
  })

  describe('through the mock API', () => {
    const server = setupServer(...mockHandlers)
    beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
    // The test setup clears the session after every test, so each test stores the project's database again.
    beforeEach(() =>
      saveDb({ ...unseededShowcaseDb(), account: { AccessMap: { 'seabreeze-hotels.guest-platform': 'Reader' } } }),
    )
    afterEach(() => server.resetHandlers())
    afterAll(() => server.close())

    const resolved = async (view: string, key: string) =>
      (await getResolvedConfiguration('seabreeze-hotels', 'guest-platform', view, key)).Content

    it('resolves a channel setup to the listing of its channel and the code of its hotel', async () => {
      expect(await resolved('default', 'exe.lisbon-marina.rate-manager.booking-com')).toMatchObject({
        hotelId: '2873451',
        hotelCode: 'SBLIS',
        endpoint: 'https://supply-xml.booking-com.example/hotels/ota',
        // Secrets need the vault, so the mock returns them as written.
        apiKey: '@(seabreeze.booking-com.lisbon-marina, primaryVault)',
      })
      // A sync run below the channel reads the same listing.
      expect(await resolved('default', 'uxe.algarve-cliffs.rate-manager.booking-com.summer-season')).toMatchObject({
        hotelId: '4410938',
        hotelCode: 'SBFAO',
      })
      expect(await resolved('default', 'exe.lisbon-marina.booking-engine.mobile-app')).toMatchObject({
        returnUrl: 'seabreeze://hotels/lisbon-marina',
        confirmationSender: 'reservations.lisbon-marina@seabreeze-hotels.example',
      })
    })

    it('resolves every configuration of every view', async () => {
      for (const view of views) {
        for (const key of configured(view)) {
          await expect(resolved(view, key), `${view} ${key}`).resolves.toBeDefined()
        }
      }
    })
  })
})
