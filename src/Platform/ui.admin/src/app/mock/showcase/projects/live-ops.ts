import type { Meta, ScopeBuilder } from '../builder'
import { evolve } from '../history'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// Game live operations, a new example of the showcase (spec section 9.12). Pixel Forge runs the online backend of its
// three games. A game (subject) ships on platform regions (contexts) and buys a service plan (usage) of each backend
// service (responsibility) it needs; a service deployment (execution) is one service in one platform region. Live
// events are the units of the events service, and an event run (unit of execution) is one event in one platform
// region: its window (ValidFrom, ExpiresAt) makes past events expired and coming events scheduled.

const docs = 'https://docs.pixelforge.example/live-ops'

type Stage = 'production' | 'staging' | 'development'
type Platform = 'steam' | 'playstation' | 'nintendo' | 'xbox' | 'mobile'
type Market = 'eu' | 'na' | 'jp' | 'apac'
type RegionName = 'steam-eu' | 'steam-na' | 'ps5-eu' | 'ps5-na' | 'switch-jp' | 'xbox-eu' | 'mobile-apac'
type Service = 'matchmaking' | 'store' | 'events' | 'anti-cheat' | 'leaderboards' | 'cloud-saves'
type EventName = 'winter-festival' | 'double-xp-weekend' | 'spring-sale' | 'anniversary' | 'halloween-hunt'

interface PlatformRegion {
  title: string
  platform: Platform
  market: Market
  cloudRegion: string
  timeZone: string
  /** The currency of the platform's store in the region. */
  currency: string
  /** The relative traffic weight of the region, for queue sizes and warm servers. */
  share: number
}

const platformRegions: Record<RegionName, PlatformRegion> = {
  'steam-eu': {
    title: 'Steam Europe',
    platform: 'steam',
    market: 'eu',
    cloudRegion: 'eu-central-1',
    timeZone: 'Europe/Berlin',
    currency: 'EUR',
    share: 0.3,
  },
  'steam-na': {
    title: 'Steam North America',
    platform: 'steam',
    market: 'na',
    cloudRegion: 'us-east-1',
    timeZone: 'America/New_York',
    currency: 'USD',
    share: 0.25,
  },
  'ps5-eu': {
    title: 'PlayStation 5 Europe',
    platform: 'playstation',
    market: 'eu',
    cloudRegion: 'eu-west-1',
    timeZone: 'Europe/Dublin',
    currency: 'EUR',
    share: 0.2,
  },
  'ps5-na': {
    title: 'PlayStation 5 North America',
    platform: 'playstation',
    market: 'na',
    cloudRegion: 'us-west-2',
    timeZone: 'America/Los_Angeles',
    currency: 'USD',
    share: 0.2,
  },
  'switch-jp': {
    title: 'Nintendo Switch Japan',
    platform: 'nintendo',
    market: 'jp',
    cloudRegion: 'ap-northeast-1',
    timeZone: 'Asia/Tokyo',
    currency: 'JPY',
    share: 0.25,
  },
  'xbox-eu': {
    title: 'Xbox Europe',
    platform: 'xbox',
    market: 'eu',
    cloudRegion: 'eu-west-3',
    timeZone: 'Europe/Paris',
    currency: 'EUR',
    share: 0.1,
  },
  // The studio books mobile revenue in USD; the stores convert their local price tiers.
  'mobile-apac': {
    title: 'Mobile Asia-Pacific',
    platform: 'mobile',
    market: 'apac',
    cloudRegion: 'ap-southeast-1',
    timeZone: 'Asia/Singapore',
    currency: 'USD',
    share: 0.35,
  },
}

/** The platforms' own names of their retail, certification and development environments. */
const platformEnvironments: Record<Platform, Record<Stage, string>> = {
  steam: { production: 'public', staging: 'staging', development: 'beta' },
  playstation: { production: 'np', staging: 'prod-qa', development: 'sp-int' },
  nintendo: { production: 'lp1', staging: 'lotcheck', development: 'dd1' },
  xbox: { production: 'RETAIL', staging: 'CERT', development: 'XDKS.1' },
  mobile: { production: 'production', staging: 'testflight', development: 'internal' },
}

const domains: Record<Stage, string> = {
  production: 'pixelforge.example',
  staging: 'staging.pixelforge.example',
  development: 'dev.pixelforge.example',
}
const logLevels: Record<Stage, string> = { production: 'warn', staging: 'info', development: 'debug' }
/** The players of a stage relative to production. */
const load: Record<Stage, number> = { production: 1, staging: 0.01, development: 0.001 }

/** Consoles are closed platforms that sync saves themselves: anti-cheat and cloud saves run on PC and mobile only. */
const openPlatformServices: Service[] = ['anti-cheat', 'cloud-saves']
const runsOn = (service: Service, region: PlatformRegion) =>
  !openPlatformServices.includes(service) || region.platform === 'steam' || region.platform === 'mobile'

/** A context of a game: a platform region, or a developer rig on the platform of one. */
interface Place {
  name: string
  title: string
  region: PlatformRegion
}

const placeOf = (name: RegionName): Place => ({
  name,
  title: platformRegions[name].title,
  region: platformRegions[name],
})

/** A run window in days from now. */
interface RunWindow {
  startsIn: number
  expiresIn: number
}

interface EventPlan {
  event: EventName
  /** The platform regions of the game that run it; all of them by default. */
  regions?: RegionName[]
  /** No window: a recurring event that is always on. */
  window?: RunWindow
  /** Platform regions that keep a window of their own. */
  windows?: Partial<Record<RegionName, RunWindow>>
  /** Platform regions where the run is switched off. */
  paused?: RegionName[]
}

interface Game {
  name: string
  title: string
  description: string
  labels: string[]
  values: Record<string, unknown>
  /** Days since the game went live, for the Created dates. */
  since: number
  tier: 'gold' | 'silver'
  peakCcu: number
  /** The budget of the game's plans relative to the biggest game. */
  scale: number
  modes: string[]
  boards: string[]
  services: Service[]
  regions: RegionName[]
  /** Platform regions that are not live yet: days until the launch. */
  launches?: Partial<Record<RegionName, number>>
  /** The regions of the certification environments (staging). */
  staging: RegionName[]
  /** The id of the title in each platform's store. */
  storeIds: Partial<Record<Platform, string>>
  ratings: Record<Market, string>
  currencies: { soft: string; premium: string }
  maintenanceWindow: string
  /** The minimum client versions the backend accepted, oldest first, and the one of the next release. */
  clients: string[]
  nextClient: string
  /** Days between two client patches. */
  patchEvery: number
  events: EventPlan[]
}

// 19 March to 2 April, timed with the Steam Spring Sale; 15 December to 5 January.
const springSale: RunWindow = { startsIn: -216, expiresIn: -202 }
const winterFestival: RunWindow = { startsIn: 55, expiresIn: 76 }

const starDrift: Game = {
  name: 'star-drift',
  title: 'Star Drift',
  description:
    'Free-to-play space shooter: **ranked** squad battles, co-op raids and a new season every ten weeks.\n\nProgression is per platform: Steam saves sync through Cloud saves, consoles keep their saves on the platform.',
  labels: ['shooter', 'free-to-play'],
  values: { franchiseId: 'PF-SD', producer: 'alan@example.com', engine: 'Unreal Engine 5.4', launchedOn: '2024-06-12' },
  since: 860,
  tier: 'gold',
  peakCcu: 420000,
  scale: 1,
  modes: ['ranked', 'casual', 'raid'],
  boards: ['ranked-season', 'raid-clears'],
  services: ['matchmaking', 'store', 'events', 'anti-cheat', 'leaderboards', 'cloud-saves'],
  regions: ['steam-eu', 'steam-na', 'ps5-eu', 'xbox-eu'],
  staging: ['steam-eu', 'ps5-eu'],
  storeIds: { steam: '3184620', playstation: 'PPSA14872_00', xbox: '9P3J8XZQ5K2L' },
  ratings: { eu: 'PEGI 12', na: 'ESRB T', jp: 'CERO B', apac: 'IARC 12+' },
  currencies: { soft: 'stardust', premium: 'crystals' },
  maintenanceWindow: 'tue 06:00-08:00 UTC',
  // A client patch every week since 3.6; the backend refuses older clients.
  clients: Array.from({ length: 20 }, (_, index) => `3.${index + 6}.0`),
  nextClient: '3.26.0',
  patchEvery: 7,
  events: [
    { event: 'winter-festival', window: winterFestival },
    { event: 'double-xp-weekend' },
    { event: 'spring-sale', regions: ['steam-eu', 'steam-na'], window: springSale },
    // Launched on 12 June 2024.
    { event: 'anniversary', window: { startsIn: -131, expiresIn: -124 } },
  ],
}

const dungeonDash: Game = {
  name: 'dungeon-dash',
  title: 'Dungeon Dash',
  description: 'Premium co-op roguelite for up to four players. Without PvP it needs no anti-cheat.',
  labels: ['roguelite', 'premium'],
  values: { franchiseId: 'PF-DD', producer: 'barbara@example.com', engine: 'Unity 6', launchedOn: '2025-10-17' },
  since: 369,
  tier: 'silver',
  peakCcu: 65000,
  scale: 0.3,
  modes: ['co-op', 'daily-run'],
  boards: ['daily-run', 'deepest-floor'],
  services: ['matchmaking', 'store', 'events', 'leaderboards', 'cloud-saves'],
  regions: ['steam-eu', 'switch-jp', 'mobile-apac'],
  staging: ['steam-eu', 'mobile-apac'],
  storeIds: { steam: '2461780', nintendo: '0100F2C0115B6000', mobile: 'com.pixelforge.dungeondash' },
  ratings: { eu: 'PEGI 7', na: 'ESRB E10+', jp: 'CERO A', apac: 'IARC 7+' },
  currencies: { soft: 'gold', premium: 'gems' },
  maintenanceWindow: 'thu 01:00-02:00 UTC',
  clients: ['2.2.0', '2.3.0', '2.3.1'],
  nextClient: '2.4.0',
  patchEvery: 30,
  events: [
    { event: 'double-xp-weekend', paused: ['mobile-apac'] },
    { event: 'spring-sale', regions: ['steam-eu', 'mobile-apac'], window: springSale },
    // The first anniversary is on now: launched on 17 October 2025.
    { event: 'anniversary', window: { startsIn: -4, expiresIn: 3 } },
    { event: 'halloween-hunt', window: { startsIn: 3, expiresIn: 13 } },
  ],
}

const kartBlitz: Game = {
  name: 'kart-blitz',
  title: 'Kart Blitz',
  description: 'Free-to-play kart racer with grand prix, battle and time-trial modes, on consoles and phones.',
  labels: ['racing', 'free-to-play'],
  values: { franchiseId: 'PF-KB', producer: 'ken@example.com', engine: 'Forge Engine 3', launchedOn: '2023-11-04' },
  since: 1082,
  tier: 'gold',
  peakCcu: 310000,
  scale: 0.8,
  modes: ['grand-prix', 'battle', 'time-trial'],
  boards: ['grand-prix', 'time-trial'],
  services: ['matchmaking', 'store', 'events', 'anti-cheat', 'leaderboards', 'cloud-saves'],
  regions: ['switch-jp', 'ps5-eu', 'ps5-na', 'mobile-apac', 'xbox-eu'],
  launches: { 'xbox-eu': 23 },
  staging: ['ps5-eu', 'xbox-eu'],
  storeIds: {
    nintendo: '0100A3D0158E2000',
    playstation: 'PPSA09216_00',
    xbox: '9N7QK4WZT2HB',
    mobile: 'com.pixelforge.kartblitz',
  },
  ratings: { eu: 'PEGI 3', na: 'ESRB E', jp: 'CERO A', apac: 'IARC 3+' },
  currencies: { soft: 'coins', premium: 'turbo-tokens' },
  maintenanceWindow: 'mon 19:00-20:00 UTC',
  clients: ['5.12.0'],
  nextClient: '5.13.0',
  patchEvery: 14,
  events: [
    // Japan keeps the festival through the New Year holidays, to 9 January.
    { event: 'winter-festival', window: winterFestival, windows: { 'switch-jp': { startsIn: 55, expiresIn: 80 } } },
    { event: 'spring-sale', regions: ['mobile-apac'], window: springSale },
    // Launched on 4 November 2023; the Xbox launch comes after the anniversary.
    {
      event: 'anniversary',
      regions: ['switch-jp', 'ps5-eu', 'ps5-na', 'mobile-apac'],
      window: { startsIn: 14, expiresIn: 21 },
    },
  ],
}

const games = [starDrift, dungeonDash, kartBlitz]

interface BackendService {
  title: string
  labels: string[]
  team: string
  description: string
  /** The product defaults, oldest first. */
  versions: Record<string, unknown>[]
  every?: number
  /** The version the release candidate brings. */
  next?: Record<string, unknown>
}

// Skill-based matchmaking, retuned after every season.
const matchmakingTuning = evolve(
  { algorithm: 'glicko-2', maxSkillGap: 400, widenEverySeconds: 20, maxPingMs: 120, partySizeMax: 4, backfill: false },
  5,
  (version) => [{ backfill: true }, { widenEverySeconds: 15 }, { maxPingMs: 100 }, { maxSkillGap: 350 }][version - 2]!,
)

const services: Record<Service, BackendService> = {
  matchmaking: {
    title: 'Matchmaking',
    labels: ['realtime'],
    team: 'Online Services',
    description: 'Queues players by skill and latency and hands each match to a dedicated server of the region.',
    versions: matchmakingTuning,
    every: 21,
    next: { ...matchmakingTuning.at(-1), maxSkillGap: 300, latencyBuckets: [30, 60, 100] },
  },
  store: {
    title: 'Store',
    labels: ['money'],
    team: 'Commerce',
    description: 'The in-game store: catalog, prices, the receipts of the platform stores and refunds.',
    versions: [
      { catalogVersion: 'catalog-2026-08', refundWindowDays: 14, receiptValidation: 'server', fraudScreening: 'basic' },
      {
        catalogVersion: 'catalog-2026-10',
        refundWindowDays: 14,
        receiptValidation: 'server',
        fraudScreening: 'adaptive',
      },
    ],
    every: 45,
    next: {
      catalogVersion: 'catalog-2026-12',
      refundWindowDays: 14,
      receiptValidation: 'server',
      fraudScreening: 'adaptive',
    },
  },
  events: {
    title: 'Events',
    labels: ['content'],
    team: 'Live Ops',
    description: 'Schedules the live events, announces them in the game news and delivers their rewards.',
    versions: [
      { announceHoursBefore: 48, maxConcurrentEvents: 2, calendar: 'https://calendar.pixelforge.example/live-ops.ics' },
    ],
  },
  'anti-cheat': {
    title: 'Anti-cheat',
    labels: ['security'],
    team: 'Trust & Safety',
    description:
      'Cheat detection on the open platforms:\n\n- **PC**: Easy Anti-Cheat\n- **Mobile**: the app attestation of the stores\n\nConsoles are closed platforms and need none.',
    versions: evolve(
      { vendor: 'easy-anti-cheat', reportThreshold: 5, banWaveCron: '0 4 * * 3', action: 'kick-and-report' },
      3,
      (version) => (version === 2 ? { reportThreshold: 3 } : { banWaveCron: '0 4 * * 2' }),
    ),
    every: 60,
  },
  leaderboards: {
    title: 'Leaderboards',
    labels: ['social'],
    team: 'Online Services',
    description: 'Weekly, seasonal and all-time rankings per mode.',
    versions: [{ seasonLengthDays: 70, topN: 1000, weeklyResetCron: '0 0 * * 1', tieBreak: 'earliest' }],
  },
  'cloud-saves': {
    title: 'Cloud saves',
    labels: ['storage'],
    team: 'Platform Engineering',
    description: 'Syncs progress across devices on PC and mobile; the consoles sync saves through their own platform.',
    versions: [
      { quotaMb: 32, conflictPolicy: 'newest-wins', retentionDays: 365 },
      { quotaMb: 64, conflictPolicy: 'newest-wins', retentionDays: 365 },
    ],
    every: 90,
  },
}
const allServices = Object.keys(services) as Service[]

interface LiveEvent {
  title: string
  labels: string[]
  description: string
  unitType: 'time' | 'event'
  definition: string
  /** Rewards and length, oldest first; the type schema requires the rewards. */
  versions: Record<string, unknown>[]
  next?: Record<string, unknown>
}

const winterRewards = [
  { item: 'snowflake-banner', quantity: 1 },
  { item: 'frost-trail', quantity: 1 },
]

const liveEvents: Record<EventName, LiveEvent> = {
  'winter-festival': {
    title: 'Winter Festival',
    labels: ['seasonal'],
    description: 'Three weeks of snow maps and festive cosmetics, from 15 December to 5 January.',
    unitType: 'time',
    definition: '0 10 15 12 *',
    versions: [
      { rewards: winterRewards.slice(0, 1), durationHours: 504, theme: 'snow' },
      { rewards: winterRewards, durationHours: 504, theme: 'snow' },
      { rewards: [...winterRewards, { item: 'premium-currency', quantity: 300 }], durationHours: 504, theme: 'snow' },
    ],
    // The final reward list of the next release.
    next: {
      rewards: [...winterRewards, { item: 'premium-currency', quantity: 300 }, { item: 'aurora-skin', quantity: 1 }],
      durationHours: 504,
      theme: 'aurora',
    },
  },
  'double-xp-weekend': {
    title: 'Double XP weekend',
    labels: ['recurring'],
    description: 'Every weekend from Friday 16:00 UTC: twice the experience for every match.',
    unitType: 'time',
    definition: '0 16 * * 5',
    versions: [{ rewards: [{ item: 'xp-boost', quantity: 1 }], durationHours: 56, xpMultiplier: 2 }],
  },
  'spring-sale': {
    title: 'Spring Sale',
    labels: ['sale'],
    description: 'Two weeks of discounts in the PC and mobile stores, timed with the Steam Spring Sale.',
    unitType: 'time',
    definition: '0 17 19 3 *',
    versions: [{ rewards: [{ item: 'spring-sale-badge', quantity: 1 }], durationHours: 336, discountPercent: 30 }],
  },
  anniversary: {
    title: 'Anniversary',
    labels: ['seasonal'],
    description: 'A week of celebration from the launch day of each game; the event fires on the game’s own date.',
    unitType: 'event',
    definition: 'title.launch-anniversary',
    versions: [
      { rewards: [{ item: 'anniversary-banner', quantity: 1 }], durationHours: 168 },
      {
        rewards: [
          { item: 'anniversary-banner', quantity: 1 },
          { item: 'loot-crate', quantity: 3 },
        ],
        durationHours: 168,
      },
    ],
  },
  'halloween-hunt': {
    title: 'Halloween Hunt',
    labels: ['seasonal'],
    description: 'Cancelled: the haunted crypt missed the certification deadline of the platforms.',
    unitType: 'time',
    definition: '0 12 24 10 *',
    versions: [{ rewards: [{ item: 'pumpkin-helmet', quantity: 1 }], durationHours: 240 }],
  },
}

const rewardList = {
  type: 'array',
  minItems: 1,
  items: {
    type: 'object',
    properties: {
      item: { type: 'string', pattern: '^[a-z0-9-]+$' },
      quantity: { type: 'integer', minimum: 1, maximum: 10000 },
    },
    required: ['item', 'quantity'],
  },
}

const liveEventProperties = { rewards: rewardList, durationHours: { type: 'integer', minimum: 1, maximum: 720 } }

/** A live event cannot be saved without its rewards; the release candidate also requires its length. */
const liveEventSchema = (next: boolean) => [
  { type: 'object', properties: { rewards: { type: 'array', minItems: 1 } }, required: ['rewards'] },
  { type: 'object', properties: liveEventProperties, required: ['rewards'] },
  ...(next ? [{ type: 'object', properties: liveEventProperties, required: ['rewards', 'durationHours'] }] : []),
]

function addServices(b: ScopeBuilder, names: readonly Service[], next = false) {
  for (const name of names) {
    const service = services[name]
    b.responsibility(name, {
      title: service.title,
      description: service.description,
      labels: service.labels,
      link: `${docs}/services/${name}`,
      values: { ownerTeam: service.team, onCall: `pf-${name}` },
      createdDaysAgo: 1100,
      createdBy: 'alan',
    })
    b.configure(`rst.${name}`, next && service.next ? [...service.versions, service.next] : service.versions, {
      every: service.every,
    })
  }
  if (names.includes('store')) {
    // A store deployment cannot be saved without the currency of its platform region.
    b.descendantSchema('rst.store', 'exe', {
      type: 'object',
      properties: { currency: { type: 'string', pattern: '^[A-Z]{3}$' }, taxIncluded: { type: 'boolean' } },
      required: ['currency'],
    })
  }
}

function addLiveEvents(b: ScopeBuilder, names: readonly EventName[], next = false) {
  for (const name of names) {
    const event = liveEvents[name]
    b.unit('events', name, {
      unitType: event.unitType,
      definition: event.definition,
      // The CRON definitions of the live events are written in UTC, not in the studio's time zone.
      ...(event.unitType === 'time' ? { timeZone: 'UTC' } : {}),
      title: event.title,
      description: event.description,
      labels: event.labels,
      link: `${docs}/events/${name}`,
      createdDaysAgo: name === 'halloween-hunt' ? 60 : 500,
      createdBy: 'ken',
    })
    b.configure(`unt.events.${name}`, next && event.next ? [...event.versions, event.next] : event.versions, {
      every: 12,
    })
  }
  b.typeSchema('unt', liveEventSchema(next), { every: 120 })
  // Every event run inherits how and for how long its rewards can be claimed.
  b.template(
    'uxe',
    evolve(
      { rewardDelivery: 'inbox', claimWindowDays: 14, maxClaimsPerAccount: 1, announceHoursBefore: 48 },
      3,
      (version) => (version === 2 ? { pushNotification: true } : { claimWindowDays: 21 }),
    ),
    { every: 40 },
  )
}

function addGame(b: ScopeBuilder, game: Game, versions: Record<string, unknown>[], lastDaysAgo = 2) {
  b.subject(game.name, {
    title: game.title,
    description: game.description,
    labels: game.labels,
    values: game.values,
    link: `${docs}/games/${game.name}`,
    createdDaysAgo: game.since + 120,
    createdBy: 'alan',
  })
  b.configure(`sbt.${game.name}`, versions, { every: game.patchEvery, lastDaysAgo })
}

/** The game's settings after each client patch: the oldest client the backend still accepts. */
const releasedClients = (game: Game, next: boolean) =>
  (next ? [...game.clients, game.nextClient] : game.clients).map((minClientVersion) => ({
    minClientVersion,
    maintenanceWindow: game.maintenanceWindow,
    currencies: game.currencies,
  }))

function platformRegion(b: ScopeBuilder, game: Game, place: Place, stage: Stage, meta: Meta = {}) {
  const { region } = place
  const key = b.context(game.name, place.name, {
    title: `${game.title} on ${place.title}`,
    labels: [region.platform, region.market],
    timeZone: region.timeZone,
    createdDaysAgo: game.since,
    ...meta,
    // The platform's id of the title, read by the store deployments, and the age rating of the market.
    values: { storeAppId: game.storeIds[region.platform], rating: game.ratings[region.market], ...meta.values },
  })
  b.configure(key, {
    cloudRegion: region.cloudRegion,
    gameServers: `gs.${place.name}.${game.name}.${domains[stage]}`,
    platformEnvironment: platformEnvironments[region.platform][stage],
    logLevel: logLevels[stage],
    ...(stage === 'development' ? { debugOverlay: true, botsFillEmptySlots: true } : {}),
  })
}

const tiers = {
  gold: { sla: 99.95, support: '24/7' },
  silver: { sla: 99.9, support: 'business-hours' },
}
/** The monthly budget of a gold plan of the biggest game, in EUR. */
const budgets: Record<Service, number> = {
  matchmaking: 24000,
  store: 6000,
  events: 2500,
  'anti-cheat': 9000,
  leaderboards: 3000,
  'cloud-saves': 4500,
}

function servicePlan(b: ScopeBuilder, game: Game, service: Service, stage: Stage) {
  const key = b.usage(game.name, service, {
    title: `${game.title} ${services[service].title.toLowerCase()} plan`,
    createdDaysAgo: game.since,
  })
  const extras = {
    ...(service === 'matchmaking' ? { modes: game.modes } : {}),
    ...(service === 'leaderboards' ? { boards: game.boards } : {}),
  }
  b.configure(
    key,
    stage === 'production'
      ? {
          tier: game.tier,
          ...tiers[game.tier],
          peakCcu: game.peakCcu,
          monthlyBudgetEur: Math.round((budgets[service] * game.scale) / 100) * 100,
          ...extras,
        }
      : {
          tier: 'test',
          sla: 99,
          support: 'business-hours',
          peakCcu: Math.round(game.peakCcu * load[stage]),
          ...extras,
        },
  )
}

/** Queue sizes, region servers, store currency and keys: what a service needs in one platform region. */
function deploymentContent(game: Game, service: Service, place: Place, stage: Stage) {
  const { region } = place
  const players = game.peakCcu * region.share * load[stage]
  switch (service) {
    case 'matchmaking':
      return {
        queues: Object.fromEntries(
          game.modes.map((mode) => [
            mode,
            {
              maxSize: Math.max(20, Math.round(players / game.modes.length / 200) * 10),
              timeoutSeconds: mode === 'ranked' ? 120 : 60,
            },
          ]),
        ),
        dedicatedServers: {
          fleet: '@config("/gameServers")',
          region: '@config("/cloudRegion")',
          minWarm: Math.max(2, Math.round(players / 3000)),
        },
      }
    case 'store':
      return {
        currency: region.currency,
        taxIncluded: region.market !== 'na',
        storeProductId: '@annotation("/storeAppId", "Context")',
        serverKey: `@(store.${game.name}-${region.platform}, primaryVault)`,
        ...(region.platform === 'mobile' ? { localPricing: 'store-tiers' } : {}),
        ...(stage === 'production'
          ? {}
          : { sandbox: true, paymentEndpoint: `https://payments.${domains[stage]}/sandbox` }),
      }
    case 'anti-cheat':
      return region.platform === 'mobile'
        ? {
            vendor: 'app-attestation',
            attestation: 'required',
            apiSecret: `@(anti-cheat.${game.name}-mobile, primaryVault)`,
          }
        : {
            clientId: `eac-${game.name}-${place.name}`,
            apiSecret: `@(anti-cheat.${game.name}-${place.name}, primaryVault)`,
            ...(stage === 'production' ? {} : { action: 'report-only' }),
          }
    case 'cloud-saves':
      return {
        bucket: `https://saves.${region.cloudRegion}.${domains[stage]}/${game.name}`,
        quotaMb: game.tier === 'gold' ? 64 : 32,
      }
    // Events and leaderboards keep the defaults of their service and plan.
    case 'events':
    case 'leaderboards':
      return undefined
  }
}

function deploy(b: ScopeBuilder, game: Game, service: Service, place: Place, stage: Stage, meta: Meta = {}) {
  const key = b.execution(game.name, service, place.name, {
    title: `${game.title} ${services[service].title.toLowerCase()} on ${place.title}`,
    timeZone: place.region.timeZone,
    createdDaysAgo: game.since,
    ...meta,
  })
  const content = deploymentContent(game, service, place, stage)
  if (content) {
    // Keys and secrets are written by the member who may read the vault.
    const secret = service === 'store' || service === 'anti-cheat'
    b.configure(key, content, secret ? { authors: ['ken'] } : undefined)
  }
}

/** What an event run changes in its platform region. */
function runContent(game: Game, event: EventName, place: Place, stage: Stage): Record<string, unknown> | undefined {
  if (stage === 'development') {
    // On a dev rig an event fires every half hour and lasts ten minutes, so a day covers many runs.
    return { cron: '*/30 * * * *', durationMinutes: 10 }
  }
  switch (event) {
    case 'double-xp-weekend':
      // From Friday evening in the region's own time zone instead of 16:00 UTC.
      return { cron: '0 18 * * 5', timeZone: place.region.timeZone }
    case 'spring-sale':
      return {
        discountPercent: place.region.platform === 'mobile' ? 50 : 40,
        featuredBundle: `${game.name}-starter-pack`,
      }
    case 'winter-festival':
      // Japan keeps the festival through the New Year holidays, to 9 January, with a New Year reward.
      return place.region.market === 'jp'
        ? { durationHours: 600, bonusRewards: [{ item: 'kadomatsu-decal', quantity: 1 }] }
        : undefined
    case 'halloween-hunt':
      return { dungeon: 'haunted-crypt' }
    case 'anniversary':
      return undefined
  }
}

function eventRun(b: ScopeBuilder, game: Game, event: EventName, place: Place, stage: Stage, meta: Meta = {}) {
  const definition = liveEvents[event]
  const key = b.unitOfExecution(game.name, 'events', place.name, event, {
    title: `${definition.title}: ${game.title} on ${place.title}`,
    labels: definition.labels,
    timeZone: place.region.timeZone,
    createdDaysAgo: 300,
    ...meta,
  })
  const content = runContent(game, event, place, stage)
  if (content) {
    b.configure(key, content)
  }
}

/** A run window: created by the live-ops producer a few weeks before it starts. */
const windowed = (window?: RunWindow): Meta =>
  window ? { ...window, createdDaysAgo: Math.max(10, 30 - window.startsIn), createdBy: 'ken' } : {}

function production(b: ScopeBuilder, next: boolean) {
  addServices(b, allServices, next)
  // The cancelled Halloween Hunt is not part of the next release.
  const events = Object.keys(liveEvents) as EventName[]
  addLiveEvents(
    b,
    events.filter((event) => !next || event !== 'halloween-hunt'),
    next,
  )

  for (const game of games) {
    addGame(b, game, releasedClients(game, next), next ? 1 : 2)
    // A platform region that is not live yet is scheduled, and so are its deployments.
    const launch = (name: RegionName): Meta => {
      const days = game.launches?.[name]
      return days ? { startsIn: days, createdDaysAgo: 30, createdBy: 'ken' } : {}
    }
    for (const name of game.regions) {
      const launching = game.launches?.[name] !== undefined
      platformRegion(b, game, placeOf(name), 'production', {
        ...launch(name),
        ...(launching ? { description: `${game.title} launches on ${platformRegions[name].title}.` } : {}),
      })
    }
    for (const service of game.services) {
      servicePlan(b, game, service, 'production')
      for (const name of game.regions.filter((region) => runsOn(service, platformRegions[region]))) {
        deploy(b, game, service, placeOf(name), 'production', launch(name))
      }
    }
    for (const plan of game.events) {
      if (next && plan.event === 'halloween-hunt') {
        continue
      }
      for (const name of plan.regions ?? game.regions) {
        // Client 2.4 of the next release fixes the exploit, so the release candidate runs the event again.
        const paused: Meta =
          plan.paused?.includes(name) && !next
            ? {
                disabled: true,
                description: 'Paused: XP boosts stacked on mobile. Back with client 2.4.',
                modifiedDaysAgo: 6,
                modifiedBy: 'barbara',
              }
            : {}
        eventRun(b, game, plan.event, placeOf(name), 'production', {
          ...windowed(plan.windows?.[name] ?? plan.window),
          ...paused,
        })
      }
    }
  }

  if (next) {
    battlePass(b)
    return
  }

  // Dungeon Dash's saves on Switch moved to Nintendo's own save cloud; the deployment was removed after the move.
  deploy(b, dungeonDash, 'cloud-saves', placeOf('switch-jp'), 'production', {
    description: 'Retired: Switch saves sync through the platform.',
  })
  b.trash('unt.events.halloween-hunt', { daysAgo: 12, by: 'alan' })
  b.trash('exe.dungeon-dash.cloud-saves.switch-jp', { daysAgo: 9, by: 'ken' })
}

/** Season 12 brings a battle pass to Star Drift: the new service of the release candidate. */
function battlePass(b: ScopeBuilder) {
  b.responsibility('battle-pass', {
    title: 'Battle pass',
    description: 'A seasonal reward track with a free and a premium lane.',
    labels: ['money'],
    link: `${docs}/services/battle-pass`,
    values: { ownerTeam: 'Commerce', onCall: 'pf-battle-pass' },
    createdDaysAgo: 20,
    createdBy: 'ada',
  })
  b.configure('rst.battle-pass', {
    tiers: 100,
    xpPerTier: 1000,
    seasonLengthDays: 70,
    premiumLane: { currency: starDrift.currencies.premium, price: 950 },
  })
  b.usage('star-drift', 'battle-pass', { title: 'Star Drift battle pass plan', createdDaysAgo: 20, createdBy: 'ada' })
  b.configure('usg.star-drift.battle-pass', {
    tier: 'gold',
    ...tiers.gold,
    peakCcu: starDrift.peakCcu,
    monthlyBudgetEur: 5000,
  })
  for (const name of starDrift.regions) {
    const place = placeOf(name)
    const key = b.execution('star-drift', 'battle-pass', name, {
      title: `Star Drift battle pass on ${place.title}`,
      timeZone: place.region.timeZone,
      createdDaysAgo: 20,
      createdBy: 'ada',
    })
    b.configure(key, { season: 'season-12', rewardTrack: 'sd-s12-track' })
  }
}

type RigName = 'steam-beta' | 'ps5-devkit' | 'switch-devkit' | 'qa-lab' | 'phone-farm'

/** Developer rigs in the Helsinki studio, each on the platform and cloud region of one platform region. */
const rigs: Record<RigName, { title: string; base: RegionName; owner: string; devkit?: string }> = {
  'steam-beta': { title: 'Steam beta branch', base: 'steam-eu', owner: 'ken@example.com' },
  'ps5-devkit': { title: 'PS5 dev kit', base: 'ps5-eu', owner: 'barbara@example.com', devkit: 'DFI-T1000AA' },
  'switch-devkit': { title: 'Switch dev kit', base: 'switch-jp', owner: 'ken@example.com', devkit: 'SDEV-HAC' },
  'qa-lab': { title: 'QA lab', base: 'steam-eu', owner: 'qa@pixelforge.example' },
  'phone-farm': { title: 'QA phone farm', base: 'mobile-apac', owner: 'qa@pixelforge.example' },
}

const rigOf = (name: RigName): Place => ({
  name,
  title: rigs[name].title,
  region: { ...platformRegions[rigs[name].base], timeZone: 'Europe/Helsinki' },
})

interface DevelopmentSetup {
  game: Game
  rigs: RigName[]
  /** The events tested on each rig. */
  runs: Partial<Record<EventName, RigName[]>>
}

const developmentSetups: DevelopmentSetup[] = [
  {
    game: starDrift,
    rigs: ['steam-beta', 'ps5-devkit', 'qa-lab'],
    runs: { 'double-xp-weekend': ['steam-beta', 'qa-lab'], 'winter-festival': ['qa-lab'] },
  },
  { game: kartBlitz, rigs: ['switch-devkit', 'phone-farm'], runs: { 'winter-festival': ['phone-farm'] } },
]

/** Development: Star Drift and Kart Blitz on dev kits, the Steam beta branch and the QA rigs. */
function development(b: ScopeBuilder) {
  const devServices: Service[] = ['matchmaking', 'store', 'events']
  addServices(b, devServices)
  addLiveEvents(b, ['winter-festival', 'double-xp-weekend'])
  for (const setup of developmentSetups) {
    const { game } = setup
    addGame(b, game, [{ ...releasedClients(game, true).at(-1), allowUnsignedBuilds: true }], 1)
    for (const name of setup.rigs) {
      const rig = rigs[name]
      platformRegion(b, game, rigOf(name), 'development', {
        createdDaysAgo: 90,
        createdBy: 'ken',
        values: {
          rating: 'RP',
          owner: rig.owner,
          ...(rig.devkit ? { devkitSerial: `${rig.devkit}-${b.rng.int(100000, 999999)}` } : {}),
        },
      })
    }
    for (const service of devServices) {
      servicePlan(b, game, service, 'development')
      for (const name of setup.rigs) {
        deploy(b, game, service, rigOf(name), 'development', { createdDaysAgo: 90, createdBy: 'ken' })
      }
    }
    for (const [event, names] of Object.entries(setup.runs) as [EventName, RigName[]][]) {
      for (const name of names) {
        eventRun(b, game, event, rigOf(name), 'development', { createdDaysAgo: 14, createdBy: 'ken' })
      }
    }
  }
}

/** Staging: every game on the certification environments of two platform regions, rehearsing the coming events. */
function staging(b: ScopeBuilder) {
  const stagingServices: Service[] = ['matchmaking', 'store', 'events', 'anti-cheat']
  addServices(b, stagingServices)
  const rehearsed: EventName[] = ['winter-festival', 'anniversary']
  addLiveEvents(b, rehearsed)
  for (const game of games) {
    addGame(b, game, releasedClients(game, true).slice(-1), 1)
    const places = game.staging.map(placeOf)
    for (const place of places) {
      platformRegion(b, game, place, 'staging', {
        createdDaysAgo: 200,
        values: { testAccounts: Array.from({ length: 3 }, () => `pfqa-${b.rng.int(10000, 99999)}`) },
      })
    }
    for (const service of stagingServices.filter((candidate) => game.services.includes(candidate))) {
      servicePlan(b, game, service, 'staging')
      for (const place of places.filter((candidate) => runsOn(service, candidate.region))) {
        deploy(b, game, service, place, 'staging')
      }
    }
    // A coming event is rehearsed for a week on the certification environments of the regions that will run it.
    for (const plan of game.events.filter((candidate) => rehearsed.includes(candidate.event))) {
      if ((plan.window?.startsIn ?? 0) > 0) {
        const regions = plan.regions ?? game.regions
        for (const place of places.filter((candidate) => regions.includes(candidate.name as RegionName))) {
          eventRun(b, game, plan.event, place, 'staging', {
            startsIn: -2,
            expiresIn: 5,
            createdDaysAgo: 3,
            createdBy: 'ken',
          })
        }
      }
    }
  }
}

const crossplayPools: Record<Platform, 'console' | 'pc' | 'mobile'> = {
  steam: 'pc',
  playstation: 'console',
  xbox: 'console',
  nintendo: 'console',
  mobile: 'mobile',
}

/** Crossplay: one matchmaking pool across the consoles, and PC and mobile pools of their own. */
function featureDelivery(b: ScopeBuilder) {
  addServices(b, ['matchmaking'])
  b.responsibility('crossplay', {
    title: 'Crossplay',
    description:
      'Matches players across platforms. Consoles share one pool; PC and mobile join it only when the player opts in.',
    labels: ['realtime', 'feature'],
    link: `${docs}/services/crossplay`,
    values: { ownerTeam: 'Online Services', onCall: 'pf-crossplay' },
    createdDaysAgo: 25,
    createdBy: 'alan',
  })
  b.configure('rst.crossplay', [
    { pools: ['console', 'pc', 'mobile'], inputBasedMatching: true },
    { pools: ['console', 'pc', 'mobile'], inputBasedMatching: true, crossPoolOptIn: true, accountLinking: 'pf-id' },
  ])
  // Every crossplay deployment names its pool.
  b.descendantSchema('rst.crossplay', 'exe', {
    type: 'object',
    properties: { pool: { enum: ['console', 'pc', 'mobile'] }, voiceChat: { enum: ['off', 'party-only', 'all'] } },
    required: ['pool'],
  })
  for (const [game, names] of [
    [kartBlitz, ['switch-jp', 'ps5-eu', 'mobile-apac']],
    [starDrift, ['steam-eu', 'ps5-eu']],
  ] as const) {
    addGame(b, game, releasedClients(game, true).slice(-1), 1)
    servicePlan(b, game, 'matchmaking', 'staging')
    b.usage(game.name, 'crossplay', { title: `${game.title} crossplay plan`, createdDaysAgo: 25, createdBy: 'alan' })
    b.configure(`usg.${game.name}.crossplay`, {
      tier: 'test',
      sla: 99,
      peakCcu: Math.round(game.peakCcu * load.staging),
    })
    for (const name of names) {
      const place = placeOf(name)
      platformRegion(b, game, place, 'staging', { createdDaysAgo: 25 })
      deploy(b, game, 'matchmaking', place, 'staging', { createdDaysAgo: 25 })
      const key = b.execution(game.name, 'crossplay', name, {
        title: `${game.title} crossplay on ${place.title}`,
        timeZone: place.region.timeZone,
        createdDaysAgo: 25,
        createdBy: 'alan',
      })
      // Switch players talk through the platform's own app, so the game has no voice chat there.
      const pool = crossplayPools[place.region.platform]
      b.configure(key, { pool, voiceChat: place.region.platform === 'nintendo' ? 'off' : 'party-only' })
    }
  }
}

export const liveOps: ShowcaseProject = {
  org: 'pixel-forge',
  project: 'live-ops',
  description:
    'Live operations of game titles. Backend services run per platform and region, and seasonal live events are scheduled per region.',
  members: { alan: 'Owner', ada: 'Administrator', ken: 'ContributorWithSecrets', barbara: 'Contributor' },
  settings: {
    types: {
      rst: { label: 'Backend service', color: 'indigo', icon: 'i-lucide-server' },
      unt: { label: 'Live event', color: 'fuchsia', icon: 'i-lucide-calendar' },
      sbt: { label: 'Game', color: 'blue', icon: 'i-lucide-gem' },
      usg: { label: 'Service plan', color: 'amber', icon: 'i-lucide-tag' },
      cnt: { label: 'Platform region', color: 'teal', icon: 'i-lucide-globe' },
      exe: { label: 'Service deployment', color: 'emerald', icon: 'i-lucide-container' },
      uxe: { label: 'Event run', color: 'orange', icon: 'i-lucide-play' },
    },
    defaultTimeZone: 'Europe/Helsinki',
    dateFormat: 'yyyy-mm-dd',
  },
  views: environmentViews({
    development: 'Development: dev kits, the Steam beta branch and the QA lab',
    staging: 'Platform certification environments and event rehearsals',
    'release-candidate': 'Season 12: new clients, the battle pass and the final Winter Festival',
    'feature-delivery1': 'Crossplay for Kart Blitz and Star Drift',
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
