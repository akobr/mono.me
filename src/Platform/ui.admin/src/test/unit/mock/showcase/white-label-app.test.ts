import { describe, expect, it } from 'vitest'
import { effectiveContent } from '@/mock/configuration-handlers'
import { showcaseScope } from '@/mock/showcase'
import { whiteLabelApp } from '@/mock/showcase/projects/white-label-app'
import { getAllAncestors } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { ancestorKeyOf } from '@/utils/binding-language'
import { getAt } from '@/utils/json-pointer'
import { showcaseOf, unseededShowcaseDb } from './support'

// appforge/white-label-app follows example 10 of the Miro board: brands publish AppForge's app under their own name.

const showcase = showcaseOf('appforge', 'white-label-app')
const ofType = (keys: Set<string>, prefix: string) => [...keys].filter((key) => key.startsWith(prefix)).toSorted()
const at = (stamp: { At?: string | null } | undefined) => Date.parse(stamp?.At ?? '')

/** Every binding string of a document. */
function bindingsOf(value: unknown): string[] {
  if (typeof value === 'string') {
    return value.startsWith('@') ? [value] : []
  }
  if (value && typeof value === 'object') {
    return Object.values(value).flatMap(bindingsOf)
  }
  return []
}

const call = /^@(config|annotation)\("([^"]*)"(?:, "([^"]*)")?\)$/
const secret = /^@\([a-z0-9-]+\.[a-z0-9-]+, primaryVault\)$/

describe('appforge/white-label-app', () => {
  it('names the types as the board does', () => {
    expect(
      Object.fromEntries(Object.entries(whiteLabelApp.settings.types).map(([code, type]) => [code, type.label])),
    ).toEqual({
      rst: 'Feature',
      unt: 'Campaign',
      sbt: 'Brand',
      usg: 'Feature license',
      cnt: 'Platform',
      exe: 'App feature',
      uxe: 'CampaignRun',
    })
  })

  it('schedules the weekly digest on Mondays and triggers campaigns by time, message, file and event', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('unt.push.weekly-digest')).toMatchObject({ UnitType: 'time', UnitDefinition: '0 9 * * 1' })
    const units = [...annotations.values()].filter((annotation) => annotation.AnnotationKey.startsWith('unt.'))
    expect(new Set(units.map((unit) => (unit as { UnitType?: string }).UnitType))).toEqual(
      new Set(['time', 'message', 'file', 'event']),
    )
  })

  it('never dates an annotation before its ancestors, nor a change or a version before the annotation', () => {
    for (const view of whiteLabelApp.views.map((candidate) => candidate.name)) {
      const { annotations, trash, documents } = showcaseScope('appforge', 'white-label-app', view)!
      const everything = [...annotations, ...trash.flatMap((tree) => tree.annotations)]
      const created = new Map(everything.map((annotation) => [annotation.AnnotationKey, at(annotation.Created)]))
      for (const annotation of everything) {
        const key = annotation.AnnotationKey
        const since = created.get(key)!
        for (const ancestor of getAllAncestors(key)) {
          expect(since, `${view} ${key} after ${ancestor}`).toBeGreaterThanOrEqual(created.get(ancestor)!)
        }
        expect(at(annotation.Modified), `${view} ${key} modified`).toBeGreaterThanOrEqual(since)
        const first = documents.get(`configuration:${key}`)?.versions[0]
        if (first) {
          expect(at(first.Modified), `${view} ${key} version 1`).toBeGreaterThanOrEqual(since)
        }
      }
    }
  })

  it('has the board’s default frame: FreshMart with three features, three platforms and seven app features', () => {
    const keys = showcase.keys('default')
    for (const key of [
      'sbt.freshmart',
      'rst.loyalty',
      'rst.checkout',
      'rst.push',
      'cnt.freshmart.web',
      'cnt.freshmart.ios',
      'cnt.freshmart.android',
      'unt.push.weekly-digest',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    expect(ofType(keys, 'usg.freshmart.')).toEqual([
      'usg.freshmart.checkout',
      'usg.freshmart.loyalty',
      'usg.freshmart.push',
    ])
    expect(ofType(keys, 'exe.freshmart.')).toEqual([
      'exe.freshmart.checkout.android',
      'exe.freshmart.checkout.ios',
      'exe.freshmart.loyalty.android',
      'exe.freshmart.loyalty.ios',
      'exe.freshmart.loyalty.web',
      'exe.freshmart.push.android',
      'exe.freshmart.push.ios',
    ])
    // The weekly digest runs on FreshMart's two native push app features, and FreshMart runs no other campaign.
    expect(ofType(keys, 'uxe.freshmart.')).toEqual([
      'uxe.freshmart.push.android.weekly-digest',
      'uxe.freshmart.push.ios.weekly-digest',
    ])
  })

  it('extends the board to five brands on three platforms each, and five features', () => {
    const keys = showcase.keys('default')
    expect(ofType(keys, 'sbt.')).toEqual([
      'sbt.bookbarn',
      'sbt.freshmart',
      'sbt.greenleaf-pharmacy',
      'sbt.petpal',
      'sbt.urban-bakery',
    ])
    expect(ofType(keys, 'rst.')).toEqual(['rst.checkout', 'rst.loyalty', 'rst.push', 'rst.store-locator', 'rst.wallet'])
    for (const brand of ['freshmart', 'petpal', 'bookbarn', 'greenleaf-pharmacy', 'urban-bakery']) {
      expect(ofType(keys, `cnt.${brand}.`), brand).toEqual([
        `cnt.${brand}.android`,
        `cnt.${brand}.ios`,
        `cnt.${brand}.web`,
      ])
    }
  })

  it('keeps the bundle id and store id on the platform, the colors on the brand', () => {
    expect(showcase.latest('default', 'schema:type:cnt')).toMatchObject({ required: ['bundleId'] })
    expect(showcase.own('default', 'cnt.freshmart.ios')).toMatchObject({
      bundleId: 'com.freshmart.app',
      storeId: '1529384761',
      appVersion: '5.4.2',
    })
    expect(showcase.own('default', 'cnt.freshmart.android')).toMatchObject({
      bundleId: 'com.freshmart.app',
      storeId: 'com.freshmart.app',
    })
    expect(showcase.own('default', 'cnt.freshmart.web')).toMatchObject({ bundleId: 'com.freshmart.app.web' })
    expect(showcase.own('default', 'sbt.freshmart')).toMatchObject({
      currency: 'GBP',
      theme: { primaryColor: '#1B8E3E', accentColor: '#FFC20E' },
    })
    // FreshMart's platforms record every release of the app; the older versions keep only their metadata.
    expect(showcase.versions('default', 'cnt.freshmart.ios')).toBeGreaterThanOrEqual(15)
  })

  it('gives every app feature the theme defaults of the template and the brand’s colors', () => {
    expect(showcase.latest('default', 'template:exe')).toMatchObject({ theme: { mode: 'system', fontFamily: 'Inter' } })
    const loyalty = 'exe.freshmart.loyalty.ios'
    expect(showcase.own('default', loyalty)).toMatchObject({ theme: { cardColor: '@config("/theme/primaryColor")' } })
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), loyalty)).toMatchObject({
      theme: { mode: 'system', fontFamily: 'Inter', primaryColor: '#1B8E3E' },
      logLevel: 'warn',
    })
  })

  it('keeps the push and payment keys in the vault and addresses APNs by the platform’s bundle id', () => {
    expect(showcase.own('default', 'exe.freshmart.push.ios')).toMatchObject({
      apnsTopic: '@config("/bundleId")',
      apnsTeamId: '@annotation("/appleTeamId", "Context")',
      apnsKey: '@(freshmart.apns-key, primaryVault)',
    })
    expect(showcase.own('default', 'exe.freshmart.push.android')).toMatchObject({
      fcmServiceAccount: '@(freshmart.fcm-service-account, primaryVault)',
    })
    expect(showcase.own('default', 'exe.freshmart.checkout.ios')).toMatchObject({
      paymentKey: '@(freshmart.adyen-api-key, primaryVault)',
      receiptReplyTo: '@annotation("/supportEmail", "Subject")',
    })
  })

  it('resolves every @config and @annotation binding of every view', () => {
    const db = unseededShowcaseDb()
    for (const view of whiteLabelApp.views.map((candidate) => candidate.name)) {
      const annotations = showcase.annotations(view)
      for (const key of annotations.keys()) {
        for (const binding of bindingsOf(showcase.own(view, key))) {
          const parts = call.exec(binding)
          if (!parts) {
            expect(binding, `${view} ${key}`).toMatch(secret)
            continue
          }
          const [, kind, pointer, type] = parts
          const value =
            kind === 'config'
              ? getAt(effectiveContent(db, showcase.scope(view), key), pointer!)
              : getAt(annotations.get(type ? ancestorKeyOf(key, type)! : key)?.Values, pointer!)
          expect(value, `${view} ${key} ${binding}`).toBeDefined()
        }
      }
    }
  })

  it('shows the lifecycle: a scheduled holiday campaign, an expired one, a feature off on the web, a scheduled license', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('unt.push.holiday-deals')).toBe('scheduled')
    expect(status('uxe.bookbarn.push.ios.holiday-deals')).toBe('scheduled')
    expect(status('unt.push.summer-sale')).toBe('expired')
    expect(status('uxe.petpal.push.android.summer-sale')).toBe('expired')
    expect(status('exe.petpal.wallet.web')).toBe('disabled')
    expect(status('usg.urban-bakery.checkout')).toBe('scheduled')
    // BookBarn gave notice on the store locator: active, with the end of the contract year ahead.
    expect(status('usg.bookbarn.store-locator')).toBe('active')
    expect(annotations.get('usg.bookbarn.store-locator')?.ExpiresAt).toBeDefined()
  })

  it('starts with a deleted brand and all of its app, and an app feature deleted alone', () => {
    const trash = showcase.trash('default')
    expect(trash.get('sbt.cornerstone-hardware')).toEqual(
      expect.arrayContaining([
        'sbt.cornerstone-hardware',
        'cnt.cornerstone-hardware.ios',
        'usg.cornerstone-hardware.loyalty',
        'exe.cornerstone-hardware.store-locator.web',
        'uxe.cornerstone-hardware.loyalty.ios.birthday-reward',
      ]),
    )
    expect(trash.get('exe.bookbarn.store-locator.web')).toEqual(['exe.bookbarn.store-locator.web'])
    expect(showcase.keys('default').has('sbt.cornerstone-hardware')).toBe(false)
  })

  it('follows the board in development: FreshMart’s Android beta with checkout and Scan & Go only', () => {
    expect([...showcase.keys('development')].toSorted()).toEqual([
      'cnt.freshmart.android-beta',
      'exe.freshmart.checkout.android-beta',
      'exe.freshmart.scan-and-go.android-beta',
      'rst.checkout',
      'rst.scan-and-go',
      'sbt.freshmart',
      'usg.freshmart.checkout',
      'usg.freshmart.scan-and-go',
    ])
    expect(showcase.own('development', 'cnt.freshmart.android-beta')).toMatchObject({
      bundleId: 'com.freshmart.app.beta',
      track: 'internal',
      logLevel: 'debug',
    })
    expect(showcase.own('development', 'exe.freshmart.checkout.android-beta')).toMatchObject({ testMode: true })
    // The beta is a separate app: testers install it from the internal test track, not from the store listing.
    const beta = showcase.annotations('development').get('cnt.freshmart.android-beta')?.Values
    expect(beta?.internalTesting).toMatch(/^https:\/\/play\.google\.com\/apps\/internaltest\/47\d{17}$/)
    expect(beta).not.toHaveProperty('storeListing')
    expect(showcase.annotations('development').get('sbt.freshmart')?.Values).toMatchObject({
      supportEmail: 'qa+freshmart@appforge.example',
    })
    // Scan & Go and the beta platform exist only here.
    for (const view of ['default', 'staging', 'release-candidate', 'feature-delivery1']) {
      expect(showcase.keys(view).has('rst.scan-and-go'), view).toBe(false)
      expect(showcase.keys(view).has('cnt.freshmart.android-beta'), view).toBe(false)
    }
  })

  it('tests staging builds of FreshMart, PetPal and the demo store', () => {
    const keys = showcase.keys('staging')
    expect(ofType(keys, 'sbt.')).toEqual(['sbt.appforge-demo', 'sbt.freshmart', 'sbt.petpal'])
    expect(showcase.own('staging', 'cnt.petpal.ios')).toMatchObject({
      bundleId: 'com.petpal.shop.staging',
      distribution: 'testflight',
    })
    expect(showcase.own('staging', 'exe.freshmart.push.ios')).toMatchObject({
      apnsEnvironment: 'sandbox',
      apnsKey: '@(freshmart.apns-key-test, primaryVault)',
    })
    // The values of a platform describe the staging app, as its configuration does.
    const annotations = showcase.annotations('staging')
    expect(annotations.get('cnt.petpal.ios')?.Values?.testFlight).toMatch(
      /^https:\/\/testflight\.apple\.com\/join\/[A-Za-z0-9]{8}$/,
    )
    expect(annotations.get('cnt.appforge-demo.ios')?.Values).not.toHaveProperty('storeListing')
    expect(annotations.get('cnt.freshmart.web')?.Values?.manifest).toBe(
      'https://staging.freshmart.example/manifest.json',
    )
    expect(showcase.own('staging', 'cnt.freshmart.web')).toMatchObject({ domain: 'staging.freshmart.example' })
    expect(showcase.annotations('default').get('cnt.freshmart.ios')?.Values).toMatchObject({
      storeListing: 'https://apps.apple.com/app/id1529384761',
    })
  })

  it('lets licenses inherit the limits of the brand’s edition, and keeps own terms where a brand has them', () => {
    const db = unseededShowcaseDb()
    expect(showcase.own('default', 'usg.petpal.checkout')).toBeUndefined()
    expect(effectiveContent(db, showcase.scope('default'), 'usg.petpal.checkout')).toMatchObject({
      edition: 'plus',
      editionLimits: { monthlyOrders: { plus: 100_000 } },
    })
    expect(showcase.own('default', 'usg.freshmart.push')).toMatchObject({
      edition: 'enterprise',
      limits: { monthlyMessages: 30_000_000 },
    })
    expect(showcase.versions('default', 'usg.freshmart.push')).toBe(2)
    expect(showcase.own('default', 'usg.bookbarn.store-locator')).toEqual({
      storeFeed: 'https://feeds.bookbarn.example/stores.json',
    })
    expect(showcase.own('default', 'usg.urban-bakery.checkout')).toMatchObject({ orderAhead: { until: '11:00' } })
  })

  it('sends Greenleaf’s refill reminders apart from the offers, which a patient may mute', () => {
    expect(showcase.own('default', 'exe.greenleaf-pharmacy.push.android')).toMatchObject({
      channels: ['offers', 'orders', 'prescriptions'],
      channelId: 'offers',
    })
    expect(showcase.own('default', 'uxe.greenleaf-pharmacy.push.android.refill-reminder')).toMatchObject({
      channelId: 'prescriptions',
    })
    expect(showcase.own('default', 'uxe.greenleaf-pharmacy.push.ios.refill-reminder')).toMatchObject({
      interruptionLevel: 'time-sensitive',
    })
  })

  it('brings release 5.5.0 in the release candidate: gift cards, a platinum tier and a tighter platform schema', () => {
    const keys = showcase.keys('release-candidate')
    expect(keys.has('rst.gift-cards')).toBe(true)
    expect(keys.has('exe.freshmart.gift-cards.ios')).toBe(true)
    expect(keys.has('exe.bookbarn.gift-cards.android')).toBe(true)
    expect(showcase.keys('default').has('rst.gift-cards')).toBe(false)
    expect(showcase.own('release-candidate', 'cnt.freshmart.ios')).toMatchObject({ appVersion: '5.5.0' })
    expect(showcase.versions('release-candidate', 'cnt.freshmart.ios')).toBe(
      showcase.versions('default', 'cnt.freshmart.ios') + 1,
    )
    expect(showcase.own('release-candidate', 'rst.loyalty')?.tiers).toContainEqual({
      name: 'platinum',
      minPoints: 10000,
    })
    expect(showcase.latest('release-candidate', 'schema:type:cnt')).toMatchObject({
      required: ['bundleId', 'appVersion'],
    })
    // What production deleted is not part of the next release.
    expect(keys.has('sbt.cornerstone-hardware')).toBe(false)
    expect(keys.has('exe.bookbarn.store-locator.web')).toBe(false)
  })

  it('delivers click & collect to FreshMart’s native apps in feature delivery 1', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(ofType(keys, 'exe.')).toEqual([
      'exe.freshmart.checkout.android',
      'exe.freshmart.checkout.ios',
      'exe.freshmart.click-and-collect.android',
      'exe.freshmart.click-and-collect.ios',
    ])
    expect(keys.has('cnt.freshmart.web')).toBe(false)
    expect(showcase.keys('default').has('rst.click-and-collect')).toBe(false)
  })
})
