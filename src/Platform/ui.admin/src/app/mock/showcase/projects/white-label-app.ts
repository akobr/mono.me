import type { Meta, ScopeBuilder, UnitMeta } from '../builder'
import { evolve } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews, type EnvironmentView } from '../views'

// Example 10 of the Miro board: AppForge builds one mobile app that retail brands publish under their own name. A
// brand (subject) licenses features (responsibilities) and ships the app on its platforms (contexts); an app feature
// (execution) is one licensed feature on one platform of the brand. Campaigns are the units of a feature, and a
// campaign run is one campaign on one platform of a brand. The brand carries its colors and logo, the platform its
// bundle id and store id, the app feature its keys, which stay in the vault.

const docs = 'https://docs.appforge.example/white-label'
const cdn = (brand: string, file: string) => `https://cdn.appforge.example/brands/${brand}/${file}`

type Platform = 'ios' | 'android' | 'web'
type Feature =
  'loyalty' | 'checkout' | 'push' | 'store-locator' | 'wallet' | 'scan-and-go' | 'gift-cards' | 'click-and-collect'
type Edition = 'standard' | 'plus' | 'enterprise'

const all: Platform[] = ['ios', 'android', 'web']
const native: Platform[] = ['ios', 'android']
const platformTitles: Record<Platform, string> = { ios: 'iOS', android: 'Android', web: 'Web' }

interface Stage {
  /** The app release the view's builds carry. */
  appVersion: string
  /** Test builds are separate apps: their bundle ids get a suffix. */
  suffix: string
  /** The host of the web app, under the brand's domain. */
  host: string
  api: string
  logLevel: 'debug' | 'info' | 'warn'
  test: boolean
}

const live: Stage = {
  appVersion: '5.4.2',
  suffix: '',
  host: 'app',
  api: 'https://api.appforge.example/v3',
  logLevel: 'warn',
  test: false,
}

/** What the builds of each environment look like; the release candidate is production with the next release. */
const stages: Record<EnvironmentView, Stage> = {
  default: live,
  'release-candidate': { ...live, appVersion: '5.5.0' },
  staging: {
    appVersion: '5.5.0-rc.1',
    suffix: '.staging',
    host: 'staging',
    api: 'https://api.staging.appforge.example/v3',
    logLevel: 'info',
    test: true,
  },
  development: {
    appVersion: '5.6.0-beta.2',
    suffix: '.beta',
    host: 'dev',
    api: 'https://api.dev.appforge.example/v3',
    logLevel: 'debug',
    test: true,
  },
  'feature-delivery1': {
    appVersion: '5.6.0-preview.1',
    suffix: '.preview',
    host: 'preview',
    api: 'https://api.preview.appforge.example/v3',
    logLevel: 'info',
    test: true,
  },
}

const stageOf = (b: ScopeBuilder) => stages[b.view as EnvironmentView]

/** The store releases of the app, oldest first, one a week; FreshMart's platforms record every one of them. */
const releases = [
  '4.9.0',
  '4.9.1',
  '4.10.0',
  '4.10.1',
  '4.11.0',
  '4.12.0',
  '4.12.1',
  '4.12.2',
  '5.0.0',
  '5.0.1',
  '5.1.0',
  '5.1.1',
  '5.2.0',
  '5.2.1',
  '5.2.2',
  '5.3.0',
  '5.3.1',
  '5.4.0',
  '5.4.1',
  '5.4.2',
]

const features: Record<Feature, { title: string; labels: string[]; description: string; since: number }> = {
  loyalty: {
    title: 'Loyalty',
    labels: ['engagement'],
    description: 'Points, tiers and the digital loyalty card, in the app and in the phone’s wallet.',
    since: 820,
  },
  checkout: {
    title: 'Checkout',
    labels: ['commerce'],
    description: 'Basket, payment and receipts; cards, Apple Pay and Google Pay through the brand’s payment provider.',
    since: 820,
  },
  push: {
    title: 'Push notifications',
    labels: ['engagement'],
    description: [
      'Sends the app’s notifications through **APNs** on iOS and **FCM** on Android.',
      '',
      '- Quiet hours follow the brand’s time zone.',
      '- Campaigns need the member’s marketing opt-in; order and pharmacy messages do not.',
    ].join('\n'),
    since: 820,
  },
  'store-locator': {
    title: 'Store locator',
    labels: ['stores'],
    description: 'Nearby stores with opening hours and directions, from the brand’s store feed.',
    since: 500,
  },
  wallet: {
    title: 'Wallet',
    labels: ['engagement', 'commerce'],
    description: 'Coupons as passes in Apple Wallet and Google Wallet.',
    since: 380,
  },
  // Features of one view only: Scan & Go in development, gift cards in the release candidate, click & collect in
  // feature delivery 1.
  'scan-and-go': {
    title: 'Scan & Go',
    labels: ['commerce', 'beta'],
    description: 'Shoppers scan the items while they shop and pay at the exit gate, without a checkout lane.',
    since: 40,
  },
  'gift-cards': {
    title: 'Gift cards',
    labels: ['commerce'],
    description: 'Digital gift cards bought in the app and kept in the wallet; ships with release 5.5.0.',
    since: 12,
  },
  'click-and-collect': {
    title: 'Click & collect',
    labels: ['commerce', 'stores'],
    description: 'Order in the app and pick up in a time slot at the chosen store.',
    since: 25,
  },
}

/** The features of the production catalog. */
const catalog: Feature[] = ['loyalty', 'checkout', 'push', 'store-locator', 'wallet']

interface Brand {
  name: string
  title: string
  legalName: string
  domain: string
  /** Labels: the retail segment and the market. */
  segment: string
  market: string
  timeZone: string
  locales: string[]
  currency: string
  edition: Edition
  /** Days since the brand's app went live. */
  since: number
  colors: { primary: string; accent: string }
  /** The bundle id of the native apps; the web app's manifest id adds ".web". */
  bundleId: string
  appStoreId: string
  appleTeamId: string
  crmId: string
  accountManager: string
  supportEmail: string
  payments: { provider: 'adyen' | 'stripe'; merchantAccount: string }
  /** The platforms the brand publishes the app on; all three by default. */
  platforms?: Platform[]
  /** The licensed features and the platforms each one ships on. */
  features: Partial<Record<Feature, Platform[]>>
  /** The store release the brand is on, when it is behind the newest one. */
  appVersion?: string
  /** The lifecycle of a license and of its app features: a start date, a contract end. */
  licenses?: Partial<Record<Feature, Meta>>
  /** The terms of a license beyond the limits of the brand's edition, its own configuration. */
  terms?: Partial<Record<Feature, Record<string, unknown>>>
  /** App features switched off, by "<feature>.<platform>", with the reason (Markdown). */
  off?: Record<string, string>
}

const brands: Brand[] = [
  {
    name: 'freshmart',
    title: 'FreshMart',
    legalName: 'FreshMart Stores Ltd',
    domain: 'freshmart.example',
    segment: 'grocery',
    market: 'uk',
    timeZone: 'Europe/London',
    locales: ['en-GB', 'cy-GB'],
    currency: 'GBP',
    edition: 'enterprise',
    since: 720,
    colors: { primary: '#1B8E3E', accent: '#FFC20E' },
    bundleId: 'com.freshmart.app',
    appStoreId: '1529384761',
    appleTeamId: 'F7M2Q8K4TR',
    crmId: 'AF-CRM-0042',
    accountManager: 'Hedy Lamarr',
    supportEmail: 'app-support@freshmart.example',
    payments: { provider: 'adyen', merchantAccount: 'FreshMartGB_App' },
    // The board: loyalty on all three platforms, checkout and push in the native apps.
    features: { loyalty: all, checkout: native, push: native },
  },
  {
    name: 'petpal',
    title: 'PetPal',
    legalName: 'PetPal Supplies, Inc.',
    domain: 'petpal.example',
    segment: 'pets',
    market: 'us',
    timeZone: 'America/Chicago',
    locales: ['en-US', 'es-US'],
    currency: 'USD',
    edition: 'plus',
    since: 540,
    colors: { primary: '#E4572E', accent: '#29335C' },
    bundleId: 'com.petpal.shop',
    appStoreId: '1561047283',
    appleTeamId: 'P3T9L2X6WA',
    crmId: 'AF-CRM-0057',
    accountManager: 'Edsger Dijkstra',
    supportEmail: 'help@petpal.example',
    payments: { provider: 'stripe', merchantAccount: 'acct_1MqPetPalUS042' },
    features: { loyalty: native, checkout: all, push: native, wallet: all },
    off: {
      'wallet.web':
        'Off on the web: coupon passes need **Apple Wallet** or **Google Wallet**. The web app shows the coupons on the account page until the PWA wallet ships.',
    },
  },
  {
    name: 'bookbarn',
    title: 'BookBarn',
    legalName: 'BookBarn Books Ltd.',
    domain: 'bookbarn.example',
    segment: 'books',
    market: 'ca',
    timeZone: 'America/Toronto',
    locales: ['en-CA', 'fr-CA'],
    currency: 'CAD',
    edition: 'plus',
    since: 430,
    colors: { primary: '#7B3F00', accent: '#F2C14E' },
    bundleId: 'ca.bookbarn.app',
    appStoreId: '1574820396',
    appleTeamId: 'B8K4N2R7CE',
    crmId: 'AF-CRM-0063',
    accountManager: 'Hedy Lamarr',
    supportEmail: 'support@bookbarn.example',
    payments: { provider: 'stripe', merchantAccount: 'acct_1NbBookBarnCA07' },
    features: { loyalty: native, checkout: all, push: native, 'store-locator': all },
    licenses: {
      // BookBarn moves to the store finder of its new website; the license runs to the end of the contract year.
      'store-locator': {
        expiresIn: 70,
        labels: ['notice-given'],
        modifiedDaysAgo: 15,
        modifiedBy: 'hedy',
      },
    },
  },
  {
    name: 'greenleaf-pharmacy',
    title: 'Greenleaf Pharmacy',
    legalName: 'Greenleaf Apotheek B.V.',
    domain: 'greenleaf-pharmacy.example',
    segment: 'pharmacy',
    market: 'nl',
    timeZone: 'Europe/Amsterdam',
    locales: ['nl-NL', 'en-GB'],
    currency: 'EUR',
    edition: 'enterprise',
    since: 300,
    colors: { primary: '#2A9D8F', accent: '#264653' },
    bundleId: 'nl.greenleaf.pharmacy',
    appStoreId: '1608812457',
    appleTeamId: 'G2L7F9Q3PH',
    crmId: 'AF-CRM-0071',
    accountManager: 'Edsger Dijkstra',
    supportEmail: 'app@greenleaf-pharmacy.example',
    payments: { provider: 'adyen', merchantAccount: 'GreenleafNL_App' },
    features: { checkout: all, push: native, 'store-locator': native, wallet: native },
  },
  {
    name: 'urban-bakery',
    title: 'Urban Bakery',
    legalName: 'Urban Bakery Pty Ltd',
    domain: 'urban-bakery.example',
    segment: 'food',
    market: 'au',
    timeZone: 'Australia/Melbourne',
    locales: ['en-AU'],
    currency: 'AUD',
    edition: 'standard',
    since: 150,
    colors: { primary: '#C97B3D', accent: '#3D2B1F' },
    bundleId: 'au.urbanbakery.app',
    appStoreId: '1689023514',
    appleTeamId: 'U4B6K8R2YD',
    crmId: 'AF-CRM-0088',
    accountManager: 'Hedy Lamarr',
    supportEmail: 'hello@urban-bakery.example',
    payments: { provider: 'stripe', merchantAccount: 'acct_1PxUrbanBakeryAU' },
    features: { loyalty: all, push: native, 'store-locator': native, checkout: native },
    appVersion: '5.4.1',
    licenses: {
      // Order-ahead for the morning rush goes live with the summer menu.
      checkout: {
        startsIn: 21,
        createdDaysAgo: 9,
        createdBy: 'hedy',
        description: 'Order ahead and pay in the app; live with the summer menu.',
      },
    },
    // Orders for the morning rush only, ready ten minutes after payment.
    terms: { checkout: { orderAhead: { readyInMinutes: 10, until: '11:00' }, tableService: false } },
  },
  {
    // Ended its contract and left with its whole app (deleted in production).
    name: 'cornerstone-hardware',
    title: 'Cornerstone Hardware',
    legalName: 'Cornerstone Hardware LLC',
    domain: 'cornerstone-hardware.example',
    segment: 'diy',
    market: 'us',
    timeZone: 'America/Denver',
    locales: ['en-US'],
    currency: 'USD',
    edition: 'standard',
    since: 610,
    colors: { primary: '#B22222', accent: '#4A4A4A' },
    bundleId: 'com.cornerstonehardware.app',
    appStoreId: '1547736209',
    appleTeamId: 'C5H3W7D9NE',
    crmId: 'AF-CRM-0049',
    accountManager: 'Edsger Dijkstra',
    supportEmail: 'app@cornerstone-hardware.example',
    payments: { provider: 'stripe', merchantAccount: 'acct_1LkCornerstone09' },
    features: { loyalty: native, 'store-locator': all },
    appVersion: '5.2.2',
  },
]

/** AppForge's own test tenant: every feature, for QA and sales demos; it exists in staging only. */
const demoBrand: Brand = {
  name: 'appforge-demo',
  title: 'AppForge Demo',
  legalName: 'AppForge Ltd',
  domain: 'demo.appforge.example',
  segment: 'demo',
  market: 'ie',
  timeZone: 'Europe/Dublin',
  locales: ['en-IE', 'de-DE', 'fr-FR'],
  currency: 'EUR',
  edition: 'enterprise',
  since: 200,
  colors: { primary: '#5B21B6', accent: '#F59E0B' },
  bundleId: 'com.appforge.demo',
  appStoreId: '1600400117',
  appleTeamId: 'A9F4R2G7DM',
  crmId: 'AF-CRM-0001',
  accountManager: 'Edsger Dijkstra',
  supportEmail: 'demo@appforge.example',
  payments: { provider: 'adyen', merchantAccount: 'AppForgeDemo_ECOM' },
  features: { loyalty: all, checkout: all, push: native, 'store-locator': all, wallet: native },
}

const brandNamed = (name: string) => brands.find((brand) => brand.name === name)!

/** Removed from production: Cornerstone Hardware with its whole app, and BookBarn's store locator on the web. */
const deletions: { key: string; daysAgo: number; by: PersonaId }[] = [
  { key: 'sbt.cornerstone-hardware', daysAgo: 24, by: 'edsger' },
  { key: 'exe.bookbarn.store-locator.web', daysAgo: 6, by: 'hedy' },
]

/** The release candidate starts from production as it is now, so what production deleted is not there. */
const deletedFromProduction = (b: ScopeBuilder, key: string) =>
  b.view === 'release-candidate' && deletions.some((deletion) => deletion.key === key)

interface Campaign {
  feature: Feature
  name: string
  meta: UnitMeta
  /** The campaign's documents, oldest first. */
  content: Record<string, unknown>[]
  /** The brands that run it, on the native platforms unless `platforms` says otherwise. */
  brands: string[]
  platforms?: Platform[]
  /** The own configuration of a brand's runs. */
  runs?: Record<string, Record<string, unknown>>
  /** How the runs deliver on each platform, where it is not the push app feature's default. */
  delivery?: Partial<Record<Platform, Record<string, unknown>>>
}

const campaigns: Campaign[] = [
  {
    feature: 'push',
    name: 'weekly-digest',
    meta: {
      unitType: 'time',
      definition: '0 9 * * 1',
      title: 'Weekly digest',
      description: 'This week’s offers, every Monday at 09:00 in the brand’s time zone.',
      labels: ['always-on'],
      createdDaysAgo: 600,
      createdBy: 'hedy',
    },
    content: [
      { template: 'weekly-digest-v2', audience: 'marketing-opt-in', maxOffers: 10, deepLink: 'app://offers/weekly' },
      { template: 'weekly-digest-v3', audience: 'marketing-opt-in', maxOffers: 10, deepLink: 'app://offers/weekly' },
      { template: 'weekly-digest-v3', audience: 'marketing-opt-in', maxOffers: 8, deepLink: 'app://offers/weekly' },
    ],
    brands: ['freshmart', 'petpal', 'bookbarn', 'greenleaf-pharmacy', 'urban-bakery', 'appforge-demo'],
    // FreshMart sends an hour earlier, before the school run, with fewer offers.
    runs: { freshmart: { schedule: '0 8 * * 1', maxOffers: 6 } },
  },
  {
    feature: 'push',
    name: 'holiday-deals',
    meta: {
      unitType: 'time',
      definition: '0 10 * 11,12 *',
      title: 'Holiday deals',
      description: 'A deal a day from Black Friday to New Year’s Eve.',
      labels: ['seasonal'],
      // From Black Friday to the end of New Year's Eve in Toronto, the westernmost brand that runs it.
      startsIn: 37,
      expiresIn: 72,
      createdDaysAgo: 6,
      createdBy: 'hedy',
    },
    content: [{ template: 'holiday-2026', audience: 'marketing-opt-in', deepLink: 'app://offers/holiday' }],
    brands: ['bookbarn', 'urban-bakery'],
    runs: { bookbarn: { locales: ['en-CA', 'fr-CA'] } },
  },
  {
    feature: 'push',
    name: 'summer-sale',
    meta: {
      unitType: 'time',
      definition: '0 11 * 6-8 *',
      title: 'Summer sale',
      description: 'Ended on 31 August.',
      labels: ['seasonal'],
      expiresIn: -51,
      createdDaysAgo: 150,
      createdBy: 'hedy',
    },
    content: [{ template: 'summer-2026', audience: 'marketing-opt-in', deepLink: 'app://offers/summer' }],
    brands: ['petpal'],
  },
  {
    feature: 'push',
    name: 'refill-reminder',
    meta: {
      unitType: 'event',
      definition: 'prescription.refill-due',
      title: 'Refill reminder',
      description: 'Reminds a patient that a repeat prescription is due; a care message, so no marketing opt-in.',
      labels: ['transactional'],
      createdDaysAgo: 280,
      createdBy: 'edsger',
    },
    content: [{ template: 'refill-reminder-v2', leadDays: 7, deepLink: 'app://prescriptions' }],
    brands: ['greenleaf-pharmacy'],
    runs: { 'greenleaf-pharmacy': { leadDays: 5, requiresPharmacistCheck: true } },
    // A patient who mutes the offers keeps the reminders: their own Android channel, and time-sensitive on iOS.
    delivery: { android: { channelId: 'prescriptions' }, ios: { interruptionLevel: 'time-sensitive' } },
  },
  {
    feature: 'loyalty',
    name: 'birthday-reward',
    meta: {
      unitType: 'event',
      definition: 'member.birthday',
      title: 'Birthday reward',
      labels: ['always-on'],
      createdDaysAgo: 500,
      createdBy: 'hedy',
    },
    content: [{ rewardPoints: 200, validDays: 30, template: 'birthday-v1' }],
    brands: ['petpal', 'cornerstone-hardware'],
  },
  {
    feature: 'checkout',
    name: 'abandoned-cart',
    meta: {
      unitType: 'message',
      definition: 'checkout-abandoned-carts',
      title: 'Abandoned cart',
      description: 'A reminder an hour after a basket was left, from the checkout events queue.',
      labels: ['always-on'],
      createdDaysAgo: 420,
      createdBy: 'ada',
    },
    content: [{ delayMinutes: 60, maxReminders: 2, template: 'abandoned-cart-v2' }],
    brands: ['petpal', 'appforge-demo'],
    platforms: all,
  },
  {
    feature: 'wallet',
    name: 'coupon-drop',
    meta: {
      unitType: 'file',
      definition: 'coupons/weekly-coupons.json',
      title: 'Coupon drop',
      description: 'Issues the coupons of the brand’s weekly file as wallet passes.',
      labels: ['always-on'],
      createdDaysAgo: 360,
      createdBy: 'hedy',
    },
    content: [{ format: 'json', maxCoupons: 10, notify: true }],
    brands: ['petpal', 'greenleaf-pharmacy'],
    runs: { petpal: { maxCoupons: 15 } },
  },
]

const campaignNamed = (name: string) => campaigns.find((campaign) => campaign.name === name)!

/** The license limit of each feature by edition; a license inherits it and the edition from its brand. */
const limits: Record<Feature, [string, Record<Edition, number>]> = {
  loyalty: ['monthlyActiveMembers', { standard: 50_000, plus: 250_000, enterprise: 2_000_000 }],
  checkout: ['monthlyOrders', { standard: 20_000, plus: 100_000, enterprise: 1_000_000 }],
  push: ['monthlyMessages', { standard: 500_000, plus: 2_500_000, enterprise: 20_000_000 }],
  'store-locator': ['maxStores', { standard: 50, plus: 300, enterprise: 2_500 }],
  wallet: ['maxActivePasses', { standard: 100_000, plus: 500_000, enterprise: 5_000_000 }],
  'scan-and-go': ['maxStores', { standard: 5, plus: 25, enterprise: 100 }],
  'gift-cards': ['monthlyCards', { standard: 5_000, plus: 25_000, enterprise: 250_000 }],
  'click-and-collect': ['monthlyOrders', { standard: 10_000, plus: 50_000, enterprise: 500_000 }],
}

/** A feature's product defaults, every version with the limits of the editions. */
function withLimits(feature: Feature, versions: Record<string, unknown>[]): Record<string, unknown>[] {
  const [metric, byEdition] = limits[feature]
  return versions.map((content) => ({ ...content, editionLimits: { [metric]: byEdition } }))
}

/** A license and its app features exist from the day both the brand's app and the feature did. */
const licensedSince = (brand: Brand, feature: Feature) =>
  brand.licenses?.[feature]?.createdDaysAgo ?? Math.min(brand.since, features[feature].since)

function addFeature(b: ScopeBuilder, name: Feature) {
  const feature = features[name]
  b.responsibility(name, {
    title: feature.title,
    description: feature.description,
    labels: feature.labels,
    link: `${docs}/features/${name}`,
    createdDaysAgo: feature.since,
    createdBy: 'edsger',
  })
}

/** Checkout's product defaults, oldest first: wallets came one by one, then 3-D Secure only when the bank asks. */
const checkoutDefaults = [
  { paymentMethods: ['card'], guestCheckout: false, maxBasketItems: 100, threeDSecure: 'always' },
  { paymentMethods: ['card', 'apple-pay'], guestCheckout: false, maxBasketItems: 100, threeDSecure: 'always' },
  {
    paymentMethods: ['card', 'apple-pay', 'google-pay'],
    guestCheckout: true,
    maxBasketItems: 100,
    threeDSecure: 'always',
  },
  {
    paymentMethods: ['card', 'apple-pay', 'google-pay'],
    guestCheckout: true,
    maxBasketItems: 150,
    threeDSecure: 'when-required',
  },
]

/**
 * The loyalty programme since May, one change every nine days, the long history of the project: a summer of double
 * points at weekends, a QR code that rotates against screenshots, higher tiers in September and a bigger welcome
 * bonus for the sign-ups before the holidays.
 */
const loyaltyTuning: Record<string, unknown>[] = [
  { referralBonus: 250 },
  { doublePointsWeekends: true },
  { maxPointsPerDay: 7500 },
  { cardFormat: 'qr-and-barcode' },
  { minRedeemPoints: 400 },
  { welcomeBonus: 150 },
  { expiryReminderDays: 30 },
  { pointsExpiryMonths: 18 },
  { referralBonus: 300 },
  { maxPointsPerDay: 10000 },
  { doublePointsWeekends: false },
  {
    tiers: [
      { name: 'member', minPoints: 0 },
      { name: 'silver', minPoints: 750 },
      { name: 'gold', minPoints: 2500 },
    ],
  },
  { qrRefreshSeconds: 60 },
  { minRedeemPoints: 300 },
  { qrRefreshSeconds: 30 },
  { expiryReminderDays: 14 },
  { welcomeBonus: 200 },
]

/** The product defaults of the catalog's features; the release candidate adds the platinum tier and gift cards. */
function featureDocuments(b: ScopeBuilder) {
  const next = b.view === 'release-candidate'
  const loyalty = evolve(
    {
      pointsPerCurrencyUnit: 1,
      welcomeBonus: 100,
      referralBonus: 200,
      minRedeemPoints: 500,
      maxPointsPerDay: 5000,
      pointsExpiryMonths: 12,
      doublePointsWeekends: false,
      cardFormat: 'qr',
      tiers: [
        { name: 'member', minPoints: 0 },
        { name: 'silver', minPoints: 500 },
        { name: 'gold', minPoints: 2000 },
      ],
    },
    loyaltyTuning.length + 1,
    (version) => loyaltyTuning[version - 2]!,
  )
  if (next) {
    const previous = loyalty.at(-1)!
    loyalty.push({
      ...previous,
      tiers: [...(previous.tiers as object[]), { name: 'platinum', minPoints: 10000 }],
    })
  }
  b.configure('rst.loyalty', withLimits('loyalty', loyalty), { every: 9, lastDaysAgo: next ? 1 : 3 })
  b.configure('rst.checkout', withLimits('checkout', checkoutDefaults), { every: 30 })
  b.configure(
    'rst.push',
    withLimits('push', [
      { quietHours: { from: '21:00', to: '08:00' }, maxPerDay: 3, marketingRequiresOptIn: true },
      { quietHours: { from: '21:00', to: '08:00' }, maxPerDay: 2, marketingRequiresOptIn: true },
    ]),
    { every: 45 },
  )
  b.configure(
    'rst.store-locator',
    withLimits('store-locator', [{ mapProvider: 'google-maps', searchRadiusKm: 20, showOpeningHours: true }]),
  )
  b.configure(
    'rst.wallet',
    withLimits('wallet', [{ passProviders: ['apple-wallet', 'google-wallet'], maxActiveCoupons: 25 }]),
  )
  if (next) {
    b.configure(
      'rst.gift-cards',
      withLimits('gift-cards', [{ denominations: [10, 25, 50, 100], expiresAfterMonths: 24, minTopUp: 5 }]),
    )
  }
}

/** The views of a new feature: the app's defaults, checkout as it is now, and the new feature. */
function newFeature(b: ScopeBuilder, feature: Feature, defaults: Record<string, unknown>[]) {
  appDefaults(b)
  platformSchema(b)
  addFeature(b, 'checkout')
  b.configure('rst.checkout', withLimits('checkout', checkoutDefaults.slice(-1)))
  addFeature(b, feature)
  b.configure(`rst.${feature}`, withLimits(feature, defaults))
}

/** Every app feature starts from the app's theme and diagnostics; the brand's colors and logo come from the brand. */
function appDefaults(b: ScopeBuilder) {
  const stage = stageOf(b)
  b.template(
    'exe',
    evolve(
      {
        theme: { mode: 'system', fontFamily: 'Inter', cornerRadius: 12 },
        apiBaseUrl: stage.api,
        logLevel: stage.logLevel,
        crashReporting: true,
      },
      3,
      (version) => (version === 2 ? { theme: { cornerRadius: 16 } } : { theme: { haptics: true } }),
    ),
    { every: 20 },
  )
}

/**
 * The stores, APNs and the web manifest know an app by its bundle id, so no platform is saved without one. The
 * release candidate also checks the forms of the bundle id and of the versions.
 */
function platformSchema(b: ScopeBuilder) {
  const semver = '^\\d+\\.\\d+\\.\\d+(-[0-9a-z.]+)?$'
  b.typeSchema('cnt', [
    {
      type: 'object',
      properties: { bundleId: { type: 'string', minLength: 3 }, appVersion: { type: 'string' } },
      required: ['bundleId'],
    },
    ...(b.view === 'release-candidate'
      ? [
          {
            type: 'object',
            properties: {
              bundleId: { type: 'string', pattern: '^[a-z][a-z0-9]*(\\.[a-z][a-z0-9-]*)+$' },
              appVersion: { type: 'string', pattern: semver },
              minSupportedVersion: { type: 'string', pattern: semver },
            },
            required: ['bundleId', 'appVersion'],
          },
        ]
      : []),
  ])
}

function addCampaigns(b: ScopeBuilder, list: Campaign[]) {
  for (const campaign of list) {
    const key = b.unit(campaign.feature, campaign.name, {
      ...campaign.meta,
      link: `${docs}/campaigns/${campaign.name}`,
    })
    b.configure(key, campaign.content, { every: 40 })
  }
}

function brandContent(brand: Brand) {
  return {
    displayName: brand.title,
    legalName: brand.legalName,
    // The edition of the brand's contract picks the limits of its licenses.
    edition: brand.edition,
    currency: brand.currency,
    locales: brand.locales,
    theme: {
      primaryColor: brand.colors.primary,
      accentColor: brand.colors.accent,
      logoUrl: cdn(brand.name, 'logo.svg'),
      logoDarkUrl: cdn(brand.name, 'logo-dark.svg'),
    },
  }
}

function brandHistory(brand: Brand): Record<string, unknown>[] {
  const current = brandContent(brand)
  if (brand.name !== 'freshmart') {
    return [current]
  }
  // FreshMart's refresh: a brighter yellow and a logo for dark mode, then the Welsh translation.
  return [
    {
      ...current,
      locales: ['en-GB'],
      theme: { primaryColor: brand.colors.primary, accentColor: '#F9B000', logoUrl: cdn(brand.name, 'logo.svg') },
    },
    { ...current, locales: ['en-GB'] },
    current,
  ]
}

function addSubject(b: ScopeBuilder, brand: Brand) {
  const test = stageOf(b).test
  b.subject(brand.name, {
    title: brand.title,
    labels: [brand.segment, brand.market],
    values: {
      crmId: brand.crmId,
      accountManager: brand.accountManager,
      // Receipts of test builds go to AppForge's QA inbox, never to the brand's customers.
      supportEmail: test ? `qa+${brand.name}@appforge.example` : brand.supportEmail,
    },
    link: `${docs}/brands/${brand.name}`,
    timeZone: brand.timeZone,
    createdDaysAgo: brand.since,
    createdBy: 'edsger',
  })
  b.configure(`sbt.${brand.name}`, brandHistory(brand), { every: 60 })
}

const alphanumeric = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
const randomCode = (b: ScopeBuilder, characters: string, length: number) =>
  Array.from({ length }, () => b.rng.pick([...characters])).join('')

/**
 * Facts about the brand's app on a platform, read by the bindings of its app features. Production links the store
 * listing; the separate apps of the test builds link where the testers install them.
 */
function platformValues(b: ScopeBuilder, brand: Brand, platform: Platform): Record<string, unknown> {
  const stage = stageOf(b)
  switch (platform) {
    case 'ios':
      return {
        appleTeamId: brand.appleTeamId,
        ...(stage.test
          ? { testFlight: `https://testflight.apple.com/join/${randomCode(b, alphanumeric, 8)}` }
          : { storeListing: `https://apps.apple.com/app/id${brand.appStoreId}` }),
      }
    case 'android':
      return {
        walletIssuerId: `338800000002${brand.appStoreId.slice(-7)}`,
        ...(stage.test
          ? { internalTesting: `https://play.google.com/apps/internaltest/47${randomCode(b, '0123456789', 17)}` }
          : { storeListing: `https://play.google.com/store/apps/details?id=${brand.bundleId}` }),
      }
    case 'web':
      return { hosting: 'appforge-edge', manifest: `https://${stage.host}.${brand.domain}/manifest.json` }
  }
}

function platformContent(brand: Brand, platform: Platform, appVersion: string, stage: Stage): Record<string, unknown> {
  const bundleId = `${brand.bundleId}${stage.suffix}`
  switch (platform) {
    case 'ios':
      return {
        bundleId,
        ...(stage.test ? { distribution: 'testflight' } : { storeId: brand.appStoreId, distribution: 'app-store' }),
        minOsVersion: '16.0',
        appVersion,
        minSupportedVersion: '5.0.0',
      }
    case 'android':
      return {
        bundleId,
        // Google Play knows an app by its package name.
        storeId: bundleId,
        track: stage.test ? 'internal' : 'production',
        minSdk: 26,
        appVersion,
        minSupportedVersion: '5.0.0',
      }
    case 'web':
      return { bundleId: `${bundleId}.web`, domain: `${stage.host}.${brand.domain}`, appVersion }
  }
}

/** The versions of a platform's document: production keeps the brand's last releases, test builds one version. */
function platformHistory(b: ScopeBuilder, brand: Brand, platform: Platform): Record<string, unknown>[] {
  const stage = stageOf(b)
  const next = b.view === 'release-candidate'
  if (b.view !== 'default' && !next) {
    return [platformContent(brand, platform, stage.appVersion, stage)]
  }
  const current = brand.appVersion ?? releases.at(-1)!
  let history: Record<string, unknown>[]
  if (brand.name === 'freshmart' && platform !== 'web') {
    // FreshMart ships every release the week it is out; 5.0.0 raised the minimum OS and the forced update.
    const [os, before, after] = platform === 'ios' ? ['minOsVersion', '15.0', '16.0'] : ['minSdk', 24, 26]
    history = evolve(
      { ...platformContent(brand, platform, releases[0]!, stage), [os]: before, minSupportedVersion: '4.6.0' },
      releases.length,
      (version) => ({
        appVersion: releases[version - 1]!,
        ...(version === 9 ? { [os]: after, minSupportedVersion: '4.10.0' } : {}),
        ...(version === 15 ? { minSupportedVersion: '5.0.0' } : {}),
      }),
    )
  } else if (platform === 'web') {
    history = [platformContent(brand, platform, current, stage)]
  } else {
    const previous = releases[releases.indexOf(current) - 1]!
    history = [platformContent(brand, platform, previous, stage), platformContent(brand, platform, current, stage)]
  }
  if (next) {
    history.push(platformContent(brand, platform, stage.appVersion, stage))
  }
  return history
}

/** A brand's context of a platform: its name and title, and its age where it is newer than the brand's app. */
interface PlatformContext {
  name: string
  title: string
  since?: number
}

const contextOf = (platform: Platform): PlatformContext => ({ name: platform, title: platformTitles[platform] })

/** FreshMart's Android beta, the only platform of the development view. */
const androidBeta: PlatformContext = { name: 'android-beta', title: 'Android beta', since: 45 }

interface PlatformOptions {
  context?: PlatformContext
  /** Metadata over the platform's; its values are added to the platform's. */
  meta?: Meta
  /** Settings added to the latest document. */
  settings?: Record<string, unknown>
}

function addPlatform(b: ScopeBuilder, brand: Brand, platform: Platform, options: PlatformOptions = {}) {
  const { context = contextOf(platform), meta: { values, ...meta } = {}, settings } = options
  const key = b.context(brand.name, context.name, {
    title: `${brand.title} ${context.title}`,
    labels: [platform === 'web' ? 'pwa' : 'native'],
    timeZone: brand.timeZone,
    createdDaysAgo: context.since ?? brand.since,
    createdBy: 'edsger',
    ...meta,
    values: { ...platformValues(b, brand, platform), ...values },
  })
  const history = platformHistory(b, brand, platform)
  if (settings) {
    history.push({ ...history.pop(), ...settings })
  }
  const every = brand.name === 'freshmart' && platform !== 'web' ? { every: 7, lastDaysAgo: 2 } : { every: 12 }
  b.configure(key, history, every)
}

/**
 * The own terms of a license: the feeds the feature reads and what the brand negotiated. Licenses without them
 * inherit the limits of the brand's edition from the feature.
 */
function licenseTerms(brand: Brand, feature: Feature): Record<string, unknown> | undefined {
  const terms = {
    ...(feature === 'store-locator' ? { storeFeed: `https://feeds.${brand.domain}/stores.json` } : {}),
    ...(feature === 'click-and-collect' ? { pickupSlotsFeed: `https://feeds.${brand.domain}/pickup-slots.json` } : {}),
    ...brand.terms?.[feature],
  }
  return Object.keys(terms).length ? terms : undefined
}

function addLicense(b: ScopeBuilder, brand: Brand, feature: Feature) {
  const { labels = [], ...lifecycle } = brand.licenses?.[feature] ?? {}
  const key = b.usage(brand.name, feature, {
    title: `${brand.title} ${features[feature].title} license`,
    labels: [brand.edition, ...labels],
    values: { contractId: `AF-${brand.crmId.slice(-4)}-${feature.slice(0, 3).toUpperCase()}` },
    timeZone: brand.timeZone,
    createdDaysAgo: licensedSince(brand, feature),
    createdBy: 'edsger',
    ...lifecycle,
  })
  if (brand.name === 'freshmart' && feature === 'push' && !stageOf(b).test) {
    // FreshMart's push came on the plus edition; before the holiday season it moved to enterprise, with a cap above
    // the edition's.
    b.configure(key, [{ edition: 'plus' }, { edition: 'enterprise', limits: { monthlyMessages: 30_000_000 } }], {
      every: 20,
    })
    return
  }
  const terms = licenseTerms(brand, feature)
  if (terms) {
    b.configure(key, terms)
  }
}

/** What an app feature stores per platform: the platform's ids, its theme and its keys, bound to the vault. */
function appFeatureContent(
  brand: Brand,
  feature: Feature,
  platform: Platform,
  stage: Stage,
): Record<string, unknown> | undefined {
  const bundleId = `${brand.bundleId}${stage.suffix}`
  const secret = (name: string) => `@(${brand.name}.${name}${stage.test ? '-test' : ''}, primaryVault)`
  const walletIssuer = '@annotation("/walletIssuerId", "Context")'
  // The loyalty card takes the brand's color from the brand's own configuration.
  const card = { theme: { cardColor: '@config("/theme/primaryColor")' } }
  switch (feature) {
    case 'loyalty':
      return platform === 'ios'
        ? { walletPass: 'apple-wallet', passTypeId: `pass.${bundleId}.loyalty`, ...card }
        : platform === 'android'
          ? { walletPass: 'google-wallet', issuerId: walletIssuer, ...card }
          : { cardFormat: 'qr', addToHomeScreen: true }
    case 'checkout': {
      const { provider, merchantAccount } = brand.payments
      return {
        paymentProvider: provider,
        merchantAccount,
        paymentKey: secret(`${provider}-api-key`),
        receiptReplyTo: '@annotation("/supportEmail", "Subject")',
        ...(platform === 'ios' ? { applePayMerchantId: `merchant.${bundleId}` } : {}),
        ...(platform === 'android' ? { googlePayEnvironment: stage.test ? 'TEST' : 'PRODUCTION' } : {}),
        // No device biometrics on the web, so every card payment asks the bank.
        ...(platform === 'web' ? { threeDSecure: 'always' } : {}),
        ...(stage.test ? { testMode: true } : {}),
      }
    }
    case 'push':
      return platform === 'ios'
        ? {
            // APNs addresses the app by its bundle id and the developer team of its listing.
            apnsTopic: '@config("/bundleId")',
            apnsTeamId: '@annotation("/appleTeamId", "Context")',
            apnsKey: secret('apns-key'),
            apnsEnvironment: stage.test ? 'sandbox' : 'production',
          }
        : {
            fcmProjectId: `${brand.name}-app${stage.test ? '-test' : ''}`,
            fcmServiceAccount: secret('fcm-service-account'),
            // Android users mute a channel at a time; campaigns go to offers, the pharmacy's care messages apart.
            channels: ['offers', 'orders', ...(brand.segment === 'pharmacy' ? ['prescriptions'] : [])],
            channelId: 'offers',
          }
    case 'store-locator':
      // Android and the web keep the feature's Google Maps.
      return platform === 'ios' ? { mapProvider: 'apple-maps' } : undefined
    case 'wallet':
      return platform === 'ios'
        ? { passTypeId: `pass.${bundleId}.coupons`, passCertificate: secret('pass-certificate') }
        : platform === 'android'
          ? { issuerId: walletIssuer, couponClass: `${brand.name}-coupon` }
          : { fallback: 'account-page' }
    case 'scan-and-go':
      return { barcodeFormats: ['ean-13', 'qr'], exitGate: 'qr', ...(stage.test ? { debugOverlay: true } : {}) }
    case 'gift-cards':
      return {
        designs: ['classic', 'birthday', 'holiday'],
        walletPass: platform === 'ios' ? 'apple-wallet' : 'google-wallet',
      }
    case 'click-and-collect':
      return { notifyWhenReady: true, ...(platform === 'ios' ? { liveActivity: true } : {}) }
  }
}

function addAppFeature(
  b: ScopeBuilder,
  brand: Brand,
  feature: Feature,
  platform: Platform,
  context = contextOf(platform),
) {
  const license = brand.licenses?.[feature] ?? {}
  const reason = brand.off?.[`${feature}.${platform}`]
  const key = b.execution(brand.name, feature, context.name, {
    title: `${brand.title} ${features[feature].title} on ${context.title}`,
    timeZone: brand.timeZone,
    createdDaysAgo: Math.min(licensedSince(brand, feature), context.since ?? brand.since),
    createdBy: license.createdBy ?? 'edsger',
    startsIn: license.startsIn,
    expiresIn: license.expiresIn,
    ...(reason ? { disabled: true, description: reason, modifiedDaysAgo: 40, modifiedBy: 'ada' as const } : {}),
  })
  const content = appFeatureContent(brand, feature, platform, stageOf(b))
  if (content) {
    b.configure(key, content)
  }
}

/** The runs of the view's campaigns on the brand's app features. */
function addCampaignRuns(b: ScopeBuilder, brand: Brand) {
  for (const campaign of campaigns) {
    if (!campaign.brands.includes(brand.name) || !b.has(`unt.${campaign.feature}.${campaign.name}`)) {
      continue
    }
    for (const platform of campaign.platforms ?? native) {
      if (!b.has(`exe.${brand.name}.${campaign.feature}.${platform}`)) {
        continue
      }
      const key = b.unitOfExecution(brand.name, campaign.feature, platform, campaign.name, {
        title: `${brand.title} ${campaign.meta.title} on ${platformTitles[platform]}`,
        timeZone: brand.timeZone,
        createdDaysAgo: Math.min(licensedSince(brand, campaign.feature), campaign.meta.createdDaysAgo ?? 400),
        createdBy: campaign.meta.createdBy,
        startsIn: campaign.meta.startsIn,
        expiresIn: campaign.meta.expiresIn,
      })
      const own = { ...campaign.runs?.[brand.name], ...campaign.delivery?.[platform] }
      if (Object.keys(own).length) {
        b.configure(key, own)
      }
    }
  }
}

/** A brand with its platforms, licenses, app features and campaign runs. */
function addBrand(b: ScopeBuilder, brand: Brand) {
  if (deletedFromProduction(b, `sbt.${brand.name}`)) {
    return
  }
  addSubject(b, brand)
  for (const platform of brand.platforms ?? all) {
    addPlatform(b, brand, platform)
  }
  for (const [feature, platforms] of Object.entries(brand.features) as [Feature, Platform[]][]) {
    addLicense(b, brand, feature)
    for (const platform of platforms) {
      if (!deletedFromProduction(b, `exe.${brand.name}.${feature}.${platform}`)) {
        addAppFeature(b, brand, feature, platform)
      }
    }
  }
  addCampaignRuns(b, brand)
}

/** Production, and the release candidate: release 5.5.0 with gift cards for FreshMart and BookBarn. */
function production(b: ScopeBuilder) {
  const next = b.view === 'release-candidate'
  appDefaults(b)
  platformSchema(b)
  for (const feature of next ? [...catalog, 'gift-cards' as const] : catalog) {
    addFeature(b, feature)
  }
  featureDocuments(b)
  addCampaigns(b, campaigns)
  for (const brand of brands) {
    const giftCards = next && ['freshmart', 'bookbarn'].includes(brand.name)
    addBrand(b, giftCards ? { ...brand, features: { ...brand.features, 'gift-cards': native } } : brand)
  }
  if (!next) {
    deletions.forEach(({ key, ...deletion }) => b.trash(key, deletion))
  }
}

/** The board's development view: FreshMart's Android beta with checkout and the new Scan & Go, nothing else. */
function development(b: ScopeBuilder) {
  const freshmart = brandNamed('freshmart')
  newFeature(b, 'scan-and-go', [
    { maxBasketItems: 40, randomAuditRate: 0.1, exitGate: 'qr' },
    { maxBasketItems: 60, randomAuditRate: 0.05, exitGate: 'qr' },
  ])
  addSubject(b, freshmart)
  addPlatform(b, freshmart, 'android', {
    context: androidBeta,
    meta: { labels: ['native', 'beta'], values: { testers: 'freshmart-beta@appforge.example' }, createdBy: 'ada' },
    settings: { logLevel: 'debug', strictMode: true },
  })
  for (const feature of ['checkout', 'scan-and-go'] as const) {
    addLicense(b, freshmart, feature)
    addAppFeature(b, freshmart, feature, 'android', androidBeta)
  }
}

/** Staging builds of FreshMart and PetPal, and AppForge's demo store with every feature. */
function staging(b: ScopeBuilder) {
  appDefaults(b)
  platformSchema(b)
  catalog.forEach((feature) => addFeature(b, feature))
  featureDocuments(b)
  addCampaigns(b, [campaignNamed('weekly-digest'), campaignNamed('abandoned-cart')])
  for (const brand of [brandNamed('freshmart'), brandNamed('petpal'), demoBrand]) {
    addBrand(b, brand)
  }
}

/** Click & collect, built for FreshMart's native apps first. */
function featureDelivery(b: ScopeBuilder) {
  newFeature(b, 'click-and-collect', [{ pickupSlotMinutes: 30, holdHours: 48, maxOrdersPerSlot: 12 }])
  addBrand(b, {
    ...brandNamed('freshmart'),
    platforms: native,
    features: { checkout: native, 'click-and-collect': native },
  })
}

export const whiteLabelApp: ShowcaseProject = {
  org: 'appforge',
  project: 'white-label-app',
  summary: 'A white-label mobile app: retail brands, their platforms and the features licensed to each brand.',
  members: { edsger: 'Owner', hedy: 'Administrator', ada: 'Contributor', alan: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Feature', color: 'violet', icon: 'i-lucide-puzzle' },
      unt: { label: 'Campaign', color: 'pink', icon: 'i-lucide-bell' },
      sbt: { label: 'Brand', color: 'blue', icon: 'i-lucide-store' },
      usg: { label: 'Feature license', color: 'amber', icon: 'i-lucide-key' },
      cnt: { label: 'Platform', color: 'cyan', icon: 'i-lucide-smartphone' },
      exe: { label: 'App feature', color: 'emerald', icon: 'i-lucide-layout-grid' },
      uxe: { label: 'CampaignRun', color: 'orange', icon: 'i-lucide-play' },
    },
    defaultTimeZone: 'Europe/Dublin',
    dateFormat: 'd mmm yyyy',
  },
  views: environmentViews({
    development: 'Development: Scan & Go on the FreshMart Android beta',
    staging: 'Staging builds of FreshMart, PetPal and the demo store',
    'release-candidate': 'Release 5.5.0: gift cards and a platinum loyalty tier',
    'feature-delivery1': 'Click & collect for FreshMart',
  }),
  build: (b) => {
    switch (b.view) {
      case 'default':
      case 'release-candidate':
        return production(b)
      case 'development':
        return development(b)
      case 'staging':
        return staging(b)
      case 'feature-delivery1':
        return featureDelivery(b)
    }
  },
}
