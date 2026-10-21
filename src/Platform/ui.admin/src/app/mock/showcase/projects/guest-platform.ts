import type { Meta, ScopeBuilder, UnitMeta } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// A new example (spec section 9.14): Seabreeze Hotels sells the rooms of its Portuguese hotels on its own website and
// app, through online travel agencies (OTAs) and through the GDS that corporate travel agents book in. The hotels are
// the subjects and their sales channels the contexts. A channel setup (execution) is one module on one channel of one
// hotel: the rate plans, the commission and the payment terms of that channel, with the channel's API key in the vault.
// Sync jobs keep the channels in step with the property management system (PMS).

const docs = 'https://docs.seabreeze-hotels.example/guest-platform'

type Module =
  'booking-engine' | 'rate-manager' | 'mobile-key' | 'housekeeping' | 'upsell' | 'loyalty' | 'digital-check-in'

type ChannelName =
  'direct-web' | 'mobile-app' | 'booking-com' | 'expedia' | 'corporate-gds' | 'flash-sale' | 'ota-simulator'

type Content = Record<string, unknown>

interface RatePlan {
  code: string
  name: string
  discountPercent?: number
  nonRefundable?: boolean
}

interface Channel {
  title: string
  /** How a title names the channel: "Lisbon Marina on Booking.com". */
  phrase: string
  /** The own channels sell direct; the others are external and get rates and availability through their API. */
  kind: 'direct' | 'ota' | 'gds' | 'promotion' | 'simulator'
  /** The channel's part of a test listing: TEST-SBLIS-BDC. */
  code: string
  ratePlans: RatePlan[]
  paymentTerms: 'pay-at-hotel' | 'prepaid' | 'virtual-card' | 'invoice'
  /** Percent of the room revenue the channel keeps. */
  commission?: number
  /** Where the channel differs from the group's cancellation policy (the exe template). */
  cancellation?: Content
}

const bar: RatePlan = { code: 'BAR', name: 'Best available rate' }
const nonRefundable = (discountPercent: number): RatePlan => ({
  code: 'NRF',
  name: 'Non-refundable',
  discountPercent,
  nonRefundable: true,
})

const channels: Record<ChannelName, Channel> = {
  'direct-web': {
    title: 'Website',
    phrase: 'on the website',
    kind: 'direct',
    code: 'WEB',
    ratePlans: [
      bar,
      { code: 'CIRCLE10', name: 'Seabreeze Circle member rate', discountPercent: 10 },
      { code: 'ADV21', name: 'Advance purchase, 21 days', discountPercent: 15, nonRefundable: true },
    ],
    paymentTerms: 'pay-at-hotel',
  },
  'mobile-app': {
    title: 'Seabreeze app',
    phrase: 'in the app',
    kind: 'direct',
    code: 'APP',
    ratePlans: [bar, { code: 'APP12', name: 'App-only rate', discountPercent: 12 }],
    paymentTerms: 'prepaid',
  },
  'booking-com': {
    title: 'Booking.com',
    phrase: 'on Booking.com',
    kind: 'ota',
    code: 'BDC',
    ratePlans: [bar, nonRefundable(10)],
    paymentTerms: 'pay-at-hotel',
    commission: 15,
  },
  expedia: {
    title: 'Expedia',
    phrase: 'on Expedia',
    kind: 'ota',
    code: 'EXP',
    ratePlans: [bar, nonRefundable(10), { code: 'PKG', name: 'Package rate, flight and hotel', discountPercent: 18 }],
    // Expedia collects from the guest and pays the hotel with a virtual card.
    paymentTerms: 'virtual-card',
    commission: 18,
  },
  'corporate-gds': {
    title: 'Corporate GDS',
    phrase: 'in the corporate GDS',
    kind: 'gds',
    code: 'GDS',
    ratePlans: [
      { code: 'CORP', name: 'Corporate negotiated rate', discountPercent: 12 },
      { code: 'LNR', name: 'Local negotiated rate', discountPercent: 15 },
    ],
    paymentTerms: 'invoice',
    commission: 10,
    // Corporate bookings cancel free until six hours before arrival.
    cancellation: { freeUntilHours: 6 },
  },
  'flash-sale': {
    title: 'Members’ flash sale',
    phrase: 'in the members’ flash sale',
    kind: 'promotion',
    code: 'FLS',
    ratePlans: [{ code: 'FLASH35', name: 'Flash sale, 35 % off', discountPercent: 35, nonRefundable: true }],
    paymentTerms: 'prepaid',
    commission: 20,
  },
  'ota-simulator': {
    title: 'OTA simulator',
    phrase: 'on the OTA simulator',
    kind: 'simulator',
    code: 'SIM',
    ratePlans: [bar, nonRefundable(10)],
    paymentTerms: 'virtual-card',
    commission: 15,
  },
}

const isExternal = (channel: ChannelName) => channels[channel].kind !== 'direct'

interface ModuleInfo {
  name: Module
  title: string
  /** The module's part of a license id: LIC-SBLIS-RM. */
  code: string
  labels: string[]
  description: string
  /** The channels the module is set up on, where a hotel has them. */
  runsOn: (channel: ChannelName) => boolean
  /** The monthly license fee per room in EUR; none for the modules that are bundled or share revenue. */
  feePerRoom?: number
}

const own = (channel: ChannelName) => !isExternal(channel)
const inApp = (channel: ChannelName) => channel === 'mobile-app'

// The booking flow and the guest services live on the own channels; the rate manager serves every channel.
const modules: ModuleInfo[] = [
  {
    name: 'booking-engine',
    title: 'Booking engine',
    code: 'BE',
    labels: ['core', 'direct'],
    description: 'The booking flow of the website and the app: room search, extras, payment and the confirmation mail.',
    runsOn: own,
    feePerRoom: 1.4,
  },
  {
    name: 'rate-manager',
    title: 'Rate manager',
    code: 'RM',
    labels: ['core', 'revenue'],
    description: [
      'Rate plans, restrictions and commissions per channel.',
      '',
      '- pushes rates and restrictions to the OTAs and the GDS',
      '- keeps the direct rate the lowest one (rate parity)',
      '- loads the seasonal rate plans',
    ].join('\n'),
    runsOn: () => true,
    feePerRoom: 1.9,
  },
  {
    name: 'mobile-key',
    title: 'Mobile key',
    code: 'MK',
    labels: ['guest-experience'],
    description: 'Room keys on the guest’s phone: issued at check-in, revoked at check-out.',
    runsOn: inApp,
    feePerRoom: 0.9,
  },
  {
    name: 'housekeeping',
    title: 'Housekeeping',
    code: 'HK',
    labels: ['guest-experience'],
    description:
      'Guest requests to housekeeping (towels, a make-up room, skipping a cleaning) and the green-stay credit.',
    runsOn: inApp,
  },
  {
    name: 'upsell',
    title: 'Upsell',
    code: 'UP',
    labels: ['revenue'],
    description: 'Paid extras offered while booking and before arrival: early check-in, late check-out, transfers.',
    runsOn: own,
  },
  {
    name: 'loyalty',
    title: 'Loyalty',
    code: 'LY',
    labels: ['guest-experience'],
    description:
      '**Seabreeze Circle**: points on direct stays, member rates and the points export to airline partners.',
    runsOn: own,
  },
]

/** The module of the next release (release-candidate). */
const digitalCheckIn: ModuleInfo = {
  name: 'digital-check-in',
  title: 'Digital check-in',
  code: 'CI',
  labels: ['guest-experience', 'new'],
  description: 'Online check-in before arrival: identity document scan, arrival time and the registration card.',
  runsOn: own,
  feePerRoom: 0.7,
}

const moduleOf = (name: Module) => [...modules, digitalCheckIn].find((module) => module.name === name)!

interface Hotel {
  name: string
  title: string
  /** The name in titles: "Lisbon Marina on Booking.com". */
  short: string
  description: string
  timeZone: string
  segment: 'city' | 'resort'
  region: 'mainland' | 'islands'
  propertyCode: string
  rooms: number
  stars: 4 | 5
  generalManager: string
  legalEntity: string
  /** The municipal tourist tax in EUR per night, charged for up to seven nights. */
  touristTax: number
  modules: Module[]
  channels: ChannelName[]
  /** The hotel's property id on each external channel. */
  listings: Partial<Record<ChannelName, string>>
  /** The OTAs whose preferred-partner programme the hotel is in: more visibility for three points more commission. */
  preferred?: ChannelName[]
  /** The PMS of a hotel that does not run on the group's. */
  pms?: string
  /** Days on the platform. */
  since: number
  /** When the hotel left the platform with its channels and setups (Deleted in production). */
  deleted?: { daysAgo: number; by: PersonaId }
}

const lisbonMarina: Hotel = {
  name: 'lisbon-marina',
  title: 'Seabreeze Lisbon Marina',
  short: 'Lisbon Marina',
  description: 'City hotel on the Tagus waterfront by the Alcântara marina; conferences and corporate travel.',
  timeZone: 'Europe/Lisbon',
  segment: 'city',
  region: 'mainland',
  propertyCode: 'SBLIS',
  rooms: 212,
  stars: 5,
  generalManager: 'Inês Carvalho',
  legalEntity: 'Seabreeze Hotéis Lisboa, S.A.',
  touristTax: 4,
  modules: ['booking-engine', 'rate-manager', 'mobile-key', 'housekeeping', 'upsell', 'loyalty'],
  channels: ['direct-web', 'mobile-app', 'booking-com', 'expedia', 'corporate-gds'],
  listings: { 'booking-com': '2873451', expedia: '61839204', 'corporate-gds': 'SZLIS212' },
  preferred: ['booking-com'],
  since: 900,
}

const portoRiverside: Hotel = {
  name: 'porto-riverside',
  title: 'Seabreeze Porto Riverside',
  short: 'Porto Riverside',
  description: 'Boutique hotel on the Ribeira quay, across the Douro from the port wine cellars.',
  timeZone: 'Europe/Lisbon',
  segment: 'city',
  region: 'mainland',
  propertyCode: 'SBOPO',
  rooms: 148,
  stars: 4,
  generalManager: 'Rui Monteiro',
  legalEntity: 'Seabreeze Hotéis Norte, Lda.',
  touristTax: 3,
  modules: ['booking-engine', 'rate-manager', 'housekeeping', 'upsell', 'loyalty'],
  channels: ['direct-web', 'mobile-app', 'booking-com', 'expedia'],
  listings: { 'booking-com': '1946027', expedia: '38220517' },
  since: 720,
}

const algarveCliffs: Hotel = {
  name: 'algarve-cliffs',
  title: 'Seabreeze Algarve Cliffs Resort',
  short: 'Algarve Cliffs',
  description: 'Beach resort on the cliffs above Praia da Marinha, with a spa and three pools; mostly week-long stays.',
  timeZone: 'Europe/Lisbon',
  segment: 'resort',
  region: 'mainland',
  propertyCode: 'SBFAO',
  rooms: 286,
  stars: 5,
  generalManager: 'Sofia Mendes',
  legalEntity: 'Seabreeze Resorts Algarve, S.A.',
  touristTax: 2,
  modules: ['booking-engine', 'rate-manager', 'mobile-key', 'housekeeping', 'upsell'],
  channels: ['direct-web', 'mobile-app', 'booking-com', 'expedia', 'flash-sale'],
  listings: { 'booking-com': '4410938', expedia: '72906133', 'flash-sale': 'FS-88214' },
  preferred: ['booking-com', 'expedia'],
  since: 610,
}

const madeiraGardens: Hotel = {
  name: 'madeira-gardens',
  title: 'Seabreeze Madeira Gardens',
  short: 'Madeira Gardens',
  description: 'Garden hotel above Funchal. It joined the group with its own PMS and is not in the app yet.',
  timeZone: 'Atlantic/Madeira',
  segment: 'resort',
  region: 'islands',
  propertyCode: 'SBFNC',
  rooms: 174,
  stars: 4,
  generalManager: 'João Freitas',
  legalEntity: 'Seabreeze Hotéis Madeira, Lda.',
  touristTax: 2,
  modules: ['booking-engine', 'rate-manager', 'loyalty'],
  channels: ['direct-web', 'booking-com'],
  listings: { 'booking-com': '5038712' },
  pms: 'https://pms-madeira.seabreeze-hotels.example/api',
  since: 380,
}

/** The villa wing of Algarve Cliffs, sold as a property of its own until its villas became room types of the resort. */
const algarveCliffsVillas: Hotel = {
  name: 'algarve-cliffs-villas',
  title: 'Seabreeze Algarve Cliffs Villas',
  short: 'Algarve Cliffs Villas',
  description: 'The villa wing of Algarve Cliffs: 24 villas with private pools.',
  timeZone: 'Europe/Lisbon',
  segment: 'resort',
  region: 'mainland',
  propertyCode: 'SBFAV',
  rooms: 24,
  stars: 5,
  generalManager: 'Sofia Mendes',
  legalEntity: 'Seabreeze Resorts Algarve, S.A.',
  touristTax: 2,
  modules: ['booking-engine', 'rate-manager'],
  channels: ['direct-web', 'booking-com'],
  listings: { 'booking-com': '4410951' },
  since: 500,
  deleted: { daysAgo: 20, by: 'grace' },
}

/** The developers' hotel: a dozen rooms on the PMS mock, sold on the OTA simulator. */
const testProperty: Hotel = {
  name: 'test-property',
  title: 'Seabreeze Test Property',
  short: 'Test property',
  description:
    'Twelve rooms on the PMS mock, for the developers of the guest platform. Nothing here is a real booking.',
  timeZone: 'Europe/Lisbon',
  segment: 'city',
  region: 'mainland',
  propertyCode: 'SBTST',
  rooms: 12,
  stars: 4,
  generalManager: 'Guest platform team',
  legalEntity: 'Seabreeze Hotels Digital, Lda.',
  touristTax: 4,
  modules: ['booking-engine', 'rate-manager', 'mobile-key'],
  channels: ['direct-web', 'mobile-app', 'ota-simulator'],
  listings: { 'ota-simulator': 'SIM-0001' },
  since: 200,
}

const hotels = [lisbonMarina, portoRiverside, algarveCliffs, madeiraGardens]

interface Environment {
  /** The host of the website. */
  web: string
  /** The host of the app's API. */
  api: string
  /** The API endpoint of each external channel. */
  endpoints: Partial<Record<ChannelName, string>>
  logLevel: 'debug' | 'info' | 'warn'
  /** The root of the vault secrets; staging and development have keys of their own. */
  secrets: string
  /** The domain of the reservation mailboxes. */
  mail: string
  /** The PMS every hotel reads, when it is not the production one. */
  pms?: string
  /** Test listings on the OTAs' test endpoints instead of the hotels' property ids. */
  testListings?: boolean
  /** Simulated door locks and sandbox payments. */
  developer?: boolean
  /** Real guests: the points go to the airline partners, not to their sandbox. */
  live?: boolean
  /** The lifecycle and notes of single annotations, by key. */
  states?: Record<string, Meta>
  /** The earlier versions of the documents with a history, oldest first, by key. */
  revisions?: Record<string, (latest: Content) => Content[]>
  /** The next release: newer module defaults and the tightened schema. */
  next?: boolean
}

/** The lifecycle of spec 9.14 besides the scheduled summer season (a sync job) and the deletions. */
const productionStates: Record<string, Meta> = {
  'cnt.algarve-cliffs.flash-sale': {
    startsIn: -24,
    expiresIn: -17,
    createdDaysAgo: 30,
    createdBy: 'hedy',
    description: 'A week-long flash sale for the members of a private travel club; the channel closed with the sale.',
  },
  'exe.algarve-cliffs.rate-manager.flash-sale': {
    startsIn: -24,
    expiresIn: -17,
    createdDaysAgo: 30,
    createdBy: 'hedy',
  },
  // Expedia is paused for Porto while the commission dispute is open, and so is its rate push.
  'cnt.porto-riverside.expedia': {
    disabled: true,
    labels: ['ota', 'dispute'],
    description: 'Paused during the commission dispute with Expedia (case **EXP-PT-2291**): no rates, no availability.',
    modifiedDaysAgo: 12,
    modifiedBy: 'frances',
  },
  'uxe.porto-riverside.rate-manager.expedia.rate-push': { disabled: true, modifiedDaysAgo: 12, modifiedBy: 'frances' },
  // Planned ends: the pilot license of the mobile keys and the negotiated rates of the contract year; the GDS
  // connection itself stays open.
  'usg.algarve-cliffs.mobile-key': {
    expiresIn: 70,
    labels: ['enterprise', 'pilot'],
    description: 'Pilot license; it ends unless the resort renews it after the pilot review.',
  },
  'exe.lisbon-marina.rate-manager.corporate-gds': {
    expiresIn: 71,
    description: 'Corporate rates run by contract year; next year’s negotiated rates are still in the RFP round.',
  },
}

const productionRevisions: Record<string, (latest: Content) => Content[]> = {
  // Lisbon doubled its tourist tax to 4 euros a night; later the hotel moved its check-out to 11:00.
  'sbt.lisbon-marina': (latest) => [
    { ...latest, touristTax: { perNightEur: 2, maxNights: 7 }, checkOut: '12:00' },
    { ...latest, checkOut: '12:00' },
    latest,
  ],
  // Porto raised its tourist tax from 2 to 3 euros a night.
  'sbt.porto-riverside': (latest) => [{ ...latest, touristTax: { perNightEur: 2, maxNights: 7 } }, latest],
  // The website added Spanish, then French and German.
  'cnt.lisbon-marina.direct-web': (latest) => [
    { ...latest, languages: ['pt', 'en'] },
    { ...latest, languages: ['pt', 'en', 'es'] },
    latest,
  ],
  // Lisbon joined Booking.com's preferred-partner programme and tried a deeper non-refundable discount for a month.
  'exe.lisbon-marina.rate-manager.booking-com': (latest) => [
    { ...latest, commission: 15, ratePlans: [bar, nonRefundable(8)] },
    { ...latest, commission: 15 },
    { ...latest, ratePlans: [bar, nonRefundable(12)] },
    latest,
  ],
}

const otaTestEndpoints: Partial<Record<ChannelName, string>> = {
  'booking-com': 'https://supply-xml-test.booking-com.example/hotels/ota',
  expedia: 'https://test.expediapartnercentral.example/eqc/ar',
}

const staging: Environment = {
  web: 'staging.seabreeze-hotels.example',
  api: 'api.staging.seabreeze-hotels.example',
  endpoints: otaTestEndpoints,
  logLevel: 'info',
  secrets: 'seabreeze.staging',
  mail: 'staging.seabreeze-hotels.example',
  pms: 'https://pms-staging.seabreeze-hotels.example/api',
  testListings: true,
}

const production: Environment = {
  web: 'www.seabreeze-hotels.example',
  api: 'api.seabreeze-hotels.example',
  endpoints: {
    'booking-com': 'https://supply-xml.booking-com.example/hotels/ota',
    expedia: 'https://services.expediapartnercentral.example/eqc/ar',
    'corporate-gds': 'https://gds.crs.seabreeze-hotels.example/ota/2015b',
    'flash-sale': 'https://partners.flash-escapes.example/api/v2/sales',
  },
  logLevel: 'warn',
  secrets: 'seabreeze',
  mail: 'seabreeze-hotels.example',
  live: true,
  states: productionStates,
  revisions: productionRevisions,
}

const environments = {
  production,
  'release-candidate': { ...production, next: true },
  staging,
  development: {
    web: 'dev.seabreeze-hotels.example',
    api: 'api.dev.seabreeze-hotels.example',
    endpoints: { 'ota-simulator': 'http://ota-simulator.dev.seabreeze-hotels.example:8080/ota' },
    logLevel: 'debug',
    secrets: 'seabreeze.dev',
    mail: 'dev.seabreeze-hotels.example',
    pms: 'http://pms-mock.dev.seabreeze-hotels.example:8081/api',
    developer: true,
  },
  'feature-delivery1': {
    ...staging,
    web: 'fd1.seabreeze-hotels.example',
    api: 'api.fd1.seabreeze-hotels.example',
    mail: 'fd1.seabreeze-hotels.example',
  },
} satisfies Record<string, Environment>

interface SyncJob {
  module: Module
  name: string
  meta: UnitMeta
  /** The job's document in the environment: its hosts and partners are those of the environment. */
  document: (env: Environment) => Content
  /** The channels of a setup of the module the job runs on. */
  runsOn: (hotel: Hotel, channel: ChannelName) => boolean
  /** The own configuration of one run, where it differs from the job's. */
  run?: (hotel: Hotel, channel: ChannelName, env: Environment) => Content | undefined
}

const summerPlans = (channel: ChannelName): RatePlan[] => [
  { code: 'SUMMER', name: 'Summer best available rate' },
  channel === 'direct-web'
    ? { code: 'SUMMER7', name: 'Summer, seven nights or more', discountPercent: 10 }
    : nonRefundable(8),
]

const syncJobs: SyncJob[] = [
  {
    module: 'rate-manager',
    name: 'rate-push',
    meta: {
      unitType: 'time',
      definition: '*/15 * * * *',
      title: 'Rate push',
      description: 'Pushes the rates and restrictions that changed to an external channel.',
    },
    document: () => ({ horizonDays: 400, deltaOnly: true, batchSize: 200 }),
    // The flash sale got its one rate plan once; it is not kept in step.
    runsOn: (_hotel, channel) => isExternal(channel) && channels[channel].kind !== 'promotion',
    // Booking.com sells a third of Lisbon's rooms, so its rates go out every five minutes.
    run: (hotel, channel) =>
      hotel.name === 'lisbon-marina' && channel === 'booking-com' ? { cron: '*/5 * * * *' } : undefined,
  },
  {
    module: 'booking-engine',
    name: 'availability-sync',
    meta: {
      unitType: 'event',
      definition: 'pms.inventory.changed',
      title: 'Availability sync',
      description: 'Reads the room inventory of the PMS into the booking engine whenever it changes.',
    },
    document: (env) => ({ pms: env.pms ?? 'https://pms.seabreeze-hotels.example/api', debounceSeconds: 20 }),
    runsOn: () => true,
    // A hotel with a PMS of its own reads it in production; the test environments have one PMS for every hotel.
    run: (hotel, _channel, env) => (hotel.pms && !env.pms ? { pms: hotel.pms } : undefined),
  },
  {
    module: 'loyalty',
    name: 'points-export',
    meta: {
      unitType: 'file',
      definition: 'night-audit/*.csv',
      title: 'Points export',
      description: [
        'Runs when the night audit closes a hotel’s day: posts the members’ points and exports them to the partners.',
        '',
        'One run per hotel, on its website: the night audit covers every direct stay, booked on the website or in',
        'the app.',
      ].join('\n'),
    },
    document: (env) => ({
      format: 'csv',
      target: `sftp://loyalty.${env.mail}/outbound`,
      encryption: 'pgp',
      partners: env.live ? ['atlantic-air-miles'] : ['partner-sandbox'],
    }),
    runsOn: (_hotel, channel) => channel === 'direct-web',
  },
  {
    module: 'rate-manager',
    name: 'summer-season',
    meta: {
      unitType: 'time',
      definition: '0 5 * * *',
      title: 'Summer season rate plan',
      description:
        'Loads the summer rate plans of the mainland resorts into the channels every morning, from the opening of ' +
        'summer sales in January to the end of the season.',
      // Summer stays sell months ahead, so the plan is valid from the opening of the sales.
      startsIn: 75,
      expiresIn: 345,
      createdDaysAgo: 40,
      createdBy: 'frances',
    },
    // The stay window, as month and day.
    document: () => ({ season: 'summer', stayFrom: '06-01', stayTo: '09-30', minStay: 3 }),
    runsOn: (hotel, channel) =>
      hotel.segment === 'resort' &&
      hotel.region === 'mainland' &&
      ['direct-web', 'booking-com', 'expedia'].includes(channel),
    run: (_hotel, channel) => ({ ratePlans: summerPlans(channel) }),
  },
  {
    module: 'rate-manager',
    name: 'parity-check',
    meta: {
      unitType: 'time',
      definition: '0 */2 * * *',
      title: 'Rate parity check',
      description: 'Compares the rates the OTAs show with the direct rate and alerts the revenue team.',
      createdDaysAgo: 12,
      createdBy: 'ada',
    },
    document: (env) => ({ tolerancePercent: 1, alertTo: `revenue@${env.mail}`, shopFrom: ['pt', 'gb', 'de'] }),
    runsOn: (_hotel, channel) => channels[channel].kind === 'ota',
    // Expedia's package rates hide the room rate, so its comparison is looser.
    run: (_hotel, channel) => (channel === 'expedia' ? { tolerancePercent: 2 } : undefined),
  },
]

/** The product defaults of the modules, oldest version first; the rate manager's long history is rateDefaults. */
const moduleDefaults: Record<Exclude<Module, 'rate-manager' | 'digital-check-in'>, Content[]> = {
  // A non-refundable rate is charged in full at booking, whatever the payment terms of its channel.
  'booking-engine': [
    { currency: 'EUR', bookingWindowDays: 450, holdMinutes: 15, languages: ['pt', 'en'] },
    {
      currency: 'EUR',
      bookingWindowDays: 500,
      holdMinutes: 15,
      languages: ['pt', 'en', 'es', 'fr', 'de'],
      chargeNonRefundableAtBooking: true,
    },
  ],
  'mobile-key': [{ lockPlatform: 'ble-v3', keysPerRoom: 4, validFrom: 'check-in', revokeAtCheckOut: true }],
  housekeeping: [
    {
      requestTypes: ['towels', 'pillows', 'make-up-room', 'skip-cleaning'],
      quietHours: { from: '22:00', to: '08:00' },
      greenStay: { creditEur: 8 },
      slaMinutes: 30,
    },
  ],
  upsell: [
    { offers: ['early-check-in', 'late-check-out'], preArrivalDays: 3 },
    { offers: ['early-check-in', 'late-check-out', 'airport-transfer'], preArrivalDays: 3 },
    {
      offers: ['early-check-in', 'late-check-out', 'airport-transfer', 'breakfast'],
      preArrivalDays: 5,
      placement: 'booking-flow',
    },
  ],
  loyalty: [
    {
      program: 'Seabreeze Circle',
      pointsPerEuro: 10,
      tierNights: { silver: 5, gold: 15, platinum: 40 },
      enrolOnBooking: true,
    },
  ],
}

/**
 * Three months of revenue management on the rate manager's defaults, one change about every six days: the August
 * peak (more overbooking, a two-night minimum, no Saturday arrivals), the September shoulder season (group blocks
 * and last-minute deals) and the quieter October. The long history of the project.
 */
const rateChanges: Content[] = [
  { overbookingPercent: 4 },
  { restrictions: { minStay: 2 } },
  { overbookingPercent: 5 },
  { restrictions: { closedToArrival: ['sat'] } },
  { parity: { tolerancePercent: 2 } },
  { groups: { releaseDays: 30, maxRooms: 40 } },
  { parity: { channels: ['booking-com', 'expedia'] } },
  { overbookingPercent: 4 },
  { restrictions: { minStay: 1, closedToArrival: [] } },
  { lastMinute: { withinDays: 2, discountPercent: 10 } },
  { groups: { releaseDays: 21 } },
  { overbookingPercent: 3 },
  { lastMinute: { withinDays: 3 } },
  { restrictions: { maxStay: 21 } },
  { lastMinute: { discountPercent: 15 } },
  { overbookingPercent: 2 },
]

function rateDefaults(next: boolean): Content[] {
  const versions = evolve(
    {
      baseCurrency: 'EUR',
      parity: { check: true, tolerancePercent: 3 },
      restrictions: { minStay: 1, maxStay: 28, closedToArrival: [] },
      overbookingPercent: 3,
    },
    rateChanges.length + 1,
    (version) => rateChanges[version - 2]!,
  )
  if (next) {
    versions.push({ ...versions.at(-1)!, dynamicPricing: { enabled: true, floorPercent: 75, ceilingPercent: 160 } })
  }
  return versions
}

function moduleDocuments(b: ScopeBuilder, env: Environment) {
  for (const [name, versions] of Object.entries(moduleDefaults)) {
    if (b.has(`rst.${name}`)) {
      // The next release shortens the hold of a room during the payment.
      const next = env.next && name === 'booking-engine' ? [{ ...versions.at(-1)!, holdMinutes: 10 }] : []
      b.configure(`rst.${name}`, [...versions, ...next], { every: 40 })
    }
  }
  if (b.has('rst.rate-manager')) {
    b.configure('rst.rate-manager', rateDefaults(!!env.next), { every: 6, lastDaysAgo: env.next ? 1 : 3 })
  }
}

/** Every channel setup inherits the group's cancellation policy; a channel may override it. */
function cancellationPolicy(b: ScopeBuilder) {
  b.template(
    'exe',
    [
      { cancellation: { freeUntilHours: 24, penalty: 'first-night' }, noShow: { penalty: 'first-night' } },
      { cancellation: { freeUntilHours: 48, penalty: 'first-night' }, noShow: { penalty: 'first-night' } },
      {
        cancellation: { freeUntilHours: 48, penalty: 'first-night', groupsFreeUntilDays: 21 },
        noShow: { penalty: 'full-stay' },
      },
    ],
    { every: 60 },
  )
}

const channelSetupSchema = (required: string[]) => ({
  type: 'object',
  properties: {
    ratePlans: {
      type: 'array',
      minItems: 1,
      items: {
        type: 'object',
        properties: {
          code: { type: 'string', pattern: '^[A-Z0-9]{2,12}$' },
          name: { type: 'string', minLength: 1 },
          discountPercent: { type: 'number', minimum: 0, maximum: 50 },
          nonRefundable: { type: 'boolean' },
        },
        required: ['code', 'name'],
      },
    },
    commission: { type: 'number', minimum: 0, maximum: 30 },
    paymentTerms: { enum: ['pay-at-hotel', 'prepaid', 'virtual-card', 'invoice'] },
  },
  required,
})

/** A channel setup of the rate manager cannot be saved without its rate plans; the next release adds the terms. */
function rateManagerSchema(b: ScopeBuilder, env: Environment) {
  b.descendantSchema(
    'rst.rate-manager',
    'exe',
    [
      { type: 'object', properties: { ratePlans: { type: 'array', minItems: 1 } }, required: ['ratePlans'] },
      channelSetupSchema(['ratePlans']),
      ...(env.next ? [channelSetupSchema(['ratePlans', 'paymentTerms'])] : []),
    ],
    { every: 45 },
  )
}

const edition = (hotel: Hotel) => (hotel.stars === 5 ? 'enterprise' : 'professional')

function licenseOf(hotel: Hotel, module: ModuleInfo): Content | undefined {
  if (module.feePerRoom) {
    return {
      edition: edition(hotel),
      roomsLicensed: hotel.rooms,
      feePerRoomEur: module.feePerRoom,
      monthlyFeeEur: Math.round(hotel.rooms * module.feePerRoom * 100) / 100,
    }
  }
  // Upsell is paid by a share of the extras it sells; housekeeping and loyalty come with the booking engine.
  return module.name === 'upsell' ? { revenueSharePercent: 8 } : undefined
}

function hotelDocument(hotel: Hotel): Content {
  return {
    legalEntity: hotel.legalEntity,
    currency: 'EUR',
    checkIn: hotel.segment === 'resort' ? '16:00' : '15:00',
    checkOut: '11:00',
    touristTax: { perNightEur: hotel.touristTax, maxNights: 7 },
  }
}

function channelDocument(hotel: Hotel, channel: ChannelName, env: Environment): Content {
  if (channel === 'direct-web') {
    return {
      url: `https://${env.web}/hotels/${hotel.name}`,
      languages: ['pt', 'en', 'es', 'fr', 'de'],
      logLevel: env.logLevel,
    }
  }
  if (channel === 'mobile-app') {
    return {
      deepLink: `seabreeze://hotels/${hotel.name}`,
      api: `https://${env.api}/v3/hotels/${hotel.name}`,
      minAppVersion: '5.12.0',
      logLevel: env.logLevel,
    }
  }
  const endpoint = env.endpoints[channel]
  if (!endpoint) {
    throw new Error(`guest-platform: no endpoint of ${channel}.`)
  }
  // The channel's id of the hotel is a fact of the listing (Values); the connection reads it from there. The binding
  // names its type, so the setups and the sync runs below the channel, which inherit it, read the same listing.
  return { endpoint, hotelId: '@annotation("/listingId", "Context")', logLevel: env.logLevel }
}

const setups: Record<Module, (hotel: Hotel, channel: ChannelName, env: Environment) => Content | undefined> = {
  // A prepaid channel takes the whole stay at booking; otherwise the resorts take a deposit and the city hotels none.
  'booking-engine': (hotel, channel, env) => ({
    deposit: { percent: channels[channel].paymentTerms === 'prepaid' ? 100 : hotel.segment === 'resort' ? 30 : 0 },
    paymentMethods:
      channel === 'mobile-app' ? ['card', 'apple-pay', 'google-pay', 'mb-way'] : ['card', 'mb-way', 'paypal'],
    merchantAccount: `@(${env.secrets}.adyen.${hotel.name}, primaryVault)`,
    // The payment page returns to the hotel's page on the channel; the confirmation comes from the hotel's mailbox.
    returnUrl: channel === 'mobile-app' ? '@config("/deepLink")' : '@config("/url")',
    confirmationSender: '@annotation("/reservationsEmail", "Subject")',
    ...(env.developer ? { sandboxPayments: true } : {}),
  }),
  'rate-manager': (hotel, channel, env) => {
    const { ratePlans, paymentTerms, commission, cancellation } = channels[channel]
    return {
      ratePlans,
      paymentTerms,
      ...(commission === undefined ? {} : { commission: commission + (hotel.preferred?.includes(channel) ? 3 : 0) }),
      ...(cancellation ? { cancellation } : {}),
      ...(isExternal(channel)
        ? {
            hotelCode: '@annotation("/propertyCode", "Subject")',
            apiKey: `@(${env.secrets}.${channel}.${hotel.name}, primaryVault)`,
          }
        : {}),
    }
  },
  'mobile-key': (hotel, _channel, env) => ({
    siteId: '@annotation("/propertyCode", "Subject")',
    vendorKey: `@(${env.secrets}.mobile-key.${hotel.name}, primaryVault)`,
    ...(env.developer ? { lockPlatform: 'ble-simulator' } : {}),
  }),
  // City hotels keep the module's defaults; the resorts add beach towels and pay a higher green-stay credit.
  housekeeping: (hotel) =>
    hotel.segment === 'resort'
      ? {
          requestTypes: ['towels', 'beach-towels', 'pillows', 'make-up-room', 'skip-cleaning'],
          greenStay: { creditEur: 10 },
        }
      : undefined,
  // The resorts sell upgrades and the spa rather than breakfast; the app offers the extras before arrival.
  upsell: (hotel, channel) => {
    const setup = {
      ...(hotel.segment === 'resort'
        ? { offers: ['late-check-out', 'airport-transfer', 'room-upgrade', 'spa-package'] }
        : {}),
      ...(channel === 'mobile-app' ? { placement: 'pre-arrival' } : {}),
    }
    return Object.keys(setup).length ? setup : undefined
  },
  // Members earn double points on bookings in the app.
  loyalty: (_hotel, channel) => (channel === 'mobile-app' ? { pointsPerEuro: 20 } : undefined),
  // The app reads the passport's chip; the website keeps the module's camera scan.
  'digital-check-in': (_hotel, channel) => (channel === 'mobile-app' ? { idScan: 'nfc-passport' } : undefined),
}

/**
 * The dates of the documents of an annotation that ended: a deleted hotel's documents last changed two days before it
 * left the platform, a closed sale's the day before it opened.
 */
function endedDates(hotel: Hotel, meta: Meta): HistoryOptions | undefined {
  if (hotel.deleted) {
    return { lastDaysAgo: hotel.deleted.daysAgo + 2 }
  }
  const { startsIn, expiresIn } = meta
  return expiresIn !== undefined && expiresIn < 0 ? { lastDaysAgo: 1 - (startsIn ?? expiresIn) } : undefined
}

/**
 * The own document of an annotation of a hotel, if it has one. Production documents with a history get their earlier
 * versions, twelve days apart; the documents of an annotation that ended are dated before its end.
 */
function configure(b: ScopeBuilder, env: Environment, hotel: Hotel, key: string, meta: Meta, content?: Content) {
  if (!content) {
    return
  }
  const revise = env.revisions?.[key]
  const dates = endedDates(hotel, meta)
  b.configure(key, revise ? revise(content) : content, revise ? { every: 12, ...dates } : dates)
}

/** The meta of an annotation of a hotel: the hotel's time zone and age, its own meta, then its state in the view. */
const metaOf = (hotel: Hotel, env: Environment, key: string, meta: Meta): Meta => ({
  timeZone: hotel.timeZone,
  createdDaysAgo: hotel.since,
  ...meta,
  ...env.states?.[key],
})

function addResponsibility(b: ScopeBuilder, module: ModuleInfo, meta: Meta = {}) {
  b.responsibility(module.name, {
    title: module.title,
    description: module.description,
    labels: module.labels,
    link: `${docs}/modules/${module.name}`,
    createdDaysAgo: 1000,
    ...meta,
  })
}

/** The license of one module for one hotel, its setups on the hotel's channels and the sync runs of those setups. */
function addSetups(b: ScopeBuilder, hotel: Hotel, module: ModuleInfo, env: Environment, meta: Meta = {}) {
  const usage = `usg.${hotel.name}.${module.name}`
  const usageMeta = metaOf(hotel, env, usage, {
    title: `${hotel.short}: ${module.title} license`,
    labels: [module.feePerRoom ? edition(hotel) : module.name === 'upsell' ? 'revenue-share' : 'bundled'],
    values: { licenseId: `LIC-${hotel.propertyCode}-${module.code}` },
    ...meta,
  })
  b.usage(hotel.name, module.name, usageMeta)
  configure(b, env, hotel, usage, usageMeta, licenseOf(hotel, module))

  for (const channel of hotel.channels.filter((candidate) => module.runsOn(candidate))) {
    const { phrase } = channels[channel]
    const execution = `exe.${hotel.name}.${module.name}.${channel}`
    const executionMeta = metaOf(hotel, env, execution, { title: `${hotel.short}: ${module.title} ${phrase}`, ...meta })
    b.execution(hotel.name, module.name, channel, executionMeta)
    configure(b, env, hotel, execution, executionMeta, setups[module.name](hotel, channel, env))

    for (const job of syncJobs) {
      if (job.module !== module.name || !b.has(`unt.${module.name}.${job.name}`) || !job.runsOn(hotel, channel)) {
        continue
      }
      const run = `uxe.${hotel.name}.${module.name}.${channel}.${job.name}`
      const { title, startsIn, expiresIn, createdDaysAgo, createdBy } = job.meta
      const runMeta = metaOf(hotel, env, run, {
        title: `${hotel.short}: ${title} ${phrase}`,
        // A run lives in the window of its job, and is not older than the job or the hotel.
        startsIn,
        expiresIn,
        createdDaysAgo: Math.min(hotel.since, createdDaysAgo ?? hotel.since),
        createdBy,
      })
      b.unitOfExecution(hotel.name, module.name, channel, job.name, runMeta)
      configure(b, env, hotel, run, runMeta, job.run?.(hotel, channel, env))
    }
  }
}

function addHotel(b: ScopeBuilder, hotel: Hotel, env: Environment) {
  const subject = `sbt.${hotel.name}`
  const subjectMeta = metaOf(hotel, env, subject, {
    title: hotel.title,
    description: hotel.description,
    labels: [hotel.segment, hotel.region],
    values: {
      propertyCode: hotel.propertyCode,
      rooms: hotel.rooms,
      stars: hotel.stars,
      generalManager: hotel.generalManager,
      reservationsEmail: `reservations.${hotel.name}@${env.mail}`,
    },
    createdBy: 'grace',
  })
  b.subject(hotel.name, subjectMeta)
  configure(b, env, hotel, subject, subjectMeta, hotelDocument(hotel))

  for (const channel of hotel.channels) {
    const info = channels[channel]
    const context = `cnt.${hotel.name}.${channel}`
    const listingId = env.testListings ? `TEST-${hotel.propertyCode}-${info.code}` : hotel.listings[channel]
    const contextMeta = metaOf(hotel, env, context, {
      title: `${hotel.short} ${info.phrase}`,
      labels: [info.kind],
      values: isExternal(channel) ? { listingId } : undefined,
      link: isExternal(channel) ? `${docs}/channels/${channel}` : undefined,
      createdBy: 'frances',
    })
    b.context(hotel.name, channel, contextMeta)
    configure(b, env, hotel, context, contextMeta, channelDocument(hotel, channel, env))
  }

  for (const module of hotel.modules) {
    addSetups(b, hotel, moduleOf(module), env)
  }
}

/** The modules and sync jobs the hotels use, the shared documents, then the hotels with their channels and setups. */
function addCatalog(b: ScopeBuilder, hotelsOfView: Hotel[], env: Environment, jobs: string[]) {
  const used = new Set(hotelsOfView.flatMap((hotel) => hotel.modules))
  for (const module of modules.filter((candidate) => used.has(candidate.name))) {
    addResponsibility(b, module)
  }
  for (const job of syncJobs.filter((candidate) => jobs.includes(candidate.name) && used.has(candidate.module))) {
    b.unit(job.module, job.name, { createdDaysAgo: 1000, link: `${docs}/sync-jobs/${job.name}`, ...job.meta })
    b.configure(`unt.${job.module}.${job.name}`, job.document(env))
  }
  moduleDocuments(b, env)
  cancellationPolicy(b)
  if (used.has('rate-manager')) {
    rateManagerSchema(b, env)
  }
  hotelsOfView.forEach((hotel) => addHotel(b, hotel, env))

  // The states and histories of the view are keyed by annotation key; a key outside the catalog is a typo.
  for (const key of [...Object.keys(env.states ?? {}), ...Object.keys(env.revisions ?? {})]) {
    if (!b.has(key)) {
      throw new Error(`guest-platform: ${key} is not in the catalog of ${b.view}.`)
    }
  }
}

/** Production: the four hotels and the lifecycle of the model; the release candidate is its copy with the next release. */
function productionView(b: ScopeBuilder, next: boolean) {
  const env = next ? environments['release-candidate'] : environments.production
  const jobs = ['rate-push', 'availability-sync', 'points-export', 'summer-season']
  addCatalog(b, next ? hotels : [...hotels, algarveCliffsVillas], env, jobs)

  if (next) {
    // Release 2026.11 brings digital check-in to the website and the app of Lisbon Marina and Algarve Cliffs.
    addResponsibility(b, digitalCheckIn, { createdDaysAgo: 10, createdBy: 'ada' })
    b.configure('rst.digital-check-in', {
      opensHoursBefore: 48,
      idScan: 'camera',
      arrivalTimeRequired: true,
      registrationCard: 'signed-in-app',
    })
    for (const hotel of [lisbonMarina, algarveCliffs]) {
      addSetups(b, hotel, digitalCheckIn, env, { createdDaysAgo: 10, createdBy: 'ada' })
    }
    return
  }

  // Housekeeping requests on the website, retired when they moved to the app.
  b.execution('lisbon-marina', 'housekeeping', 'direct-web', {
    title: 'Lisbon Marina: Housekeeping on the website',
    createdDaysAgo: 420,
  })
  b.trash('exe.lisbon-marina.housekeeping.direct-web', { daysAgo: 6, by: 'hedy' })
  // The villas became room types of Algarve Cliffs, so the wing left the channels with all its setups.
  b.trash(`sbt.${algarveCliffsVillas.name}`, algarveCliffsVillas.deleted!)
}

/** Development: the test property on the website, the app and the OTA simulator, with debug logging. */
function developmentView(b: ScopeBuilder) {
  addCatalog(b, [testProperty], environments.development, ['rate-push', 'availability-sync'])
}

/** Staging: Lisbon Marina and Algarve Cliffs on the OTAs' test endpoints, with test listings and keys. */
function stagingView(b: ScopeBuilder) {
  const tested: ChannelName[] = ['direct-web', 'mobile-app', 'booking-com', 'expedia']
  addCatalog(
    b,
    [lisbonMarina, algarveCliffs].map((hotel) => ({
      ...hotel,
      channels: hotel.channels.filter((channel) => tested.includes(channel)),
    })),
    environments.staging,
    ['rate-push', 'availability-sync', 'points-export'],
  )
}

/** The rate parity check, delivered to Lisbon Marina on its OTAs first. */
function featureDeliveryView(b: ScopeBuilder) {
  addCatalog(
    b,
    [
      {
        ...lisbonMarina,
        modules: ['booking-engine', 'rate-manager'],
        channels: ['direct-web', 'booking-com', 'expedia'],
      },
    ],
    environments['feature-delivery1'],
    ['rate-push', 'availability-sync', 'parity-check'],
  )
}

export const guestPlatform: ShowcaseProject = {
  org: 'seabreeze-hotels',
  project: 'guest-platform',
  summary:
    'Hotel distribution: four Portuguese hotels, their own and OTA sales channels, and the modules set up per channel.',
  members: {
    grace: 'Owner',
    frances: 'Administrator',
    ada: 'ContributorWithSecrets',
    hedy: 'Contributor',
    annie: 'Reader',
  },
  settings: {
    types: {
      rst: { label: 'Module', color: 'indigo', icon: 'i-lucide-puzzle' },
      unt: { label: 'Sync job', color: 'fuchsia', icon: 'i-lucide-workflow' },
      sbt: { label: 'Hotel', color: 'sky', icon: 'i-lucide-building-2' },
      usg: { label: 'License', color: 'amber', icon: 'i-lucide-file' },
      cnt: { label: 'Channel', color: 'teal', icon: 'i-lucide-globe' },
      exe: { label: 'Channel setup', color: 'emerald', icon: 'i-lucide-cog' },
      uxe: { label: 'Sync run', color: 'orange', icon: 'i-lucide-activity' },
    },
    defaultTimeZone: 'Europe/Lisbon',
    dateFormat: 'dd/mm/yyyy',
  },
  views: environmentViews({
    development: 'Development: the test property and the OTA simulator',
    staging: 'Staging: Lisbon Marina and Algarve Cliffs on the OTA test endpoints',
    'release-candidate': 'Release 2026.11: digital check-in and dynamic pricing',
    'feature-delivery1': 'Rate parity check for Lisbon Marina',
  }),
  build: (b) => {
    switch (b.view) {
      case 'default':
        return productionView(b, false)
      case 'release-candidate':
        return productionView(b, true)
      case 'development':
        return developmentView(b)
      case 'staging':
        return stagingView(b)
      case 'feature-delivery1':
        return featureDeliveryView(b)
    }
  },
}
