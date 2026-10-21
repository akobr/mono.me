import type { Meta, ScopeBuilder, UnitMeta } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// The distributed platform of the documentation (docs/42for.net/platform/examples/distributed-platform.md), example 5
// of the Miro board. Streamwave streams films and series in eight markets: the market is the subject, the cell the
// context, the service the responsibility and a pipeline inside a service the unit. Every market has a production
// cell and a canary cell, so a bad release meets a small share of the traffic before it is everywhere.

type Content = Record<string, unknown>

/** What a view shows: production, the playback release of the documentation, or the next platform release. */
type Release = 'live' | 'playback-14' | 'next'

const runbooks = 'https://runbooks.streamwave.example'

interface Market {
  name: string
  title: string
  description?: string
  timeZone: string
  /** Region and size tier; tier-1 markets get the new flags first. */
  labels: string[]
  /** The company that sells the subscriptions there; billing binds its invoice issuer to this value. */
  legalEntity: string
  lead: string
  launchedOn?: string
  /** Days until the launch of a market that is not live yet. */
  launchesIn?: number
  audioLanguage: string
  /** The territories whose viewers hear another language by default. */
  audioLanguages?: Record<string, string>
  /** The store currency of the market's legal entity. */
  currency: string
  /** The territories that pay in a currency of their own. */
  currencies?: Record<string, string>
  territories: string[]
  /** Days between a title's premiere and its availability in the market. */
  lagDays: number
  cloudRegion: string
  /** A second production cell in another region, closer to a part of the market's viewers. */
  secondCell?: { region: string; description: string }
  /** Traffic against Europe, for the replica counts. */
  scale: number
  /** The services the market runs: its licenses. */
  services: string[]
  /** Age verification is the law there (Online Safety Act, JMStV): a market-wide flag. */
  ageVerification?: boolean
  payments?: string[]
  ratings?: string[]
  /** The languages the subtitle pipeline translates into. */
  translations?: string[]
}

/** The services every live market runs. */
const core = ['playback', 'recommendations', 'search', 'catalog', 'profiles', 'auth', 'drm']
const stores = ['apple-iap', 'google-play']

const markets: Market[] = [
  {
    name: 'eu',
    title: 'Europe',
    description:
      'Western and Southern Europe: Benelux, France, Ireland, Iberia and Italy. DACH and the Nordics are markets of ' +
      'their own.',
    timeZone: 'Europe/Amsterdam',
    labels: ['emea', 'tier-1'],
    legalEntity: 'Streamwave International B.V.',
    lead: 'Sophie Janssen',
    launchedOn: '2019-03-12',
    audioLanguage: 'en',
    currency: 'EUR',
    territories: ['be', 'es', 'fr', 'ie', 'it', 'lu', 'nl', 'pt'],
    lagDays: 0,
    cloudRegion: 'eu-west-1',
    scale: 1,
    // No billing: Europe is billed by the app stores and the telco partners until Streamwave billing launches there.
    services: [...core, 'cdn-steering', 'subtitles', 'downloads', 'parental-controls', 'watch-history'],
    ratings: ['kijkwijzer', 'csa'],
    translations: ['es', 'fr', 'it', 'nl', 'pt'],
  },
  {
    name: 'us',
    title: 'United States',
    timeZone: 'America/New_York',
    labels: ['americas', 'tier-1'],
    legalEntity: 'Streamwave Inc.',
    lead: 'Marcus Reed',
    launchedOn: '2018-06-01',
    audioLanguage: 'en',
    currency: 'USD',
    territories: ['pr', 'us'],
    lagDays: 0,
    cloudRegion: 'us-east-1',
    secondCell: {
      region: 'us-west-2',
      description: 'Oregon (us-west-2) serves the West Coast and takes over when Virginia fails.',
    },
    scale: 1.6,
    services: [
      ...core,
      'billing',
      'cdn-steering',
      'downloads',
      'ads',
      'live-events',
      'notifications',
      'experiments',
      'gift-cards',
      'device-pairing',
    ],
    payments: ['stripe', 'paypal', ...stores],
  },
  {
    name: 'uk',
    title: 'United Kingdom',
    timeZone: 'Europe/London',
    labels: ['emea', 'tier-1'],
    legalEntity: 'Streamwave UK Ltd',
    lead: 'Olivia Hughes',
    launchedOn: '2019-03-12',
    audioLanguage: 'en',
    currency: 'GBP',
    territories: ['gb', 'gg', 'im', 'je'],
    lagDays: 0,
    cloudRegion: 'eu-west-2',
    scale: 0.6,
    services: [...core, 'billing', 'cdn-steering', 'ads', 'live-events', 'parental-controls', 'previews'],
    ageVerification: true,
    payments: ['adyen', 'paypal', ...stores],
    ratings: ['bbfc'],
  },
  {
    name: 'nordics',
    title: 'Nordics',
    timeZone: 'Europe/Stockholm',
    labels: ['emea', 'tier-2'],
    legalEntity: 'Streamwave Nordic AB',
    lead: 'Erik Lindqvist',
    launchedOn: '2020-02-04',
    audioLanguage: 'en',
    currency: 'SEK',
    currencies: { dk: 'DKK', fi: 'EUR', is: 'ISK', no: 'NOK' },
    territories: ['dk', 'fi', 'is', 'no', 'se'],
    lagDays: 0,
    cloudRegion: 'eu-north-1',
    scale: 0.3,
    services: [...core, 'billing', 'subtitles'],
    payments: ['adyen', 'klarna', ...stores],
    translations: ['da', 'fi', 'no', 'sv'],
  },
  {
    name: 'dach',
    title: 'DACH',
    description: 'Germany, Austria, Switzerland and Liechtenstein, dubbed in German.',
    timeZone: 'Europe/Berlin',
    labels: ['emea', 'tier-1'],
    legalEntity: 'Streamwave Deutschland GmbH',
    lead: 'Lena Hoffmann',
    launchedOn: '2019-10-01',
    audioLanguage: 'de',
    currency: 'EUR',
    currencies: { ch: 'CHF', li: 'CHF' },
    territories: ['at', 'ch', 'de', 'li'],
    lagDays: 0,
    cloudRegion: 'eu-central-1',
    scale: 0.7,
    services: [...core, 'billing', 'subtitles', 'parental-controls', 'live-events'],
    ageVerification: true,
    payments: ['adyen', 'sepa-direct-debit', 'paypal'],
    ratings: ['fsk'],
    translations: ['de'],
  },
  {
    name: 'latam',
    title: 'Latin America',
    timeZone: 'America/Sao_Paulo',
    labels: ['americas', 'tier-2'],
    legalEntity: 'Streamwave Brasil Ltda.',
    lead: 'Camila Duarte',
    launchedOn: '2021-05-18',
    audioLanguage: 'es',
    audioLanguages: { br: 'pt' },
    currency: 'BRL',
    currencies: { ar: 'ARS', cl: 'CLP', co: 'COP', mx: 'MXN', pe: 'PEN' },
    territories: ['ar', 'br', 'cl', 'co', 'mx', 'pe'],
    lagDays: 7,
    cloudRegion: 'sa-east-1',
    scale: 0.5,
    services: [...core, 'billing', 'ads'],
    payments: ['dlocal', ...stores],
  },
  {
    name: 'apac',
    title: 'Asia Pacific',
    timeZone: 'Asia/Singapore',
    labels: ['apac', 'tier-1'],
    legalEntity: 'Streamwave Asia Pacific Pte. Ltd.',
    lead: 'Wei Ling Tan',
    launchedOn: '2022-01-10',
    audioLanguage: 'en',
    currency: 'SGD',
    currencies: { au: 'AUD', my: 'MYR', nz: 'NZD', ph: 'PHP', th: 'THB' },
    territories: ['au', 'my', 'nz', 'ph', 'sg', 'th'],
    lagDays: 3,
    cloudRegion: 'ap-southeast-1',
    secondCell: {
      region: 'ap-southeast-2',
      description: 'Sydney (ap-southeast-2) serves Australia and New Zealand close to the viewers.',
    },
    scale: 0.9,
    services: [...core, 'billing', 'cdn-steering', 'downloads', 'ads'],
    payments: ['adyen', ...stores],
  },
  {
    name: 'mena',
    title: 'Middle East and North Africa',
    description:
      'Launches next quarter. The cells are built and the licenses signed; all of it starts on the launch day.',
    timeZone: 'Asia/Dubai',
    labels: ['emea', 'tier-2'],
    legalEntity: 'Streamwave MENA FZ-LLC',
    lead: 'Omar Haddad',
    launchesIn: 72,
    audioLanguage: 'ar',
    currency: 'AED',
    currencies: { bh: 'BHD', eg: 'EGP', kw: 'KWD', ma: 'MAD', om: 'OMR', qa: 'QAR', sa: 'SAR' },
    territories: ['ae', 'bh', 'eg', 'kw', 'ma', 'om', 'qa', 'sa'],
    lagDays: 14,
    cloudRegion: 'me-central-1',
    scale: 0.3,
    services: ['playback', 'search', 'catalog', 'auth', 'drm', 'billing', 'subtitles'],
    payments: ['checkout-com', ...stores],
    translations: ['ar', 'fr'],
  },
]

/** The test territory of staging: a fictional country code, so no real customer ever lands in it. */
const testTerritory: Market = {
  name: 'zz',
  title: 'Test territory ZZ',
  description: 'A fictional territory for end-to-end tests: test accounts, test cards and the full catalog.',
  timeZone: 'UTC',
  labels: ['test'],
  legalEntity: 'Streamwave International B.V.',
  lead: 'Radia Perlman',
  audioLanguage: 'en',
  currency: 'EUR',
  territories: ['zz'],
  lagDays: 0,
  cloudRegion: 'eu-west-1',
  scale: 0.05,
  services: [...core, 'billing'],
  payments: ['adyen-test'],
}

interface Service {
  name: string
  title: string
  /** Markdown. */
  description: string
  labels: string[]
  team: string
  /** Replicas of a production cell of Europe. */
  replicas: number
  /** Deployed in the canary cells, so its releases meet real traffic there first. */
  canary?: boolean
  /** Deployed in the second production cell too, in the markets that have one. */
  scaleOut?: boolean
  /** Days since the service joined the catalog, when it is newer than the platform. */
  since?: number
  /** The service's defaults, one version or several oldest first; playback and recommendations have their own. */
  document?: Content | Content[]
}

const services: Service[] = [
  {
    name: 'playback',
    title: 'Playback',
    description:
      'Starts and keeps streams going: the **manifest**, the ABR ladder and the license handoff to DRM.\n\n' +
      '- The codec ladder and the startup timeout are the service’s document.\n' +
      '- Rollout flags are off by the deployment template and turned on per deployment.',
    labels: ['edge'],
    team: 'Playback Core',
    replicas: 180,
    canary: true,
    scaleOut: true,
  },
  {
    name: 'recommendations',
    title: 'Recommendations',
    description: 'The rows of the home screen, ranked per profile by a model that every cell refreshes each night.',
    labels: ['ml'],
    team: 'Personalization',
    replicas: 60,
    canary: true,
  },
  {
    name: 'search',
    title: 'Search',
    description:
      'Title, people and genre search. New rankers meet production as shadow traffic, so search has no canary deployments.',
    labels: ['ml'],
    team: 'Discovery',
    replicas: 48,
    scaleOut: true,
    document: [
      { ranker: 'bm25', typoTolerance: 1, suggestLimit: 8 },
      { ranker: 'bm25-ltr', typoTolerance: 1, suggestLimit: 8 },
      { ranker: 'bm25-ltr', typoTolerance: 2, suggestLimit: 10 },
    ],
  },
  {
    name: 'billing',
    title: 'Billing',
    description:
      'Subscriptions, renewals and invoices.\n\nNot launched in **Europe** yet: subscriptions there are billed by the ' +
      'app stores and the telco partners, so `eu` has no billing license.',
    labels: ['money'],
    team: 'Payments',
    replicas: 24,
    document: { gracePeriodDays: 7, retryAfterDays: [1, 3, 7], prorateUpgrades: true },
  },
  {
    name: 'catalog',
    title: 'Catalog',
    description: 'Titles, seasons, artwork and the rights windows per territory.',
    labels: ['content'],
    team: 'Content Platform',
    replicas: 64,
    canary: true,
    document: { cacheTtlSeconds: 300, imageCdn: 'https://img.streamwave.example', maturityRatings: true },
  },
  {
    name: 'profiles',
    title: 'Profiles',
    description: 'Viewer profiles of an account, kids profiles included.',
    labels: ['identity'],
    team: 'Identity',
    replicas: 32,
    document: { maxProfiles: 5, kidsProfiles: true },
  },
  {
    name: 'auth',
    title: 'Sign-in',
    description: 'Sign-in, sessions and the tokens every other service checks.',
    labels: ['identity', 'edge'],
    team: 'Identity',
    replicas: 40,
    canary: true,
    scaleOut: true,
    document: [
      { sessionDays: 30, mfa: 'optional', providers: ['password', 'apple', 'google'] },
      { sessionDays: 60, mfa: 'optional', providers: ['password', 'apple', 'google'] },
    ],
  },
  {
    name: 'downloads',
    title: 'Downloads',
    description: 'Offline downloads on phones and tablets, with persistent licenses.',
    labels: ['edge'],
    team: 'Offline',
    replicas: 20,
    document: { maxTitlesPerDevice: 25, expiryHours: 48, defaultQuality: 'hd' },
  },
  {
    name: 'subtitles',
    title: 'Subtitles',
    description: 'Subtitle and caption tracks, and their machine translation.',
    labels: ['content'],
    team: 'Localization',
    replicas: 16,
    document: { formats: ['webvtt', 'ttml', 'imsc1'], burnInForcedNarratives: true },
  },
  {
    name: 'ads',
    title: 'Ads',
    description: 'Ad breaks of the ad-supported tier: decisioning, pods and frequency caps.',
    labels: ['money', 'ml'],
    team: 'Ad Tech',
    replicas: 36,
    canary: true,
    document: { podLengthSeconds: 90, frequencyCapPerHour: 3 },
  },
  {
    name: 'live-events',
    title: 'Live events',
    description: 'Live sport and concerts, with a DVR window and low-latency delivery.',
    labels: ['edge'],
    team: 'Live',
    replicas: 30,
    document: { dvrWindowMinutes: 120, latencyMode: 'low' },
  },
  {
    name: 'parental-controls',
    title: 'Parental controls',
    description: 'Maturity limits and the PIN, mapped to the rating systems of the market.',
    labels: ['identity'],
    team: 'Trust and Safety',
    replicas: 8,
    document: { pinLength: 4, defaultMaturity: 'teen' },
  },
  {
    name: 'notifications',
    title: 'Notifications',
    description: 'Push and e-mail: new episodes, reminders and account messages.',
    labels: ['engagement'],
    team: 'Messaging',
    replicas: 12,
    document: { channels: ['push', 'email'], quietHours: { from: '22:00', to: '08:00' } },
  },
  {
    name: 'drm',
    title: 'DRM licenses',
    description: 'Widevine, PlayReady and FairPlay licenses for every stream and download.',
    labels: ['edge'],
    team: 'Content Protection',
    replicas: 48,
    scaleOut: true,
    document: { systems: ['widevine', 'playready', 'fairplay'], licenseDurationHours: 48, persistentLicenses: true },
  },
  {
    name: 'cdn-steering',
    title: 'CDN steering',
    description: 'Chooses the CDN of each session from live probes and moves sessions away from a failing one.',
    labels: ['edge'],
    team: 'Delivery',
    replicas: 24,
    scaleOut: true,
    document: { probeIntervalSeconds: 30, failoverThresholdMs: 800 },
  },
  {
    name: 'watch-history',
    title: 'Watch history',
    description: 'Continue watching and the viewing history the recommendations learn from.',
    labels: ['data'],
    team: 'Personalization',
    replicas: 28,
  },
  {
    name: 'experiments',
    title: 'Experiments',
    description: 'A/B test assignment and the exposure log.',
    labels: ['ml'],
    team: 'Experimentation',
    replicas: 10,
    canary: true,
    since: 150,
  },
  {
    name: 'gift-cards',
    title: 'Gift cards',
    description: 'Gift card codes, sold in retail and redeemed in the apps.',
    labels: ['money'],
    team: 'Payments',
    replicas: 6,
    document: { codeLength: 16, validityMonths: 24 },
  },
  {
    name: 'device-pairing',
    title: 'Device pairing',
    description: 'Signs in a TV with a code shown on the screen.',
    labels: ['identity'],
    team: 'Devices',
    replicas: 8,
    document: { codeTtlSeconds: 600 },
  },
  {
    name: 'previews',
    title: 'Autoplay previews',
    description: 'Trailers that start when a title is in focus.',
    labels: ['edge'],
    team: 'Discovery',
    replicas: 20,
    since: 90,
  },
]

/** The new feature of feature-delivery1. */
const clips: Service = {
  name: 'clips',
  title: 'Clips',
  description: 'Short vertical clips in the home feed, cut by a model from the trailers.',
  labels: ['ml', 'feature'],
  team: 'Discovery',
  replicas: 16,
  canary: true,
  since: 10,
  document: { maxClipSeconds: 45, aspectRatio: '9:16', autoplay: true },
}

/** Ran in the US and the UK until the summer test ended; the service is in Deleted with its pipelines. */
const watchParties: Service = {
  name: 'watch-parties',
  title: 'Watch parties',
  description: 'Synchronized viewing with friends, with chat and reactions.',
  labels: ['social'],
  team: 'Social',
  replicas: 12,
  canary: true,
  since: 260,
  document: { maxGuests: 8, chat: true, reactions: true },
}

interface Pipeline {
  service: string
  name: string
  title: string
  description: string
  unitType: UnitMeta['unitType']
  definition: string
  document?: Content | Content[]
  /** Runs in every cell of its service; the others run once per market, in its primary cell. */
  everyCell?: boolean
  runsIn?: (market: Market) => boolean
}

const pipelines: Pipeline[] = [
  {
    service: 'recommendations',
    name: 'model-refresh',
    title: 'Model refresh',
    unitType: 'time',
    definition: '0 4 * * *',
    description:
      'Retrains the ranking model on the last four weeks of viewing and swaps it in. The canary cell runs first.',
    document: {
      trainingWindowDays: 28,
      featureStore: 'grpc://features.streamwave.example:6566',
      maxRuntimeMinutes: 90,
    },
    everyCell: true,
  },
  {
    service: 'search',
    name: 'index-rebuild',
    title: 'Index rebuild',
    unitType: 'time',
    definition: '30 1 * * 0',
    description:
      'Rebuilds the title index of the market early on Sunday morning, at 01:30 market time, and swaps the alias.',
    document: { shards: 12, aliasSwap: true },
  },
  {
    service: 'catalog',
    name: 'ingest',
    title: 'Catalog ingest',
    unitType: 'message',
    // Every run reads a queue of its own, catalog-ingest-<market>, with a dead-letter queue of its own.
    definition: 'catalog-ingest',
    description:
      'Takes the title deliveries of the studios from the market’s queue: metadata, artwork and rights windows.',
    document: { maxConcurrency: 16, maxDeliveries: 5 },
  },
  {
    service: 'ads',
    name: 'forecast',
    title: 'Inventory forecast',
    unitType: 'time',
    definition: '0 */6 * * *',
    description: 'Forecasts the ad inventory of the next two weeks for the sales team.',
    document: { horizonDays: 14 },
  },
  {
    service: 'billing',
    name: 'renewal-run',
    title: 'Renewal run',
    unitType: 'time',
    definition: '0 5 * * *',
    description: 'Charges the subscriptions that renew today, at five in the morning market time.',
    document: [
      { batchSize: 2000, retryFailedAfterHours: 24 },
      { batchSize: 5000, retryFailedAfterHours: 24 },
    ],
  },
  {
    service: 'subtitles',
    name: 'auto-translate',
    title: 'Subtitle translation',
    unitType: 'event',
    definition: 'subtitles.track-published',
    description: 'Translates a new subtitle track into the languages of the market; low scores go to human review.',
    document: { engine: 'nmt-v3', humanReviewBelowScore: 0.85 },
    runsIn: (market) => !!market.translations,
  },
  {
    service: 'live-events',
    name: 'schedule-sync',
    title: 'Schedule sync',
    unitType: 'file',
    definition: 'sftp://feeds.streamwave.example/schedules/*.xml',
    description: 'Reads the event schedules that the rights holders drop as XML files.',
  },
  {
    service: 'clips',
    name: 'highlight-extract',
    title: 'Highlight extraction',
    unitType: 'event',
    definition: 'catalog.trailer-published',
    description: 'Cuts the clips of a title when its trailer is published.',
    document: { model: 'highlight-v1', minScore: 0.7 },
    everyCell: true,
  },
  {
    service: 'watch-parties',
    name: 'room-cleanup',
    title: 'Room cleanup',
    unitType: 'time',
    definition: '0 * * * *',
    description: 'Closes the party rooms that have been empty for an hour.',
  },
  {
    service: 'watch-parties',
    name: 'invite-reminder',
    title: 'Invite reminder',
    unitType: 'event',
    definition: 'party.scheduled',
    description: 'Reminds the guests ten minutes before a party starts.',
  },
]

interface Cell {
  name: string
  /** For the titles: "Europe canary". */
  title: string
  kind: 'prod' | 'canary' | 'staging' | 'dev'
  /** Runs the pipelines that run once per market. */
  primary?: boolean
  /** The services deployed here when not the rule of its kind (deployedIn). */
  only?: readonly string[]
  /** Retired: its deployments expired with it. */
  retired?: boolean
  versions: Content[]
  meta?: Meta
}

/** Cell facts of the documentation: the share of the market's traffic, the tracing sample rate, the build channel. */
const cellFacts = (trafficShare: number, tracingSampleRate: number, buildChannel: string, cloudRegion: string) => ({
  trafficShare,
  tracingSampleRate,
  buildChannel,
  cloudRegion,
})

function productionCells(market: Market, release: Release): Cell[] {
  const second = market.secondCell
  const big = second !== undefined
  const region = market.cloudRegion
  const prod =
    market.name === 'eu'
      ? [
          cellFacts(0.99, 0.05, 'stable', region),
          cellFacts(0.98, 0.05, 'stable', region),
          cellFacts(0.95, 0.01, 'stable', region),
        ]
      : [cellFacts(big ? 0.6 : 0.95, 0.01, 'stable', region)]
  // Europe's canary grew from 1 % to 5 % of the traffic as the release train got safer.
  const canary =
    market.name === 'eu'
      ? [
          cellFacts(0.01, 1, 'canary', region),
          cellFacts(0.02, 0.5, 'canary', region),
          cellFacts(0.05, 0.5, 'canary', region),
          cellFacts(0.05, 0.25, 'canary', region),
        ]
      : // A cell-wide flag: HTTP/3 at the edge is on in the whole US canary cell.
        [
          {
            ...cellFacts(0.05, 0.25, 'canary', region),
            ...(market.name === 'us' ? { flags: { 'http3-edge': true } } : {}),
          },
        ]
  if (release === 'next') {
    canary.push({ ...canary.at(-1)!, buildChannel: 'candidate' })
  }

  return [
    { name: 'prod', title: 'production', kind: 'prod', primary: true, versions: prod },
    ...(second
      ? [
          {
            name: 'prod-2',
            title: 'production 2',
            kind: 'prod' as const,
            versions: [cellFacts(0.35, 0.01, 'stable', second.region)],
            meta: { description: second.description },
          },
        ]
      : []),
    { name: 'canary', title: 'canary', kind: 'canary', versions: canary },
    ...(market.name === 'latam'
      ? [
          {
            name: 'prod-iad',
            title: 'production (Virginia)',
            kind: 'prod' as const,
            only: ['playback', 'catalog', 'auth'],
            retired: true,
            versions: [cellFacts(0.95, 0.01, 'stable', 'us-east-1'), cellFacts(0, 0.01, 'stable', 'us-east-1')],
            // Drained to no traffic, its deployments scaled to zero, then retired.
            meta: {
              expiresIn: -12,
              labels: ['prod', 'retired'],
              description: 'Served Latin America from Virginia until the São Paulo cell took over.',
              modifiedDaysAgo: 12,
              modifiedBy: 'radia' as const,
            },
          },
        ]
      : []),
  ]
}

function deployedIn(service: Service, cell: Cell): boolean {
  if (cell.only) {
    return cell.only.includes(service.name)
  }
  if (cell.kind === 'canary') {
    return !!service.canary
  }
  return cell.name === 'prod-2' ? !!service.scaleOut : true
}

const replicasOf = (service: Service, market: Market, cell: Cell) =>
  Math.max(
    2,
    Math.round(service.replicas * market.scale * (cell.kind === 'canary' ? 0.1 : cell.name === 'prod-2' ? 0.6 : 1)),
  )

/** A market that has not launched yet is scheduled with everything in it. */
const launchOf = (market: Market): Meta =>
  market.launchesIn ? { startsIn: market.launchesIn, createdDaysAgo: 25, createdBy: 'ken' } : {}

/** A service newer than the platform brings its pipelines, licenses, deployments and runs along. */
const createdWith = (service: Service): Meta =>
  service.since ? { createdDaysAgo: service.since, createdBy: 'radia' } : {}

/** The licenses, deployments and runs of a service in a market: created with the newer of the two. */
function lifecycleOf(market: Market, service: Service): Meta {
  const launch = launchOf(market)
  return service.since && service.since < (launch.createdDaysAgo ?? 400)
    ? { ...launch, ...createdWith(service) }
    : launch
}

const flagsOff = (names: string[]) => Object.fromEntries(names.map((name) => [name, false]))

/** The flags a deployment turns on; the template turns them off everywhere else. */
const rolloutFlags = [
  'av1-ladder',
  'spatial-audio',
  'ml-ranking-v2',
  'server-side-ads',
  'passkeys',
  'graphql-gateway',
  'sequential-testing',
]

const pipelinesOf = (service: Service) => pipelines.filter((pipeline) => pipeline.service === service.name)

/** The services with their pipelines, and their documents: playback and recommendations have histories of their own. */
function addServices(b: ScopeBuilder, list: readonly Service[], release: Release) {
  for (const service of list) {
    const key = b.responsibility(service.name, {
      title: service.title,
      description: service.description,
      labels: service.labels,
      link: `${runbooks}/services/${service.name}`,
      values: { team: service.team, oncall: `#oncall-${service.name}` },
      ...createdWith(service),
    })
    if (service.name === 'playback') {
      b.configure(key, playbackVersions(release), { every: 6 })
    } else if (service.name === 'recommendations') {
      b.configure(key, recommendationVersions(release), { every: 20 })
    } else if (service.document) {
      b.configure(key, service.document, { every: 30 })
    }
    for (const pipeline of pipelinesOf(service)) {
      const unit = b.unit(service.name, pipeline.name, {
        unitType: pipeline.unitType,
        definition: pipeline.definition,
        title: pipeline.title,
        description: pipeline.description,
        link: `${runbooks}/services/${service.name}#${pipeline.name}`,
        ...createdWith(service),
      })
      if (pipeline.document) {
        b.configure(unit, pipeline.document, { every: 25 })
      }
    }
  }
}

/** The playback document of the documentation: the codec ladder, the startup timeout and the flags it understands. */
function playbackVersions(release: Release): Content[] {
  return evolve(
    {
      codecLadder: { h264: [240, 360, 480, 720, 1080], hevc: [720, 1080, 2160] },
      startupTimeoutMs: 6000,
      abr: { algorithm: 'buffer-based', minBufferSeconds: 10 },
      featureFlags: ['av1-ladder'],
    },
    release === 'live' ? 16 : 17,
    (version) => {
      switch (version) {
        case 4:
          return { featureFlags: ['av1-ladder', 'spatial-audio'] }
        case 6:
          return { codecLadder: { hevc: [720, 1080, 1440, 2160] } }
        case 8:
          return { startupTimeoutMs: 5000 }
        case 11:
          return { abr: { algorithm: 'hybrid' } }
        case 14:
          return { startupTimeoutMs: 4500 }
        case 17:
          // Release 14 of playback starts faster; the next platform release understands low-latency HLS.
          return release === 'playback-14'
            ? { startupTimeoutMs: 4000, abr: { algorithm: 'hybrid-v2' } }
            : { featureFlags: ['av1-ladder', 'spatial-audio', 'low-latency-hls'] }
        default:
          return { abr: { minBufferSeconds: 6 + (version % 3) * 2 } }
      }
    },
  )
}

/** The new ladder of playback release 14, tried in the canary cells: AV1 on top, the lowest H.264 rung dropped. */
const release14Ladder = { h264: [360, 540, 720, 1080], av1: [360, 540, 720, 1080, 1440, 2160] }

/** The ranking model of the home screen; the next release ships v9, which the canary cells already try. */
function recommendationVersions(release: Release): Content[] {
  const model = (version: string, freshnessBoost: number) => ({
    model: { family: 'two-tower', version },
    rowsPerPage: 40,
    freshnessBoost,
  })
  return [
    model('v6', 0.1),
    model('v7', 0.1),
    model('v8', 0.15),
    { ...model('v8', 0.15), rowsPerPage: 48 },
    ...(release === 'next' ? [{ ...model('v9', 0.15), rowsPerPage: 48 }] : []),
  ]
}

/** The template and the schema of the deployments. */
function deploymentDefaults(b: ScopeBuilder, release: Release) {
  // Every deployment inherits the rollout defaults and starts with the flags off (distributed-platform.md).
  const rollout = { strategy: 'rolling', timeoutMinutes: 20, autoRollback: true }
  b.template(
    'exe',
    [
      { rollout: { ...rollout, timeoutMinutes: 30 }, flags: flagsOff(rolloutFlags.slice(0, 4)) },
      { rollout, flags: flagsOff(rolloutFlags) },
      ...(release === 'next' ? [{ rollout, flags: flagsOff([...rolloutFlags, 'low-latency-hls']) }] : []),
    ],
    { every: 35 },
  )

  // The deployment schema types the rollout flags and limits the traffic share to a fraction; the next release
  // also rejects a flag nobody knows, so a typo fails the save instead of shipping a flag that never turns on.
  const flags = { type: 'object', description: 'Rollout flags, on or off.', additionalProperties: { type: 'boolean' } }
  const typed = {
    type: 'object',
    properties: {
      flags,
      replicas: { type: 'integer', minimum: 0, maximum: 1000 },
      autoscaling: {
        type: 'object',
        properties: {
          min: { type: 'integer', minimum: 1 },
          max: { type: 'integer', minimum: 1 },
          targetCpuUtilization: { type: 'number', exclusiveMinimum: 0, maximum: 1 },
        },
      },
    },
  }
  const limited = {
    ...typed,
    properties: {
      ...typed.properties,
      trafficShare: {
        type: 'number',
        description: 'The share of the market’s traffic this deployment takes, a fraction from 0 to 1.',
        minimum: 0,
        maximum: 1,
      },
    },
  }
  b.typeSchema(
    'exe',
    [
      typed,
      limited,
      ...(release === 'next'
        ? [
            {
              ...limited,
              properties: {
                ...limited.properties,
                flags: { ...flags, propertyNames: { enum: [...rolloutFlags, 'low-latency-hls'] } },
              },
            },
          ]
        : []),
    ],
    { every: 40, lastDaysAgo: release === 'next' ? 2 : 12 },
  )
}

/** Market facts of the documentation: the default audio language, the catalog window and the store's currency. */
function marketVersions(market: Market): Content[] {
  const facts = (territories: string[], availabilityLagDays: number) => ({
    defaultAudioLanguage: market.audioLanguage,
    ...(market.audioLanguages ? { territoryAudioLanguages: market.audioLanguages } : {}),
    catalogWindow: { territories, availabilityLagDays },
    storeCurrency: market.currency,
    ...(market.currencies ? { territoryCurrencies: market.currencies } : {}),
    // A market-wide flag: the law of the market asks for an age check before mature titles.
    ...(market.ageVerification ? { flags: { 'age-verification': true } } : {}),
  })
  return market.name === 'eu'
    ? [
        facts(['be', 'es', 'fr', 'it', 'lu', 'nl'], 7),
        facts(['be', 'es', 'fr', 'ie', 'it', 'lu', 'nl'], 0),
        facts(market.territories, 0),
      ]
    : [facts(market.territories, market.lagDays)]
}

function addMarket(b: ScopeBuilder, market: Market) {
  b.subject(market.name, {
    title: market.title,
    description: market.description,
    labels: market.labels,
    values: {
      legalEntity: market.legalEntity,
      marketLead: market.lead,
      ...(market.launchedOn ? { launchedOn: market.launchedOn } : {}),
    },
    timeZone: market.timeZone,
    ...launchOf(market),
  })
  b.configure(`sbt.${market.name}`, marketVersions(market), { every: 60, lastDaysAgo: 10 })
}

function addCell(b: ScopeBuilder, market: Market, cell: Cell) {
  b.context(market.name, cell.name, {
    title: `${market.title} ${cell.title}`,
    labels: [cell.kind],
    values: { cluster: `eks-${String(cell.versions[0]!.cloudRegion)}-${cell.name}`, oncall: `#oncall-${market.name}` },
    link: `${runbooks}/cells/${market.name}-${cell.name}`,
    timeZone: market.timeZone,
    ...launchOf(market),
    ...cell.meta,
  })
  // A retired cell's last change was the drain of its traffic, before it expired; the cells of a market that has not
  // launched yet were configured after they were built.
  b.configure(`cnt.${market.name}.${cell.name}`, cell.versions, {
    every: market.launchesIn ? 10 : 30,
    lastDaysAgo: cell.retired ? 16 : 1,
  })
}

/** Live rights per market; the contracts end, so the licenses expire with them. */
const liveRights: Record<string, { sports: string[]; endsIn: number }> = {
  us: { sports: ['boxing', 'motorsport'], endsIn: 410 },
  uk: { sports: ['darts', 'football-highlights'], endsIn: 220 },
  dach: { sports: ['handball', 'football-highlights'], endsIn: 250 },
}

const localContentShare: Record<string, number> = { eu: 0.25, dach: 0.35, nordics: 0.3, latam: 0.4, apac: 0.2 }

/** The package a market has licensed for one service; for playback, which content tiers exist there. */
function licenseVersions(market: Market, service: Service): Content[] | undefined {
  const withAds = market.services.includes('ads')
  switch (service.name) {
    case 'playback': {
      const licensed = {
        package: withAds ? 'hybrid' : 'svod',
        contentTiers: [
          ...(withAds ? ['basic-with-ads'] : []),
          ...(market.name === 'apac' ? ['mobile'] : []),
          'standard',
          'premium',
        ],
        maxStreams: 4,
      }
      return market.name === 'eu' ? [{ ...licensed, contentTiers: ['standard'], maxStreams: 2 }, licensed] : [licensed]
    }
    case 'recommendations':
      return localContentShare[market.name] ? [{ localContentShare: localContentShare[market.name] }] : undefined
    case 'billing':
      return [{ paymentProviders: market.payments ?? [], taxMode: market.name === 'us' ? 'exclusive' : 'inclusive' }]
    case 'ads':
      return [{ adServer: `https://ads.streamwave.example/vast/${market.name}`, maxAdMinutesPerHour: 4 }]
    case 'downloads':
      return [{ maxDevices: market.scale > 1 ? 6 : 4 }]
    case 'parental-controls':
      return [{ ratingSystems: market.ratings ?? [] }]
    case 'live-events':
      return liveRights[market.name] ? [{ sports: liveRights[market.name]!.sports, maxConcurrentEvents: 4 }] : undefined
    case 'clips':
      return [{ package: 'clips-pilot' }]
    default:
      return undefined
  }
}

function addLicense(b: ScopeBuilder, market: Market, service: Service) {
  const rights = service.name === 'live-events' ? liveRights[market.name] : undefined
  const key = b.usage(market.name, service.name, {
    title: `${service.title} in ${market.title}`,
    timeZone: market.timeZone,
    ...lifecycleOf(market, service),
    ...(rights
      ? { expiresIn: rights.endsIn, description: 'Licensed until the end of the current rights contract.' }
      : {}),
  })
  const versions = licenseVersions(market, service)
  if (versions) {
    b.configure(key, versions, { every: 45, lastDaysAgo: 8 })
  }
}

/** Canary deployments carry the flags that are on in canary and still off in production. */
function canaryVersions(market: Market, service: Service, cell: Cell, release: Release): Content[] | undefined {
  const first = market.labels.includes('tier-1')
  switch (service.name) {
    case 'playback': {
      const versions: Content[] = [{ replicas: replicasOf(service, market, cell) }]
      if (first) {
        versions.push({ ...versions[0], flags: { 'av1-ladder': true } })
      }
      if (release === 'playback-14') {
        versions.push({ ...versions.at(-1), flags: { 'av1-ladder': true }, codecLadder: release14Ladder })
      }
      if (release === 'next') {
        versions.push({ ...versions.at(-1), flags: { 'av1-ladder': true, 'low-latency-hls': true } })
      }
      return versions
    }
    case 'recommendations': {
      const candidate = { model: { version: 'v9' } }
      return release === 'next' ? [candidate, { ...candidate, flags: { 'ml-ranking-v2': true } }] : [candidate]
    }
    case 'catalog':
      return [{ flags: { 'graphql-gateway': true } }]
    case 'auth':
      return first ? [{ flags: { passkeys: true } }] : undefined
    case 'ads':
      if (market.name === 'us') {
        // The non-compliant one: 20 was meant as 20 % and saved before the schema limited the share to a fraction
        // (deploymentHistory). The copy of production for playback 14 stores the corrected value since, so only
        // production fails its schema.
        const test = { flags: { 'server-side-ads': true } }
        return [test, { ...test, trafficShare: 20 }, ...(release === 'live' ? [] : [{ ...test, trafficShare: 0.2 }])]
      }
      return market.name === 'uk' ? [{ flags: { 'server-side-ads': true }, trafficShare: 0.02 }] : undefined
    case 'experiments':
      return [{ flags: { 'sequential-testing': true } }]
    case 'clips':
      return [{ flags: { clips: true }, trafficShare: 0.02 }]
    default:
      return undefined
  }
}

/** The license server of a cell, with its signing key in the vault. */
const drmEndpoint = (host: string, secret: string) => ({
  licenseServer: `https://${host}/widevine`,
  signingKey: `@(drm.${secret}, primaryVault)`,
})

/** Production deployments: replicas, endpoints, and the bindings to the market, the cell and the vault. */
function productionVersions(market: Market, service: Service, cell: Cell, release: Release): Content[] | undefined {
  const replicas = replicasOf(service, market, cell)
  if (cell.retired) {
    return [{ replicas }, { replicas: 0 }]
  }
  switch (service.name) {
    case 'playback':
      // The US production cell follows the evening peaks; the ceiling went up for the finale weekend.
      if (market.name === 'us' && cell.name === 'prod') {
        return evolve({ replicas, autoscaling: { min: 160, max: 480, targetCpuUtilization: 0.6 } }, 20, (version) =>
          version === 9
            ? { autoscaling: { max: 560 } }
            : version === 15
              ? { autoscaling: { targetCpuUtilization: 0.55 } }
              : { replicas: replicas + (version % 4) * 16 },
        )
      }
      // Release 14 overrides two values in Europe production alone: a shorter start buffer, and more replicas for
      // the manifest requests of the faster starts. The view diff shows them next to what the service changes.
      return release === 'playback-14' && market.name === 'eu' && cell.name === 'prod'
        ? [{ replicas }, { replicas: replicas + 20, abr: { minBufferSeconds: 6 } }]
        : [{ replicas }]
    case 'search':
      return [cell.primary ? { replicas, indexAlias: `titles-${market.name}` } : { replicas }]
    case 'drm':
      return [drmEndpoint(`drm-${cell.name}.${market.name}.streamwave.example`, `${market.name}-signing-key`)]
    case 'billing':
      return [
        {
          invoiceIssuer: '@annotation("/legalEntity", "Subject")',
          pspWebhookSecret: `@(billing.${market.name}-psp-webhook, primaryVault)`,
        },
      ]
    case 'downloads':
      return [{ storage: { bucket: `streamwave-downloads-${market.name}`, region: '@config("/cloudRegion")' } }]
    case 'cdn-steering':
      return [
        {
          weights:
            market.name === 'apac' ? { akamai: 0.6, cloudfront: 0.4 } : { akamai: 0.5, fastly: 0.3, cloudfront: 0.2 },
        },
      ]
    case 'live-events':
      return [{ ingestUrl: `rtmps://ingest-${cell.name}.${market.name}.streamwave.example/live` }]
    case 'auth':
    case 'catalog':
      return [{ replicas }]
    default:
      return undefined
  }
}

function stagingVersions(market: Market, service: Service, cell: Cell): Content[] | undefined {
  // The load-test cell replays the evening peak, so it runs production's replica counts; the others stay small.
  const loadTest = cell.name === 'load-test'
  const replicas = loadTest ? replicasOf(service, market, cell) : 4
  switch (service.name) {
    case 'playback':
      return [{ replicas, flags: { 'av1-ladder': true } }]
    case 'drm':
      return [drmEndpoint('drm.staging.streamwave.example', 'staging-signing-key')]
    case 'billing':
      return [
        {
          invoiceIssuer: '@annotation("/legalEntity", "Subject")',
          pspMode: 'test',
          pspWebhookSecret: '@(billing.staging-psp-webhook, primaryVault)',
        },
      ]
    case 'search':
      return [{ replicas: 2, indexAlias: `titles-${market.name}-staging` }]
    default:
      return loadTest ? [{ replicas }] : undefined
  }
}

/** Developer cells talk to the sandboxes and turn every flag of the work in progress on. */
function developmentVersions(service: Service): Content[] | undefined {
  switch (service.name) {
    case 'playback':
      return [{ flags: { 'av1-ladder': true, 'spatial-audio': true } }]
    case 'drm':
      return [drmEndpoint('drm-sandbox.streamwave.example', 'sandbox-signing-key')]
    case 'catalog':
      return [{ source: 'https://catalog-sandbox.streamwave.example/v2' }]
    case 'auth':
      return [{ identityProvider: 'https://login-sandbox.streamwave.example', testAccounts: true }]
    case 'recommendations':
      return [{ model: { version: 'v9' } }]
    case 'search':
      return [{ replicas: 1 }]
    default:
      return undefined
  }
}

function deploymentVersions(market: Market, service: Service, cell: Cell, release: Release) {
  switch (cell.kind) {
    case 'canary':
      return canaryVersions(market, service, cell, release)
    case 'staging':
      return stagingVersions(market, service, cell)
    case 'dev':
      return developmentVersions(service)
    case 'prod':
      return productionVersions(market, service, cell, release)
  }
}

const usAdsCanary = (market: Market, service: Service, cell: Cell) =>
  market.name === 'us' && service.name === 'ads' && cell.kind === 'canary'

/** The lifecycle and notes of single deployments. */
function deploymentMeta(market: Market, service: Service, cell: Cell, release: Release): Meta {
  if (market.name === 'latam' && service.name === 'ads') {
    return {
      disabled: true,
      description: 'Paused, canary included, while the new ad-server contract for Latin America is signed.',
      modifiedDaysAgo: 9,
      modifiedBy: 'donald',
    }
  }
  if (usAdsCanary(market, service, cell) && release === 'live') {
    return {
      description:
        'Stores `trafficShare: 20`, meant as 20 %. It was saved before the deployment schema limited the traffic ' +
        'share to a fraction from 0 to 1, so the stored value now shows as **non-compliant**. The `playback-14` ' +
        'view already stores the fraction, 0.2.',
    }
  }
  return {}
}

/** When the versions of a deployment were saved. */
function deploymentHistory(
  market: Market,
  service: Service,
  cell: Cell,
  release: Release,
  count: number,
): HistoryOptions {
  if (cell.retired) {
    // Scaled to zero after the cell's drain and before it expired.
    return { every: 10, lastDaysAgo: 15 }
  }
  if (usAdsCanary(market, service, cell)) {
    // The share of 20 was saved 18 days ago, before schema version 2 of 12 days ago; the fix of playback 14 is new.
    return { every: 17, lastDaysAgo: release === 'live' ? 18 : 1 }
  }
  return { every: count > 5 ? 3 : 10 }
}

function runVersions(market: Market, pipeline: Pipeline, cell: Cell): Content[] | undefined {
  switch (`${pipeline.service}.${pipeline.name}`) {
    case 'recommendations.model-refresh':
      // The canary cell refreshes the model two hours before production does (distributed-platform.md).
      if (cell.kind === 'canary') {
        return market.name === 'eu' ? [{ cron: '0 3 * * *' }, { cron: '0 2 * * *' }] : [{ cron: '0 2 * * *' }]
      }
      if (cell.kind === 'dev') {
        return [{ cron: '*/30 * * * *', trainingWindowDays: 1 }]
      }
      if (cell.kind === 'staging') {
        return [{ cron: '0 */6 * * *', trainingWindowDays: 7 }]
      }
      // The US has the most viewing to train on, so its refresh may run longer.
      return market.scale > 1 ? [{ maxRuntimeMinutes: 150 }] : undefined
    case 'search.index-rebuild':
      if (cell.kind === 'dev') {
        return [{ cron: '0 * * * *', shards: 1 }]
      }
      if (cell.kind === 'staging') {
        return [{ shards: 2 }]
      }
      // One rebuild per market: in a market with a second production cell it ships the new index there too.
      return market.secondCell && cell.primary ? [{ replicateTo: ['prod-2'] }] : undefined
    case 'catalog.ingest': {
      const queue =
        cell.kind === 'prod' ? `catalog-ingest-${market.name}` : `catalog-ingest-${market.name}-${cell.name}`
      return [{ queue, deadLetterQueue: `${queue}-dlq` }]
    }
    case 'subtitles.auto-translate':
      return [{ targetLanguages: market.translations ?? [] }]
    case 'ads.forecast':
      return market.name === 'us' ? [{ horizonDays: 28 }] : undefined
    case 'billing.renewal-run':
      return cell.kind === 'staging' ? [{ dryRun: true }] : market.name === 'us' ? [{ batchSize: 20000 }] : undefined
    case 'clips.highlight-extract':
      return [{ maxClipsPerTitle: 5 }]
    default:
      return undefined
  }
}

function run(b: ScopeBuilder, market: Market, pipeline: Pipeline, cell: Cell, lifecycle: Meta) {
  const key = b.unitOfExecution(market.name, pipeline.service, cell.name, pipeline.name, {
    title: `${pipeline.title}, ${market.title} ${cell.title}`,
    timeZone: market.timeZone,
    ...lifecycle,
  })
  const versions = runVersions(market, pipeline, cell)
  if (versions) {
    b.configure(key, versions, { every: 12 })
  }
}

/** A service in one cell, with the runs of its pipelines there. */
function deploy(b: ScopeBuilder, market: Market, service: Service, cell: Cell, release: Release) {
  const lifecycle: Meta = {
    ...lifecycleOf(market, service),
    ...(cell.retired ? { expiresIn: cell.meta?.expiresIn } : {}),
  }
  const meta = deploymentMeta(market, service, cell, release)
  const key = b.execution(market.name, service.name, cell.name, {
    title: `${service.title}, ${market.title} ${cell.title}`,
    values: { argoApp: `${service.name}-${market.name}-${cell.name}` },
    timeZone: market.timeZone,
    ...lifecycle,
    ...meta,
  })
  const versions = deploymentVersions(market, service, cell, release)
  if (versions) {
    b.configure(key, versions, deploymentHistory(market, service, cell, release, versions.length))
  }
  for (const pipeline of pipelinesOf(service)) {
    const runs = (pipeline.everyCell || cell.primary) && (pipeline.runsIn?.(market) ?? true)
    if (runs && b.has(`unt.${pipeline.service}.${pipeline.name}`)) {
      // A paused deployment pauses its runs too.
      run(b, market, pipeline, cell, { ...lifecycle, ...(meta.disabled ? { disabled: true } : {}) })
    }
  }
}

const marketOf = (name: string) => markets.find((market) => market.name === name)!
const servicesNamed = (names: readonly string[]) => services.filter((service) => names.includes(service.name))

/** Production and its copies: every market, its licenses and its cells. */
function production(b: ScopeBuilder, release: Release) {
  addServices(b, services, release)
  deploymentDefaults(b, release)
  for (const market of markets) {
    addMarket(b, market)
    const licensed = servicesNamed(market.services)
    licensed.forEach((service) => addLicense(b, market, service))
    for (const cell of productionCells(market, release)) {
      addCell(b, market, cell)
      licensed
        .filter((service) => deployedIn(service, cell))
        .forEach((service) => deploy(b, market, service, cell, release))
    }
  }
  if (release !== 'live') {
    return
  }

  // Watch parties ran in the US and the UK until the summer test ended; the service went to Deleted with its
  // pipelines, licenses and deployments.
  addServices(b, [watchParties], release)
  for (const market of [marketOf('us'), marketOf('uk')]) {
    addLicense(b, market, watchParties)
    for (const cell of productionCells(market, release).filter((candidate) => deployedIn(watchParties, candidate))) {
      deploy(b, market, watchParties, cell, release)
    }
  }
  b.trash('rst.watch-parties', { daysAgo: 21, by: 'ken' })

  // The index rebuild runs once per market; the duplicate run in the second APAC cell was deleted alone.
  const apac = marketOf('apac')
  const second = productionCells(apac, release).find((cell) => cell.name === 'prod-2')!
  const rebuild = pipelines.find((pipeline) => pipeline.name === 'index-rebuild')!
  run(b, apac, rebuild, second, {})
  b.trash('uxe.apac.search.prod-2.index-rebuild', { daysAgo: 6, by: 'donald' })
}

/**
 * The board's release candidate: only the canary cells, with playback, recommendations and the canary model refresh,
 * in every market, with the flags of the next release.
 */
function releaseCandidate(b: ScopeBuilder) {
  const shipped = servicesNamed(['playback', 'recommendations'])
  addServices(b, shipped, 'next')
  deploymentDefaults(b, 'next')
  for (const market of markets) {
    addMarket(b, market)
    const canary = productionCells(market, 'next').find((cell) => cell.kind === 'canary')!
    addCell(b, market, canary)
    for (const service of shipped.filter((candidate) => market.services.includes(candidate.name))) {
      addLicense(b, market, service)
      deploy(b, market, service, canary, 'next')
    }
  }
}

/** Staging: one staging cell per market, a load-test cell in the US, and the test territory. */
function staging(b: ScopeBuilder) {
  const staged = servicesNamed(['playback', 'recommendations', 'search', 'billing', 'catalog', 'auth', 'drm'])
  addServices(b, staged, 'live')
  deploymentDefaults(b, 'live')
  for (const market of [marketOf('eu'), marketOf('us'), marketOf('apac'), testTerritory]) {
    addMarket(b, market)
    const licensed = staged.filter((service) => market.services.includes(service.name))
    licensed.forEach((service) => addLicense(b, market, service))
    const cells: Cell[] = [
      {
        name: 'staging',
        title: 'staging',
        kind: 'staging',
        primary: true,
        versions: [cellFacts(0, 1, 'beta', market.cloudRegion)],
      },
      ...(market.name === 'us'
        ? [
            {
              name: 'load-test',
              title: 'load test',
              kind: 'staging' as const,
              only: ['playback', 'catalog', 'auth', 'drm'],
              versions: [{ ...cellFacts(0, 0.05, 'beta', market.cloudRegion), syntheticViewers: 250000 }],
              meta: {
                description:
                  'Replays the recorded evening peak of the US against production-sized deployments ' +
                  'before every release.',
              },
            },
          ]
        : []),
    ]
    for (const cell of cells) {
      addCell(b, market, cell)
      licensed
        .filter((service) => deployedIn(service, cell))
        .forEach((service) => deploy(b, market, service, cell, 'live'))
    }
  }
}

/** Development: developer cells in Europe and the US, a device lab, sandbox endpoints and debug logging. */
function development(b: ScopeBuilder) {
  const developed = servicesNamed(['playback', 'recommendations', 'search', 'catalog', 'auth', 'drm'])
  addServices(b, developed, 'live')
  deploymentDefaults(b, 'live')
  const developer = (cloudRegion: string, owner: string) => ({
    versions: [{ ...cellFacts(0, 1, 'nightly', cloudRegion), logLevel: 'debug' }],
    meta: { values: { cluster: `eks-dev-${cloudRegion}`, owner }, labels: ['dev'] },
  })
  const cellsOf: Record<string, Cell[]> = {
    eu: [
      { name: 'dev', title: 'development', kind: 'dev', primary: true, ...developer('eu-west-1', 'Donald Knuth') },
      {
        name: 'qa',
        title: 'QA',
        kind: 'dev',
        primary: true,
        only: ['playback', 'search', 'catalog', 'auth', 'drm'],
        ...developer('eu-west-1', 'Grace Hopper'),
      },
      {
        name: 'device-lab',
        title: 'device lab',
        kind: 'dev',
        only: ['playback', 'drm'],
        versions: [
          {
            ...cellFacts(0, 1, 'nightly', 'eu-west-1'),
            devices: ['tizen-2024', 'webos-24', 'android-tv-14', 'apple-tv-4k', 'ps5', 'xbox-series-x'],
          },
        ],
        meta: {
          description: 'Real TVs, consoles and set-top boxes in the Amsterdam office, for the playback tests.',
          values: { location: 'Amsterdam, 4th floor', owner: 'Radia Perlman' },
          labels: ['dev', 'test-rig'],
        },
      },
    ],
    us: [
      {
        name: 'dev',
        title: 'development',
        kind: 'dev',
        primary: true,
        only: ['playback', 'search', 'catalog', 'auth'],
        ...developer('us-east-1', 'Ken Thompson'),
      },
    ],
  }
  for (const [name, cells] of Object.entries(cellsOf)) {
    const market = marketOf(name)
    addMarket(b, market)
    const licensed = developed.filter((service) => cells.some((cell) => deployedIn(service, cell)))
    licensed.forEach((service) => addLicense(b, market, service))
    for (const cell of cells) {
      addCell(b, market, cell)
      licensed
        .filter((service) => deployedIn(service, cell))
        .forEach((service) => deploy(b, market, service, cell, 'live'))
    }
  }
}

/** Clips, the new feature, delivered to the Nordic canary cell first. */
function featureDelivery(b: ScopeBuilder) {
  const delivered = [...servicesNamed(['playback', 'catalog']), clips]
  addServices(b, delivered, 'live')
  deploymentDefaults(b, 'live')
  const nordics = marketOf('nordics')
  addMarket(b, nordics)
  delivered.forEach((service) => addLicense(b, nordics, service))
  for (const cell of productionCells(nordics, 'live')) {
    addCell(b, nordics, cell)
    delivered
      .filter((service) => service !== clips || cell.kind === 'canary')
      .forEach((service) => deploy(b, nordics, service, cell, 'live'))
  }
}

export const streamingPlatform: ShowcaseProject = {
  org: 'streamwave',
  project: 'streaming-platform',
  description:
    'Streaming platform across markets. Each market has production and canary cells, and services such as playback, recommendations and search are deployed per cell.',
  members: { ken: 'Owner', ada: 'Administrator', radia: 'Administrator', donald: 'Contributor', grace: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Service', color: 'violet', icon: 'i-lucide-component' },
      unt: { label: 'Pipeline', color: 'fuchsia', icon: 'i-lucide-workflow' },
      sbt: { label: 'Market', color: 'blue', icon: 'i-lucide-globe' },
      usg: { label: 'License', color: 'amber', icon: 'i-lucide-key' },
      cnt: { label: 'Cell', color: 'cyan', icon: 'i-lucide-hexagon' },
      exe: { label: 'Deployment', color: 'emerald', icon: 'i-lucide-rocket' },
      uxe: { label: 'PipelineRun', color: 'orange', icon: 'i-lucide-play' },
    },
    defaultTimeZone: 'Europe/Amsterdam',
    dateFormat: 'yyyy-mm-dd',
  },
  views: environmentViews(
    {
      development: 'Development: developer cells and the device lab, sandbox endpoints',
      staging: 'Staging cells of Europe, the US and Asia Pacific, and the test territory',
      'release-candidate': 'Next release in every canary cell: low-latency HLS and ranking v2',
      'feature-delivery1': 'Clips in the Nordic canary cell',
    },
    [{ name: 'playback-14', description: 'Playback release 14: the new codec ladder in the canary cells' }],
  ),
  build: (b) => {
    switch (b.view) {
      case 'default':
        return production(b, 'live')
      case 'playback-14':
        return production(b, 'playback-14')
      case 'release-candidate':
        return releaseCandidate(b)
      case 'development':
        return development(b)
      case 'staging':
        return staging(b)
      case 'feature-delivery1':
        return featureDelivery(b)
    }
  },
  nonCompliant: ['exe.us.ads.canary'],
}
