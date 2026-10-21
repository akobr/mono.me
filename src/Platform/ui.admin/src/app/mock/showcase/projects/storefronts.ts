import type { Meta, ScopeBuilder, UnitMeta } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// Velvet Retail runs three brand storefronts on one commerce platform across European markets (spec 9.13, a new
// example). A storefront is a subject and a market (a country with its storefront language) is a context of it; the
// commerce capabilities are responsibilities, and a capability set up in one market is an execution. What differs by
// market lives on those market setups: the currency and the locale come from the market, the VAT rate from the tax
// engine, the carriers from shipping and the payment methods from the payment provider.

const docs = 'https://docs.velvet-retail.example/commerce'

type Capability =
  | 'checkout'
  | 'payment-provider'
  | 'shipping'
  | 'tax-engine'
  | 'search'
  | 'reviews'
  | 'gift-cards'
  | 'returns-portal'
  | 'buy-now-pay-later'

type MarketName = 'de-de' | 'at-de' | 'cs-cz' | 'pl-pl' | 'fr-fr' | 'en-gb' | 'it-it' | 'en-xa'
type Currency = 'EUR' | 'CZK' | 'PLN' | 'GBP'

/** Where a catalog runs: the host of its storefronts, whether payments and mails are test ones, and the log level. */
type Stage = 'production' | 'staging' | 'development' | 'feature'

const stages: Record<Stage, { host: string; test: boolean; logLevel: string }> = {
  production: { host: 'www', test: false, logLevel: 'warn' },
  staging: { host: 'staging', test: true, logLevel: 'info' },
  development: { host: 'dev', test: true, logLevel: 'debug' },
  // A feature is deployed below the preview host, as bnpl.preview.velvet-fashion.example.
  feature: { host: 'preview', test: true, logLevel: 'debug' },
}

interface Market {
  country: string
  /** The country in a sentence, where it needs an article; the country by default. */
  inCountry?: string
  locale: string
  currency: Currency
  /** Standard and reduced VAT in percent. */
  vatRate: number
  reducedVatRate: number
  /** How the VAT is filed: at home in Germany, through the EU One-Stop Shop, or with HMRC (Making Tax Digital). */
  filing: 'domestic' | 'oss' | 'mtd'
  timeZone: string
  region: string
  carriers: string[]
  /** The methods the payment provider (Adyen) processes. */
  paymentMethods: string[]
  /** The carrier collects cash at the door, for a fee at the checkout. */
  cashOnDelivery?: boolean
  /** Last order time for same-day dispatch, local time. */
  cutOffTime: string
  freeShippingFrom: number
  /** A value of the market: delivery exceptions go to the market manager. */
  manager: string
}

/** The markets of the group. en-xa is Android's accented-English pseudo-locale, for the development view only. */
const markets: Record<MarketName, Market> = {
  'de-de': {
    country: 'Germany',
    locale: 'de-DE',
    currency: 'EUR',
    vatRate: 19,
    reducedVatRate: 7,
    filing: 'domestic',
    timeZone: 'Europe/Berlin',
    region: 'dach',
    carriers: ['dhl', 'hermes', 'dpd'],
    paymentMethods: ['card', 'paypal', 'sepa-direct-debit', 'apple-pay'],
    cutOffTime: '15:00',
    freeShippingFrom: 49,
    manager: 'lena.vogt@velvet-retail.example',
  },
  'at-de': {
    country: 'Austria',
    locale: 'de-AT',
    currency: 'EUR',
    vatRate: 20,
    reducedVatRate: 10,
    filing: 'oss',
    timeZone: 'Europe/Vienna',
    region: 'dach',
    carriers: ['post-at', 'dhl', 'gls'],
    paymentMethods: ['card', 'paypal', 'eps', 'apple-pay'],
    cutOffTime: '14:00',
    freeShippingFrom: 49,
    manager: 'florian.huber@velvet-retail.example',
  },
  'cs-cz': {
    country: 'Czechia',
    locale: 'cs-CZ',
    currency: 'CZK',
    vatRate: 21,
    reducedVatRate: 12,
    filing: 'oss',
    timeZone: 'Europe/Prague',
    region: 'cee',
    carriers: ['ppl', 'zasilkovna', 'ceska-posta'],
    paymentMethods: ['card', 'bank-transfer', 'apple-pay'],
    cashOnDelivery: true,
    cutOffTime: '13:00',
    freeShippingFrom: 1290,
    manager: 'petra.svobodova@velvet-retail.example',
  },
  'pl-pl': {
    country: 'Poland',
    locale: 'pl-PL',
    currency: 'PLN',
    vatRate: 23,
    reducedVatRate: 8,
    filing: 'oss',
    timeZone: 'Europe/Warsaw',
    region: 'cee',
    carriers: ['inpost', 'dpd', 'poczta-polska'],
    paymentMethods: ['blik', 'przelewy24', 'card', 'paypal'],
    cutOffTime: '14:00',
    freeShippingFrom: 199,
    manager: 'tomasz.nowak@velvet-retail.example',
  },
  'fr-fr': {
    country: 'France',
    locale: 'fr-FR',
    currency: 'EUR',
    vatRate: 20,
    reducedVatRate: 5.5,
    filing: 'oss',
    timeZone: 'Europe/Paris',
    region: 'western-europe',
    carriers: ['colissimo', 'chronopost', 'mondial-relay'],
    paymentMethods: ['carte-bancaire', 'card', 'paypal', 'apple-pay'],
    cutOffTime: '13:00',
    freeShippingFrom: 60,
    manager: 'camille.martin@velvet-retail.example',
  },
  'en-gb': {
    country: 'United Kingdom',
    inCountry: 'the United Kingdom',
    locale: 'en-GB',
    currency: 'GBP',
    vatRate: 20,
    reducedVatRate: 5,
    filing: 'mtd',
    timeZone: 'Europe/London',
    region: 'uk',
    carriers: ['royal-mail', 'evri', 'dpd-uk'],
    paymentMethods: ['card', 'paypal', 'apple-pay', 'clearpay'],
    cutOffTime: '16:00',
    freeShippingFrom: 45,
    manager: 'oliver.bennett@velvet-retail.example',
  },
  'it-it': {
    country: 'Italy',
    locale: 'it-IT',
    currency: 'EUR',
    vatRate: 22,
    reducedVatRate: 10,
    filing: 'oss',
    timeZone: 'Europe/Rome',
    region: 'southern-europe',
    carriers: ['brt', 'poste-italiane', 'gls'],
    paymentMethods: ['card', 'paypal', 'apple-pay', 'google-pay'],
    cutOffTime: '12:00',
    freeShippingFrom: 49,
    manager: 'giulia.romano@velvet-retail.example',
  },
  'en-xa': {
    country: 'en-XA pseudo-locale',
    inCountry: 'the en-XA pseudo-locale',
    locale: 'en-XA',
    currency: 'EUR',
    vatRate: 19,
    reducedVatRate: 7,
    filing: 'domestic',
    timeZone: 'Europe/Berlin',
    region: 'dach',
    carriers: ['dhl'],
    paymentMethods: ['card', 'paypal'],
    cutOffTime: '15:00',
    freeShippingFrom: 49,
    manager: 'localization@velvet-retail.example',
  },
}

// A brand settles through two merchant accounts, one per legal entity: the reports of the EU account are imported at
// the German market, those of the UK account at the British one.
const settlementHomes: MarketName[] = ['de-de', 'en-gb']

const giftCardValues: Record<Currency, number[]> = {
  EUR: [25, 50, 100],
  CZK: [500, 1000, 2500],
  PLN: [100, 200, 500],
  GBP: [20, 50, 100],
}

interface CapabilityInfo {
  name: Capability
  title: string
  labels: string[]
  owner: string
  description: string
  /** Days since a new capability came; the others came with the platform. */
  since?: number
}

const capabilities: CapabilityInfo[] = [
  {
    name: 'checkout',
    title: 'Checkout',
    labels: ['core'],
    owner: 'checkout-squad@velvet-retail.example',
    description: 'Cart, addresses and the order: the steps every storefront shares.',
  },
  {
    name: 'payment-provider',
    title: 'Payment provider',
    labels: ['core', 'pci'],
    owner: 'payments-squad@velvet-retail.example',
    description:
      'Cards, wallets and the local methods of each market through Adyen, one merchant account per brand and legal entity.',
  },
  {
    name: 'shipping',
    title: 'Shipping',
    labels: ['core', 'logistics'],
    owner: 'logistics@velvet-retail.example',
    description: 'Carriers, labels and the dispatch cut-off per market.',
  },
  {
    name: 'tax-engine',
    title: 'Tax engine',
    labels: ['core', 'compliance'],
    owner: 'finance-systems@velvet-retail.example',
    description: [
      'Calculates the VAT of every order line.',
      '',
      '- the standard and the reduced rate of the market',
      '- distance sales filed through the EU **One-Stop Shop**',
      '- the UK through HMRC’s Making Tax Digital',
    ].join('\n'),
  },
  {
    name: 'search',
    title: 'Search',
    labels: ['discovery'],
    owner: 'discovery-squad@velvet-retail.example',
    description: 'Product search and category pages, one index per storefront and market.',
  },
  {
    name: 'reviews',
    title: 'Reviews',
    labels: ['engagement'],
    owner: 'crm@velvet-retail.example',
    description: 'Verified buyer reviews, invited after delivery and moderated before they show.',
  },
  {
    name: 'gift-cards',
    title: 'Gift cards',
    labels: ['add-on'],
    owner: 'payments-squad@velvet-retail.example',
    description: 'Digital and printed gift cards in the market’s currency.',
  },
  {
    name: 'returns-portal',
    title: 'Returns portal',
    labels: ['logistics'],
    owner: 'logistics@velvet-retail.example',
    description: 'Self-service returns: the customer picks the items, prints the label and is refunded on the payment.',
    since: 24,
  },
  {
    name: 'buy-now-pay-later',
    title: 'Buy now, pay later',
    labels: ['feature', 'pci'],
    owner: 'payments-squad@velvet-retail.example',
    description: 'Klarna’s pay in 30 days and pay in 3 at the payment step, through a direct Klarna integration.',
    since: 18,
  },
]

const core: Capability[] = ['checkout', 'payment-provider', 'shipping', 'tax-engine']
const productionCapabilities: Capability[] = [...core, 'search', 'reviews', 'gift-cards']
const infoOf = (capability: Capability) => capabilities.find((entry) => entry.name === capability)!

type FeedName = 'product-feed' | 'settlement-import' | 'tracking-sync'

/** The feeds: a schedule into search, the settlement files of the payment provider, the carriers' tracking queue. */
const feeds: (Pick<UnitMeta, 'unitType' | 'definition'> & {
  capability: Capability
  name: FeedName
  title: string
  description: string
})[] = [
  {
    capability: 'search',
    name: 'product-feed',
    title: 'Product feed',
    unitType: 'time',
    definition: '15 */2 * * *',
    description: 'Pushes products, prices and stock into the search index of a market every two hours.',
  },
  {
    capability: 'payment-provider',
    name: 'settlement-import',
    title: 'Settlement import',
    unitType: 'file',
    definition: 'settlement_detail_report_batch_*.csv',
    description: 'Reads the settlement reports of a merchant account and books the payouts and fees in the ledger.',
  },
  {
    capability: 'shipping',
    name: 'tracking-sync',
    title: 'Tracking sync',
    unitType: 'message',
    definition: 'carrier-tracking-events',
    description: 'Updates orders from the tracking events that the carriers put on the queue.',
  },
]

interface StorefrontMarket {
  name: MarketName
  /** Days since the market opened for the storefront. */
  since: number
  /** Capabilities set up here besides the core ones. */
  extra: Capability[]
  /** The storefront ships with some of the market's carriers only. */
  carriers?: string[]
  /** Set up once and removed since: in Deleted in production, absent from the other views. */
  removed?: Capability[]
  /** Days to the launch: the market and its setups are scheduled. */
  startsIn?: number
  /** Closed since: the market and its setups are in Deleted in production. */
  closed?: boolean
  description?: string
}

interface Storefront {
  name: string
  title: string
  domain: string
  legalEntity: string
  theme: string
  returnWindowDays: number
  description: string
  labels: string[]
  values: { brandManager: string; supportEmail: string; costCenter: string }
  plan: 'enterprise' | 'growth'
  skus: number
  /** The contracted capabilities: the subscriptions. */
  capabilities: Capability[]
  /**
   * The tracking events of its parcels come through the carriers' queue. Velvet's warehouse is run by a fulfilment
   * partner whose WMS posts every tracking event to the order webhook instead.
   */
  trackingSync: boolean
  since: number
  markets: StorefrontMarket[]
}

const brands: Storefront[] = [
  {
    name: 'velvet',
    title: 'Velvet',
    domain: 'velvet-fashion.example',
    legalEntity: 'Velvet Retail GmbH',
    theme: 'noir',
    returnWindowDays: 30,
    description: 'Premium womenswear and accessories, the group’s first and largest brand.',
    labels: ['fashion', 'premium'],
    values: { brandManager: 'Sophie Wagner', supportEmail: 'service@velvet-fashion.example', costCenter: 'CC-2100' },
    plan: 'enterprise',
    skus: 184000,
    capabilities: productionCapabilities,
    trackingSync: false,
    since: 900,
    markets: [
      { name: 'de-de', since: 900, extra: ['search', 'reviews', 'gift-cards'] },
      { name: 'at-de', since: 860, extra: ['search'] },
      { name: 'fr-fr', since: 610, extra: ['search'] },
      { name: 'en-gb', since: 520, extra: ['search', 'reviews', 'gift-cards'] },
      { name: 'cs-cz', since: 300, extra: ['search'] },
      { name: 'pl-pl', since: 240, extra: ['search', 'gift-cards'], removed: ['reviews'] },
      {
        name: 'it-it',
        since: 12,
        extra: [],
        startsIn: 30,
        description: 'Velvet opens in **Italy** next month: prices, texts and carriers are ready, search follows.',
      },
    ],
  },
  {
    name: 'urban-step',
    title: 'Urban Step',
    domain: 'urbanstep.example',
    legalEntity: 'Urban Step GmbH',
    theme: 'street',
    returnWindowDays: 30,
    description: 'Sneakers and street footwear.',
    labels: ['footwear'],
    values: { brandManager: 'Jonas Becker', supportEmail: 'hello@urbanstep.example', costCenter: 'CC-2200' },
    plan: 'growth',
    skus: 42000,
    capabilities: [...core, 'search', 'reviews'],
    trackingSync: true,
    since: 430,
    markets: [
      { name: 'de-de', since: 430, extra: ['search', 'reviews'] },
      { name: 'pl-pl', since: 380, extra: ['search'] },
      { name: 'en-gb', since: 200, extra: ['search', 'reviews'] },
    ],
  },
  {
    name: 'kidzone',
    title: 'KidZone',
    domain: 'kidzone-shop.example',
    legalEntity: 'KidZone Handels GmbH',
    theme: 'playful',
    returnWindowDays: 100,
    description: 'Children’s clothing and toys; returns are accepted for 100 days, because children grow.',
    labels: ['kids'],
    values: { brandManager: 'Marta Kowalska', supportEmail: 'care@kidzone-shop.example', costCenter: 'CC-2300' },
    plan: 'growth',
    skus: 26000,
    capabilities: [...core, 'gift-cards'],
    trackingSync: true,
    since: 280,
    markets: [
      { name: 'de-de', since: 280, extra: ['gift-cards'] },
      // KidZone ships to Zásilkovna's pick-up points only in Czechia.
      { name: 'cs-cz', since: 150, extra: [], carriers: ['zasilkovna'] },
      { name: 'fr-fr', since: 120, extra: [], closed: true },
    ],
  },
]

const brand = (name: string) => brands.find((candidate) => candidate.name === name)!

/** The storefront in some of its markets only, with the capabilities they set up. */
function inMarkets(source: Storefront, names: MarketName[]): Storefront {
  return { ...source, markets: source.markets.filter((market) => names.includes(market.name)) }
}

/** The storefront with a new capability, contracted and set up in some of its markets. */
function withCapability(source: Storefront, capability: Capability, names: MarketName[]): Storefront {
  return {
    ...source,
    capabilities: [...source.capabilities, capability],
    markets: source.markets.map((market) =>
      names.includes(market.name) ? { ...market, extra: [...market.extra, capability] } : market,
    ),
  }
}

/** The lifecycle examples of production, by key: a subscription under notice, a paused setup, an ended promotion. */
const lifecycle: Record<string, Meta> = {
  'usg.urban-step.reviews': {
    expiresIn: 45,
    labels: ['notice-given'],
    description: 'Urban Step moves its reviews into its own community app; the subscription ends with the contract.',
  },
  'exe.urban-step.reviews.de-de': {
    disabled: true,
    description: 'Paused while the verification of German reviews is audited.',
  },
  'exe.velvet.gift-cards.pl-pl': {
    createdDaysAgo: 85,
    createdBy: 'ada',
    expiresIn: -25,
    title: 'Velvet gift-card launch promotion in Poland',
    description:
      'A 10 % bonus on gift cards for their launch in Poland. The two-month promotion has ended, and gift cards are not sold there for now.',
  },
}

/** What holds in every view where the key is: a feed run switched off for good, so staging mirrors production. */
const settled: Record<string, Meta> = {
  'uxe.kidzone.shipping.cs-cz.tracking-sync': {
    disabled: true,
    description: 'Off since Zásilkovna, KidZone’s only carrier in Czechia, calls the order webhook directly.',
  },
}

/** The feed runs that differ from their feed. */
const feedRuns: Record<string, Record<string, unknown>> = {
  // Germany has the most price changes, so Velvet's German index is fed every half hour.
  'uxe.velvet.search.de-de.product-feed': { cron: '*/30 * * * *' },
  // Evri sends an event for every delivery attempt, up to three per parcel, so the British queue has more consumers.
  'uxe.urban-step.shipping.en-gb.tracking-sync': { maxConcurrentCalls: 16 },
}

/** Created when it launched; what is older than the platform came with the migration 400 days ago, by the system. */
const created = (since: number, by: PersonaId): Meta =>
  since >= 400 ? { createdDaysAgo: 400 } : { createdDaysAgo: since, createdBy: by }

/** A new capability is younger than the storefront or the market it is set up in. */
const sinceOf = (capability: Capability, since: number) => Math.min(since, infoOf(capability).since ?? since)

const countryCode = (name: MarketName) => markets[name].locale.slice(3)
const language = (name: MarketName) => markets[name].locale.slice(0, 2)
const inCountry = (market: Market) => market.inCountry ?? market.country
/** The merchant account of a market: the UK legal entity takes the pounds, the EU one the rest. */
const accountOf = (market: Market) => (market.currency === 'GBP' ? 'UK' : 'EU')

interface Variant {
  stage: Stage
  /** The release candidate: the documents of the next release. */
  next?: boolean
  /** The feature of a feature delivery, deployed below the stage's host. */
  feature?: string
}

/** The host of the variant's storefronts and endpoints, as `staging` or `bnpl.preview`. */
const hostOf = (variant: Variant) => [variant.feature, stages[variant.stage].host].filter(Boolean).join('.')

/** The mailbox of a test variant: plus-addressed, so that test mails never reach the people of production. */
const testInbox = (box: string, variant: Variant, tag?: string) =>
  `${box}+${tag ?? variant.feature ?? variant.stage}@velvet-retail.example`

function addCapabilities(b: ScopeBuilder, names: readonly Capability[]) {
  for (const capability of capabilities.filter((entry) => names.includes(entry.name))) {
    b.responsibility(capability.name, {
      title: capability.title,
      description: capability.description,
      labels: capability.labels,
      link: `${docs}/capabilities/${capability.name}`,
      values: { owner: capability.owner },
      ...(capability.since ? created(capability.since, 'edsger') : {}),
    })
  }
  for (const feed of feeds.filter((entry) => b.has(`rst.${entry.capability}`))) {
    b.unit(feed.capability, feed.name, {
      unitType: feed.unitType,
      definition: feed.definition,
      title: feed.title,
      description: feed.description,
      link: `${docs}/runbooks/${feed.name}`,
    })
  }
}

/**
 * The product defaults of the capabilities and feeds in the view, and the templates of the setups and feed runs. Test
 * variants call the provider's test systems and mail the plus-addressed ops inbox.
 */
function capabilityDocuments(b: ScopeBuilder, variant: Variant) {
  const next = !!variant.next
  const { test } = stages[variant.stage]
  const configure = (
    key: string,
    versions: Record<string, unknown> | Record<string, unknown>[],
    options?: HistoryOptions,
  ) => {
    if (b.has(key)) {
      b.configure(key, versions, options)
    }
  }

  // The checkout of all brands, tuned every few weeks since the platform went live: the long history of the project.
  const checkout = evolve(
    {
      steps: ['cart', 'address', 'shipping', 'payment', 'review'],
      addressValidation: 'none',
      maxCartItems: 50,
      cartTtlHours: 72,
      abandonedCartReminderHours: 4,
    },
    17,
    (version) =>
      version === 4
        ? { addressValidation: 'loqate' }
        : version === 8
          ? { steps: ['cart', 'address', 'payment', 'review'] }
          : version === 11
            ? { expressCheckout: ['apple-pay'] }
            : version === 15
              ? { expressCheckout: ['apple-pay', 'paypal'] }
              : { abandonedCartReminderHours: 2 + (version % 3) },
  )
  if (next) {
    checkout.push({ ...checkout.at(-1)!, expressCheckout: ['apple-pay', 'paypal', 'google-pay'] })
  }
  configure('rst.checkout', checkout, { every: 23, lastDaysAgo: next ? 1 : 2 })

  // Payments are captured when the parcel ships since the second version; the next release adds network tokens.
  const payments = evolve(
    {
      provider: 'adyen',
      captureMode: 'immediate',
      threeDSecure: 'required',
      webhookUrl: `https://payments.${test ? `${hostOf(variant)}.` : ''}velvet-retail.example/webhooks/adyen`,
    },
    next ? 4 : 3,
    (version) =>
      version === 2
        ? { captureMode: 'on-shipment' }
        : version === 3
          ? { refundAutoApproveBelow: { amount: 100, currency: 'EUR' } }
          : { networkTokens: true },
  )
  configure('rst.payment-provider', payments, { every: 30, lastDaysAgo: next ? 1 : 6 })
  configure('rst.shipping', { labelFormat: 'pdf-a6', defaultParcelWeightKg: 0.8, returnLabel: 'in-box' })
  configure('rst.tax-engine', {
    provider: 'avalara',
    roundingMode: 'half-up',
    taxCodes: 'apparel-2026',
    pricesIncludeVat: true,
  })
  configure('rst.search', {
    engine: 'algolia',
    applicationId: test ? 'QX4T7VRA2N' : 'VR7Q2LX9KD',
    hitsPerPage: 48,
    typoTolerance: 'min',
    adminKey: `@(search.algolia-${test ? 'test-' : ''}admin-key, primaryVault)`,
  })
  configure('rst.reviews', {
    vendor: 'trustpilot',
    moderation: 'pre-publish',
    invitationDelayDays: 14,
    minStarsToFeature: 4,
  })
  configure('rst.gift-cards', { issuer: 'givex', validityMonths: 36, maxPerOrder: 5 })
  configure('rst.returns-portal', { refundTo: 'original-payment', labelProvider: 'dhl', inspectionDays: 3 })
  configure('rst.buy-now-pay-later', {
    provider: 'klarna',
    creditCheck: 'provider',
    showOnProductPage: true,
    products: ['pay-in-30-days', 'pay-in-3'],
  })

  configure('unt.search.product-feed', {
    format: 'jsonl',
    includeOutOfStock: false,
    fields: ['sku', 'title', 'brand', 'price', 'sizes', 'colors', 'stock', 'imageUrl'],
  })
  configure('unt.payment-provider.settlement-import', {
    source: `sftp://reports${test ? '-test' : ''}.adyen.example/settlements`,
    reportUser: `@(adyen.${test ? 'test-' : ''}report-user, primaryVault)`,
    matchBy: 'pspReference',
    postTo: 'sap-s4hana',
  })
  configure('unt.shipping.tracking-sync', {
    deadLetterQueue: 'carrier-tracking-events-dlq',
    maxDeliveryAttempts: 5,
    maxConcurrentCalls: 8,
    statusMap: 'carrier-status-v3',
  })

  // Every market setup starts from the checkout defaults: guest checkout, the order timeout, the statutory 14-day
  // withdrawal period of the EU and the UK, and the retries of the provider calls. The log level comes from the market.
  b.template(
    'exe',
    evolve(
      {
        guestCheckout: true,
        orderTimeoutMinutes: 30,
        withdrawalPeriodDays: 14,
        retry: { attempts: 3 },
      },
      3,
      (version) => (version === 2 ? { retry: { backoffMs: 500 } } : { orderTimeoutMinutes: 20 }),
    ),
  )
  b.template('uxe', {
    retries: 3,
    timeoutMinutes: 20,
    alertEmail: test ? testInbox('commerce-ops', variant) : 'commerce-ops@velvet-retail.example',
  })
}

function schemas(b: ScopeBuilder, next: boolean) {
  // A market cannot be saved without its currency and locale: prices, number formats and texts depend on them.
  const market = {
    type: 'object',
    properties: {
      currency: { enum: ['EUR', 'CZK', 'PLN', 'GBP'] },
      locale: { type: 'string', pattern: '^[a-z]{2}-[A-Z]{2}$' },
      freeShippingFrom: { type: 'number', minimum: 0 },
    },
  }
  b.typeSchema('cnt', [
    { ...market, required: ['currency'] },
    { ...market, required: ['currency', 'locale'] },
  ])

  // Every tax setup states the market's VAT rate (27 % in Hungary is the highest in the EU). Ahead of a B2B shop with
  // net prices, the next release makes every tax setup state whether shelf prices include VAT (gross for consumers).
  const rate = { type: 'number', minimum: 0, maximum: 27 }
  const tax = {
    type: 'object',
    properties: { vatRate: rate, reducedVatRate: rate, filing: { enum: ['domestic', 'oss', 'mtd'] } },
    required: ['vatRate'],
  }
  b.descendantSchema('rst.tax-engine', 'exe', [
    tax,
    ...(next
      ? [
          {
            ...tax,
            properties: { ...tax.properties, priceDisplay: { enum: ['gross', 'net'] } },
            required: ['vatRate', 'priceDisplay'],
          },
        ]
      : []),
  ])
}

function storefrontContent(source: Storefront): Record<string, unknown> {
  return {
    legalEntity: source.legalEntity,
    theme: source.theme,
    logoUrl: `https://cdn.velvet-retail.example/brands/${source.name}/logo.svg`,
    returnWindowDays: source.returnWindowDays,
  }
}

function subscriptionContent(source: Storefront, capability: Capability): Record<string, unknown> | undefined {
  const enterprise = source.plan === 'enterprise'
  switch (capability) {
    case 'checkout':
      // Velvet wraps gifts and answers order mails from the brand's service address, in every market.
      return source.name === 'velvet'
        ? { giftWrapping: true, replyTo: '@annotation("/supportEmail", "Subject")' }
        : undefined
    case 'payment-provider':
      return { pricing: 'interchange-plus', markupPercent: enterprise ? 0.45 : 0.6 }
    case 'search':
      return { plan: enterprise ? 'premium' : 'grow', records: source.skus }
    case 'reviews':
      return { plan: 'plus', invitationsPerMonth: enterprise ? 25000 : 8000 }
    case 'gift-cards':
      return { program: `${source.name}-gift-card`, physicalCards: enterprise }
    case 'buy-now-pay-later':
      return { pricing: 'per-transaction', feePercent: 2.49 }
    default:
      // Shipping, the tax engine and returns come with the platform fee.
      return undefined
  }
}

/** The market's document; its setups and feed runs inherit the log level of the stage from it. */
function marketContent(source: Storefront, name: MarketName, variant: Variant): Record<string, unknown> {
  const market = markets[name]
  return {
    currency: market.currency,
    locale: market.locale,
    baseUrl: `https://${hostOf(variant)}.${source.domain}/${name}`,
    freeShippingFrom: market.freeShippingFrom,
    logLevel: stages[variant.stage].logLevel,
  }
}

/** What a capability needs in one market. The currency and the base URL are read from the market (`@config`). */
function setupContent(
  source: Storefront,
  capability: Capability,
  entry: StorefrontMarket,
  variant: Variant,
): Record<string, unknown> | undefined {
  const name = entry.name
  const market = markets[name]
  const { test } = stages[variant.stage]
  switch (capability) {
    case 'checkout':
      // Czech shoppers still pay the courier in cash; the other markets keep the checkout of the storefront.
      return market.cashOnDelivery ? { cashOnDeliveryFee: 39 } : undefined
    case 'payment-provider':
      return {
        merchantAccount: `${source.title.replaceAll(' ', '')}${accountOf(market)}`,
        methods: market.paymentMethods,
        settlementCurrency: '@config("/currency")',
        endpoint: `https://checkout-${test ? 'test' : 'live'}.adyen.example/v71`,
        apiKey: `@(${source.name}.adyen-${test ? 'test' : 'live'}-api-key, primaryVault)`,
      }
    case 'shipping':
      return {
        carriers: entry.carriers ?? market.carriers,
        cutOffTime: market.cutOffTime,
        ...(market.cashOnDelivery ? { cashOnDelivery: true } : {}),
        exceptionAlertsTo: '@annotation("/marketManager", "Context")',
      }
    case 'tax-engine':
      return {
        vatRate: market.vatRate,
        reducedVatRate: market.reducedVatRate,
        filing: market.filing,
        ...(variant.next ? { priceDisplay: 'gross' } : {}),
      }
    case 'search':
      return {
        // The test stages share the test application of the search, so their indexes carry the host.
        indexName: `${test ? `${hostOf(variant)}_` : ''}${source.name}_${name}_products`.replace(/[-.]/g, '_'),
        queryLanguages: [language(name)],
        productUrlBase: '@config("/baseUrl")',
      }
    case 'reviews':
      return source.name === 'velvet' ? { language: language(name), moderation: 'pre-publish' } : undefined
    case 'gift-cards':
      return {
        denominations: giftCardValues[market.currency],
        // The launch promotion of gift cards in Poland (expired in production).
        ...(source.name === 'velvet' && name === 'pl-pl' ? { bonus: { percent: 10, minimumAmount: 200 } } : {}),
      }
    case 'returns-portal':
      return {
        dropOffPoints:
          name === 'at-de' ? ['post-at-filiale', 'post-at-abholstation'] : ['dhl-packstation', 'hermes-paketshop'],
        returnWindowDays: source.returnWindowDays,
      }
    case 'buy-now-pay-later':
      return {
        minOrderValue: 25,
        maxOrderValue: name === 'at-de' ? 1000 : 1500,
        apiKey: `@(${source.name}.klarna-${test ? 'test' : 'live'}-api-key, primaryVault)`,
      }
  }
}

/** The documents of production with a history, oldest first; the latest version is the catalog's content. */
function versionsOf(key: string, latest: Record<string, unknown>): [Record<string, unknown>[], HistoryOptions?] {
  switch (key) {
    // The free-shipping threshold of Germany rose twice with the carrier rates.
    case 'cnt.velvet.de-de':
      return [[{ ...latest, freeShippingFrom: 39 }, { ...latest, freeShippingFrom: 45 }, latest], { every: 60 }]
    // BLIK came after the launch and is now the first method of Poland.
    case 'exe.velvet.payment-provider.pl-pl':
      return [
        [
          { ...latest, methods: ['card', 'przelewy24'] },
          { ...latest, methods: ['card', 'przelewy24', 'blik'] },
          latest,
        ],
        { every: 100, lastDaysAgo: 5 },
      ]
    // DPD joined DHL and Hermes for bulky parcels.
    case 'exe.velvet.shipping.de-de':
      return [[{ ...latest, carriers: ['dhl', 'hermes'] }, latest], { every: 20 }]
    default:
      return [[latest]]
  }
}

/** The storefronts with their subscriptions, markets, market setups and feed runs. */
function catalog(b: ScopeBuilder, list: readonly Storefront[], variant: Variant) {
  const { test } = stages[variant.stage]
  // Production has the lifecycle examples and the histories; the default view also has what is in Deleted now.
  const live = variant.stage === 'production'
  const withDeleted = live && !variant.next
  const meta = (key: string, base: Meta): Meta => ({ ...base, ...settled[key], ...(live ? lifecycle[key] : {}) })
  const configure = (key: string, content: Record<string, unknown> | undefined) => {
    if (content) {
      const [versions, options] = live ? versionsOf(key, content) : [[content]]
      b.configure(key, versions, options)
    }
  }

  for (const source of list) {
    const s = source.name
    b.subject(s, {
      title: source.title,
      description: source.description,
      labels: source.labels,
      link: `${docs}/storefronts/${s}`,
      values: test ? { ...source.values, supportEmail: testInbox('test-inbox', variant, s) } : source.values,
      ...created(source.since, 'margaret'),
    })
    configure(`sbt.${s}`, storefrontContent(source))

    for (const capability of source.capabilities.filter((name) => b.has(`rst.${name}`))) {
      const key = `usg.${s}.${capability}`
      b.usage(
        s,
        capability,
        meta(key, {
          title: `${source.title} ${infoOf(capability).title.toLowerCase()}`,
          ...created(sinceOf(capability, source.since), 'margaret'),
        }),
      )
      configure(key, subscriptionContent(source, capability))
    }

    for (const entry of source.markets.filter((market) => withDeleted || !market.closed)) {
      const m = entry.name
      const market = markets[m]
      const timing: Meta = { timeZone: market.timeZone, ...(entry.startsIn ? { startsIn: entry.startsIn } : {}) }
      b.context(s, m, {
        title: `${source.title} ${market.country}`,
        description: entry.description,
        labels: [market.region, ...(market.currency === 'EUR' ? ['eurozone'] : []), ...(test ? ['test'] : [])],
        values: {
          marketManager: test ? testInbox('test-inbox', variant, `${s}-${m}`) : market.manager,
          countryCode: countryCode(m),
        },
        ...timing,
        ...created(entry.since, 'edsger'),
      })
      configure(`cnt.${s}.${m}`, marketContent(source, m, variant))

      const setUp = [...core, ...entry.extra, ...(withDeleted ? (entry.removed ?? []) : [])]
      for (const capability of setUp.filter((name) => b.has(`usg.${s}.${name}`))) {
        const key = `exe.${s}.${capability}.${m}`
        const since = sinceOf(capability, entry.since)
        b.execution(
          s,
          capability,
          m,
          meta(key, {
            title: `${source.title} ${infoOf(capability).title.toLowerCase()} in ${inCountry(market)}`,
            labels: capability === 'payment-provider' ? ['pci'] : capability === 'tax-engine' ? ['compliance'] : [],
            ...timing,
            ...created(since, 'ada'),
          }),
        )
        configure(key, setupContent(source, capability, entry, variant))

        for (const feed of feeds.filter((candidate) => candidate.capability === capability)) {
          const runs =
            feed.name === 'product-feed' ||
            (feed.name === 'settlement-import' && settlementHomes.includes(m)) ||
            (feed.name === 'tracking-sync' && source.trackingSync)
          if (!runs || !b.has(`unt.${capability}.${feed.name}`)) {
            continue
          }
          const run = `uxe.${s}.${capability}.${m}.${feed.name}`
          b.unitOfExecution(
            s,
            capability,
            m,
            feed.name,
            meta(run, {
              // A settlement import reads the reports of a whole merchant account, not of the market it runs at.
              title:
                feed.name === 'settlement-import'
                  ? `${source.title} ${accountOf(market)} settlement import`
                  : `${source.title} ${feed.title.toLowerCase()} in ${inCountry(market)}`,
              ...timing,
              ...created(since, 'ada'),
            }),
          )
          configure(run, feedRuns[run])
        }
      }
    }
  }
}

/** The capabilities and feeds of a view with their product defaults, templates and schemas. */
function platform(b: ScopeBuilder, names: readonly Capability[], variant: Variant) {
  addCapabilities(b, names)
  capabilityDocuments(b, variant)
  schemas(b, !!variant.next)
}

/** Production; the release candidate brings the returns portal to Velvet and the price display to tax setups. */
function production(b: ScopeBuilder, next: boolean) {
  const variant: Variant = { stage: 'production', next }
  platform(b, next ? [...productionCapabilities, 'returns-portal'] : productionCapabilities, variant)
  const list = next
    ? brands.map((source) =>
        source.name === 'velvet' ? withCapability(source, 'returns-portal', ['de-de', 'at-de']) : source,
      )
    : brands
  catalog(b, list, variant)

  if (!next) {
    // KidZone left France with its market setups and feed run; Velvet stopped the Polish reviews on their own.
    b.trash('cnt.kidzone.fr-fr', { daysAgo: 21, by: 'margaret' })
    b.trash('exe.velvet.reviews.pl-pl', { daysAgo: 9, by: 'edsger' })
  }
}

/** Velvet on the developers' sandbox: German, and the en-XA pseudo-locale that shows untranslated texts. */
function development(b: ScopeBuilder) {
  const variant: Variant = { stage: 'development' }
  platform(b, [...core, 'search'], variant)
  const velvet: Storefront = {
    ...brand('velvet'),
    markets: [
      { name: 'de-de', since: 60, extra: ['search'] },
      {
        name: 'en-xa',
        since: 30,
        extra: [],
        description: 'Accented pseudo-English: untranslated texts and truncated labels stand out.',
      },
    ],
  }
  catalog(b, [velvet], variant)
}

/** Staging: the three storefronts in Germany, Czechia and the UK, with test payments and test inboxes. */
function staging(b: ScopeBuilder) {
  const variant: Variant = { stage: 'staging' }
  platform(b, productionCapabilities, variant)
  const stagingMarkets: Record<string, MarketName[]> = {
    velvet: ['de-de', 'cs-cz', 'en-gb'],
    'urban-step': ['de-de'],
    kidzone: ['cs-cz'],
  }
  catalog(
    b,
    brands.map((source) => inMarkets(source, stagingMarkets[source.name] ?? [])),
    variant,
  )
}

/** Buy now, pay later for Velvet in Germany and Austria, on the feature's preview hosts. */
function featureDelivery(b: ScopeBuilder) {
  const variant: Variant = { stage: 'feature', feature: 'bnpl' }
  platform(b, ['checkout', 'payment-provider', 'tax-engine', 'buy-now-pay-later'], variant)
  const velvet = withCapability(inMarkets(brand('velvet'), ['de-de', 'at-de']), 'buy-now-pay-later', ['de-de', 'at-de'])
  catalog(b, [velvet], variant)
}

export const storefronts: ShowcaseProject = {
  org: 'velvet-retail',
  project: 'storefronts',
  summary: 'Multi-brand storefronts: brand storefronts, their markets and the commerce capabilities set up per market.',
  members: { margaret: 'Owner', edsger: 'Administrator', ada: 'Contributor', radia: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Capability', color: 'violet', icon: 'i-lucide-component' },
      unt: { label: 'Feed', color: 'fuchsia', icon: 'i-lucide-workflow' },
      sbt: { label: 'Storefront', color: 'rose', icon: 'i-lucide-store' },
      usg: { label: 'Subscription', color: 'amber', icon: 'i-lucide-tag' },
      cnt: { label: 'Market', color: 'sky', icon: 'i-lucide-globe' },
      exe: { label: 'Market setup', color: 'emerald', icon: 'i-lucide-cog' },
      uxe: { label: 'Feed run', color: 'orange', icon: 'i-lucide-play' },
    },
    defaultTimeZone: 'Europe/Berlin',
    dateFormat: 'dd.mm.yyyy',
  },
  views: environmentViews({
    development: 'Development: Velvet’s sandbox in German and the en-XA pseudo-locale',
    staging: 'Staging: the three storefronts with test payments in Germany, Czechia and the UK',
    'release-candidate': 'Release 2026.11: the returns portal and the price display of tax setups',
    'feature-delivery1': 'Buy now, pay later for Velvet in Germany and Austria',
  }),
  build: (b) => {
    switch (b.view) {
      case 'default':
        return production(b, false)
      case 'release-candidate':
        return production(b, true)
      case 'development':
        return development(b)
      case 'staging':
        return staging(b)
      case 'feature-delivery1':
        return featureDelivery(b)
    }
  },
}
