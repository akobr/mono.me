import type { Lifecycle, ScopeBuilder, UnitMeta } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// Example 11 of the Miro board: Gatherly runs conference series for their organizers. A series is a subject and each
// of its editions is a context, so an edition's lifecycle follows the calendar: past editions are expired, announced
// ones are scheduled until their ticket sales open. The organizer books event services for the series (usages), the
// services are set up per edition (executions), and the mail jobs of a service run per edition (units of execution).

const docs = 'https://docs.gatherly.example'

const day = 86_400_000
/** The day the calendar of the dataset is written for: the builder's "now" stands for it. */
const asOf = Date.parse('2026-10-21')
const daysUntil = (date: string) => Math.round((Date.parse(date) - asOf) / day)
/** An edition stays live this long after its last day, until its invoices and recordings are settled. */
const settleDays = 25

type Env = 'production' | 'staging' | 'development'

/** The prefix of hosts, sites and vault entries outside production. */
const envPrefix: Record<Env, string> = { production: '', staging: 'staging.', development: 'dev.' }

const editors: PersonaId[] = ['ada', 'donald', 'frances']

interface Service {
  name: string
  title: string
  labels: string[]
  description: string
  vendor: string
  owner: string
  /** Days since Gatherly offers the service; its jobs are as old. */
  since: number
}

/** The event services Gatherly offers; the last two come with the next release and with feature-delivery1. */
const services: Service[] = [
  {
    name: 'ticketing',
    title: 'Ticketing',
    labels: ['core', 'revenue'],
    description:
      'Sells the tickets of an edition: **tiers**, prices, VAT and refunds.\n\nCard payments settle to the payout account of the series’ booking.',
    vendor: 'pretix',
    owner: 'Frances Allen',
    since: 1100,
  },
  {
    name: 'streaming',
    title: 'Live streaming',
    labels: ['media'],
    description: 'Streams the rooms of an edition over SRT and publishes the recordings a day later.',
    vendor: 'Mux',
    owner: 'Donald Knuth',
    since: 1100,
  },
  {
    name: 'badges',
    title: 'Badges',
    labels: ['core', 'on-site'],
    description: 'Prints the attendee badges at the registration desk; the QR code carries the ticket id.',
    vendor: 'Zebra',
    owner: 'Frances Allen',
    since: 1000,
  },
  {
    name: 'call-for-papers',
    title: 'Call for papers',
    labels: ['content'],
    description: 'Collects the talk submissions and their reviews until the **deadline** of the edition.',
    vendor: 'Sessionize',
    owner: 'Donald Knuth',
    since: 950,
  },
  {
    name: 'sponsorship',
    title: 'Sponsorship',
    labels: ['revenue'],
    description: 'Sponsor packages, the booth plan and the leads scanned from the badges at each booth.',
    vendor: 'Gatherly',
    owner: 'Ada Lovelace',
    since: 950,
  },
  {
    name: 'venue-wifi',
    title: 'Venue Wi-Fi',
    labels: ['on-site'],
    description: 'The guest network of an in-person edition.',
    vendor: 'Cisco Meraki',
    owner: 'Donald Knuth',
    since: 700,
  },
  {
    name: 'event-app',
    title: 'Event app',
    labels: ['mobile'],
    description: 'The agenda, the floor plan and push notifications in the attendees’ pockets.',
    vendor: 'Gatherly',
    owner: 'Ada Lovelace',
    since: 40,
  },
  {
    name: 'matchmaking',
    title: 'Matchmaking',
    labels: ['feature'],
    description: 'Introduces attendees with overlapping interests and suggests where to meet.',
    vendor: 'Gatherly',
    owner: 'Ada Lovelace',
    since: 30,
  },
]

/** The services of the production catalog. */
const currentServices = ['ticketing', 'streaming', 'badges', 'call-for-papers', 'sponsorship', 'venue-wifi']

interface Job {
  service: string
  name: string
  title: string
  unitType: UnitMeta['unitType']
  definition: string
  description: string
  /** Whether an edition that has the service runs the job too. */
  runs: (edition: Edition) => boolean
}

const jobs: Job[] = [
  {
    service: 'ticketing',
    name: 'reminder-mail',
    title: 'Ticket reminder mail',
    unitType: 'time',
    definition: '0 9 * * *',
    description: 'Reminds the ticket holders of the dates, the venue and the badge pickup.',
    runs: () => true,
  },
  {
    service: 'call-for-papers',
    name: 'review-reminder',
    title: 'Review reminder',
    unitType: 'time',
    definition: '0 8 * * 1',
    description: 'Nudges the programme committee about talks still waiting for a review.',
    runs: () => true,
  },
  {
    service: 'sponsorship',
    name: 'lead-digest',
    title: 'Sponsor lead digest',
    unitType: 'message',
    definition: 'sponsor-leads',
    description: 'Mails each sponsor the badges scanned at its booth, as the scans arrive on the queue.',
    // The leads are badge scans, so only editions with badges have them.
    runs: (edition) => edition.services.includes('badges'),
  },
  {
    service: 'matchmaking',
    name: 'intro-mail',
    title: 'Intro mail',
    unitType: 'time',
    definition: '30 7 * * *',
    description: 'Introduces matched attendees every morning of the edition.',
    runs: () => true,
  },
]

interface Series {
  name: string
  title: string
  description: string
  domain: string
  timeZone: string
  labels: string[]
  values: { crmId: string; organizer: string; organizerEmail: string }
  /** The services the organizer books; the ones not in the view's catalog are left out. */
  services: string[]
  package: 'full-service' | 'self-service'
  tracks: string[]
  /** Days since Gatherly runs the series. */
  since: number
}

const seriesList: Series[] = [
  {
    name: 'devconf',
    title: 'DevConf',
    description:
      'The developer conference on cloud, languages and tooling: every November in Prague, with an **online edition** since 2026.',
    domain: 'devconf.example',
    timeZone: 'Europe/Prague',
    labels: ['tech', 'flagship'],
    values: { crmId: 'GTH-S-0001', organizer: 'Jana Dvořáková', organizerEmail: 'team@devconf.example' },
    services: [...currentServices, 'event-app', 'matchmaking'],
    package: 'full-service',
    tracks: ['cloud', 'languages', 'tooling', 'security'],
    since: 900,
  },
  {
    name: 'data-summit',
    title: 'Data Summit',
    description: 'Two days of data engineering, analytics and ML operations, alternating between Amsterdam and London.',
    domain: 'datasummit.example',
    timeZone: 'Europe/Amsterdam',
    labels: ['tech'],
    values: { crmId: 'GTH-S-0002', organizer: 'Pieter de Vries', organizerEmail: 'hello@datasummit.example' },
    services: ['ticketing', 'streaming', 'badges', 'call-for-papers', 'sponsorship'],
    package: 'full-service',
    tracks: ['data-engineering', 'analytics', 'ml-ops'],
    since: 800,
  },
  {
    name: 'design-week',
    title: 'Design Week',
    description:
      'A week of exhibitions and talks on product and furniture design; the organizer curates the programme.',
    domain: 'designweek.example',
    timeZone: 'Europe/Copenhagen',
    labels: ['design'],
    values: { crmId: 'GTH-S-0003', organizer: 'Sofie Lund', organizerEmail: 'studio@designweek.example' },
    services: ['ticketing', 'streaming', 'badges', 'sponsorship', 'venue-wifi'],
    package: 'self-service',
    tracks: [],
    since: 600,
  },
]

type Currency = 'CZK' | 'DKK' | 'EUR' | 'GBP'

/** The steps prices are rounded to: crowns to 50 and 25, euros and pounds to 5. */
const priceStep: Record<Currency, number> = { CZK: 50, DKK: 25, EUR: 5, GBP: 5 }

interface Edition {
  series: string
  name: string
  title: string
  /** None for an online edition. */
  venue?: { name: string; city: string; country: string; contact: string }
  timeZone: string
  /** The cloud region of the stream ingest. */
  region: string
  start: string
  end: string
  capacity: number
  /** Earlier capacities, oldest first, when the venue grew. */
  grewFrom?: number[]
  currency: Currency
  vatRate: number
  /** The regular ticket price. */
  price: number
  /** The services of the series set up for this edition. */
  services: string[]
  cfp?: { opens: string; deadline: string; programmeFinal?: boolean }
  rooms: string[]
  eventCode: string
  /** When an announced edition goes live: its ticket sales open. */
  salesOpen?: string
  createdDaysAgo: number
  /** In person only: the streaming booked for the series is off for this edition. */
  inPersonOnly?: boolean
  /** Cancelled and deleted (default view only). */
  cancelled?: boolean
  /** A test edition of development: live as long as the environment. */
  sandbox?: boolean
  values?: Record<string, unknown>
}

const praguecc = {
  name: 'Prague Congress Centre',
  city: 'Prague',
  country: 'CZ',
  contact: 'events@praguecc.example',
}
const rai = { name: 'RAI Amsterdam', city: 'Amsterdam', country: 'NL', contact: 'events@rai.example' }
const superstudio = { name: 'Superstudio Più', city: 'Milan', country: 'IT', contact: 'booking@superstudio.example' }

const editions: Edition[] = [
  {
    series: 'devconf',
    name: 'prague-2025',
    title: 'DevConf Prague 2025',
    venue: praguecc,
    timeZone: 'Europe/Prague',
    region: 'eu-central-1',
    start: '2025-11-19',
    end: '2025-11-21',
    capacity: 1600,
    currency: 'CZK',
    vatRate: 21,
    price: 9900,
    services: [...currentServices],
    cfp: { opens: '2025-03-03', deadline: '2025-06-30T23:59:00+02:00', programmeFinal: true },
    rooms: ['congress-hall', 'forum-hall', 'meeting-hall-a'],
    eventCode: 'DC25-PRG',
    createdDaysAgo: 620,
  },
  {
    series: 'devconf',
    name: 'online-2026',
    title: 'DevConf Online 2026',
    timeZone: 'UTC',
    region: 'eu-central-1',
    start: '2026-12-02',
    end: '2026-12-03',
    capacity: 5000,
    // The online passes sell to an international audience: in euros, with the Czech VAT of the organizer.
    currency: 'EUR',
    vatRate: 21,
    price: 79,
    services: ['ticketing', 'streaming', 'call-for-papers', 'sponsorship', 'event-app'],
    cfp: { opens: '2026-08-03', deadline: '2026-10-31T23:59:00Z' },
    rooms: ['main-stage', 'deep-dive'],
    eventCode: 'DC26-ONL',
    createdDaysAgo: 210,
  },
  {
    series: 'devconf',
    name: 'prague-2026',
    title: 'DevConf Prague 2026',
    venue: praguecc,
    timeZone: 'Europe/Prague',
    region: 'eu-central-1',
    start: '2026-11-18',
    end: '2026-11-20',
    capacity: 1800,
    grewFrom: [1400, 1600],
    currency: 'CZK',
    vatRate: 21,
    price: 10500,
    services: [...currentServices, 'event-app', 'matchmaking'],
    cfp: { opens: '2026-03-02', deadline: '2026-06-30T23:59:00+02:00', programmeFinal: true },
    rooms: ['congress-hall', 'forum-hall', 'meeting-hall-a', 'meeting-hall-b'],
    eventCode: 'DC26-PRG',
    createdDaysAgo: 330,
  },
  {
    series: 'devconf',
    name: 'berlin-2027',
    title: 'DevConf Berlin 2027',
    venue: { name: 'Estrel Congress Center', city: 'Berlin', country: 'DE', contact: 'sales@estrel.example' },
    timeZone: 'Europe/Berlin',
    region: 'eu-central-1',
    start: '2027-05-12',
    end: '2027-05-14',
    capacity: 2200,
    currency: 'EUR',
    vatRate: 19,
    price: 450,
    services: ['ticketing', 'call-for-papers', 'sponsorship'],
    cfp: { opens: '2026-12-01', deadline: '2027-02-28T23:59:00+01:00' },
    rooms: [],
    eventCode: 'DC27-BER',
    // Goes live when its ticket sales and its call for papers open.
    salesOpen: '2026-12-01',
    createdDaysAgo: 12,
  },
  {
    series: 'data-summit',
    name: 'amsterdam-2025',
    title: 'Data Summit Amsterdam 2025',
    venue: rai,
    timeZone: 'Europe/Amsterdam',
    region: 'eu-west-1',
    start: '2025-06-10',
    end: '2025-06-11',
    capacity: 900,
    currency: 'EUR',
    vatRate: 21,
    price: 495,
    services: ['ticketing', 'streaming', 'badges', 'sponsorship'],
    rooms: ['auditorium', 'elicium-1'],
    eventCode: 'DS25-AMS',
    createdDaysAgo: 700,
  },
  {
    series: 'data-summit',
    name: 'london-2026',
    title: 'Data Summit London 2026',
    venue: { name: 'ExCeL London', city: 'London', country: 'GB', contact: 'venues@excel-london.example' },
    timeZone: 'Europe/London',
    region: 'eu-west-2',
    start: '2026-11-25',
    end: '2026-11-26',
    capacity: 1200,
    grewFrom: [1000],
    currency: 'GBP',
    vatRate: 20,
    price: 450,
    services: ['ticketing', 'streaming', 'badges', 'call-for-papers', 'sponsorship'],
    cfp: { opens: '2026-04-01', deadline: '2026-07-31T23:59:00+01:00', programmeFinal: true },
    rooms: ['platinum-suite', 'capital-hall'],
    eventCode: 'DS26-LON',
    createdDaysAgo: 300,
  },
  {
    series: 'data-summit',
    name: 'amsterdam-2027',
    title: 'Data Summit Amsterdam 2027',
    venue: rai,
    timeZone: 'Europe/Amsterdam',
    region: 'eu-west-1',
    start: '2027-06-08',
    end: '2027-06-09',
    capacity: 1000,
    currency: 'EUR',
    vatRate: 21,
    price: 520,
    services: ['ticketing', 'call-for-papers'],
    cfp: { opens: '2027-01-11', deadline: '2027-03-31T23:59:00+02:00' },
    rooms: [],
    eventCode: 'DS27-AMS',
    salesOpen: '2027-01-11',
    createdDaysAgo: 6,
  },
  {
    series: 'design-week',
    name: 'milan-2026',
    title: 'Design Week Milan 2026',
    venue: superstudio,
    timeZone: 'Europe/Rome',
    region: 'eu-south-1',
    start: '2026-04-20',
    end: '2026-04-24',
    capacity: 3000,
    currency: 'EUR',
    vatRate: 22,
    price: 180,
    services: ['ticketing', 'streaming', 'badges', 'sponsorship', 'venue-wifi'],
    rooms: ['main-hall'],
    eventCode: 'DW26-MIL',
    createdDaysAgo: 500,
  },
  {
    series: 'design-week',
    name: 'copenhagen-2026',
    title: 'Design Week Copenhagen 2026',
    venue: {
      name: 'Bella Center Copenhagen',
      city: 'Copenhagen',
      country: 'DK',
      contact: 'events@bellacenter.example',
    },
    timeZone: 'Europe/Copenhagen',
    region: 'eu-north-1',
    start: '2026-10-28',
    end: '2026-10-30',
    capacity: 2500,
    currency: 'DKK',
    vatRate: 25,
    price: 1450,
    services: ['ticketing', 'streaming', 'badges', 'sponsorship', 'venue-wifi'],
    rooms: ['hall-c1'],
    eventCode: 'DW26-CPH',
    createdDaysAgo: 280,
    inPersonOnly: true,
  },
  {
    series: 'design-week',
    name: 'lisbon-2026',
    title: 'Design Week Lisbon 2026',
    venue: { name: 'LX Factory', city: 'Lisbon', country: 'PT', contact: 'hello@lxfactory.example' },
    timeZone: 'Europe/Lisbon',
    region: 'eu-south-2',
    start: '2026-09-14',
    end: '2026-09-16',
    capacity: 1400,
    currency: 'EUR',
    vatRate: 23,
    price: 160,
    services: ['ticketing', 'badges', 'venue-wifi'],
    rooms: [],
    eventCode: 'DW26-LIS',
    createdDaysAgo: 260,
    cancelled: true,
  },
  {
    series: 'design-week',
    name: 'milan-2027',
    title: 'Design Week Milan 2027',
    venue: superstudio,
    timeZone: 'Europe/Rome',
    region: 'eu-south-1',
    start: '2027-04-19',
    end: '2027-04-23',
    capacity: 3200,
    currency: 'EUR',
    vatRate: 22,
    price: 190,
    services: ['ticketing', 'sponsorship'],
    rooms: [],
    eventCode: 'DW27-MIL',
    salesOpen: '2027-01-19',
    createdDaysAgo: 9,
  },
]

/** The test editions of development: a bare online one for the developers and a QA rig with a printer and a camera. */
const testEditions: Edition[] = [
  {
    series: 'devconf',
    name: 'dev',
    title: 'DevConf dev edition',
    timeZone: 'Europe/Prague',
    region: 'eu-central-1',
    start: '2026-11-18',
    end: '2026-11-20',
    capacity: 50,
    currency: 'CZK',
    vatRate: 21,
    price: 250,
    services: ['ticketing'],
    rooms: [],
    eventCode: 'DC-DEV',
    sandbox: true,
    createdDaysAgo: 120,
    values: { owner: 'Ada Lovelace', purpose: 'Local development against the sandbox payment provider' },
  },
  {
    series: 'devconf',
    name: 'qa',
    title: 'DevConf QA edition',
    venue: { name: 'Gatherly test rig', city: 'Prague', country: 'CZ', contact: 'qa@gatherly.example' },
    timeZone: 'Europe/Prague',
    region: 'eu-central-1',
    start: '2026-11-18',
    end: '2026-11-20',
    capacity: 200,
    currency: 'CZK',
    vatRate: 21,
    price: 250,
    services: ['ticketing', 'streaming', 'badges'],
    rooms: ['test-room'],
    eventCode: 'DC-QA',
    sandbox: true,
    createdDaysAgo: 90,
    values: { owner: 'Frances Allen', purpose: 'Release tests with a real badge printer and a test camera' },
  },
]

const seriesNamed = (name: string) => seriesList.find((series) => series.name === name)!
const seriesOf = (edition: Edition) => seriesNamed(edition.series)
const editionNamed = (name: string) => editions.find((edition) => edition.name === name)!
const flagship = editionNamed('prague-2026')
const serviceOf = (name: string) => services.find((service) => service.name === name)!

/**
 * An edition's lifecycle from its dates, shared by all its services and job runs: an announced edition is scheduled
 * until its sales open, and every edition expires once it is settled after its last day.
 */
function lifecycleOf(edition: Edition): Lifecycle {
  if (edition.cancelled || edition.sandbox) {
    return {}
  }
  return {
    ...(edition.salesOpen ? { startsIn: daysUntil(edition.salesOpen) } : {}),
    expiresIn: daysUntil(edition.end) + settleDays,
  }
}

const isPast = (edition: Edition) => (lifecycleOf(edition).expiresIn ?? 1) < 0
/** The editions on sale now: neither past, nor announced, nor cancelled. */
const onSale = (edition: Edition) => !isPast(edition) && !edition.salesOpen && !edition.cancelled
/** "Venue Wi-Fi" → "venue Wi-Fi", for titles like "DevConf Prague 2026 venue Wi-Fi". */
const lowerFirst = (title: string) => title.replace(/^\w/, (char) => char.toLowerCase())
const serviceTitle = (owner: { title: string }, service: string) =>
  `${owner.title} ${lowerFirst(serviceOf(service).title)}`
/** A series books a service two days after both exist. */
const bookedDaysAgo = (series: Series, service: string) => Math.min(series.since, serviceOf(service).since) - 2
const websiteOf = (edition: Edition, env: Env) => `https://${envPrefix[env]}${seriesOf(edition).domain}/${edition.name}`
const secret = (edition: Edition, env: Env, name: string) =>
  `@(${envPrefix[env]}${edition.series}.${edition.name}.${name}, primaryVault)`
const roundPrice = (value: number, currency: Currency) => Math.round(value / priceStep[currency]) * priceStep[currency]

interface Tier {
  name: string
  price: number
  quota: number
}

const tier = (name: string, price: number, quota: number): Tier => ({ name, price, quota })

/** Online editions have a free stream and a paid pass; in-person ones the early bird, regular and student tiers. */
function tiersOf(edition: Edition): Tier[] {
  const { capacity, currency, price } = edition
  if (!edition.venue) {
    const pro = Math.round(capacity * 0.2)
    return [tier('stream-pass', 0, capacity - pro), tier('pro-pass', price, pro)]
  }
  return [
    tier('early-bird', roundPrice(price * 0.7, currency), Math.round(capacity * 0.2)),
    tier('regular', price, Math.round(capacity * 0.7)),
    tier('student', roundPrice(price * 0.3, currency), Math.round(capacity * 0.1)),
  ]
}

function ticketingDocument(edition: Edition, env: Env) {
  return {
    currency: edition.currency,
    vatRate: edition.vatRate,
    tiers: tiersOf(edition),
    salesClose: edition.end,
    // The invoice numbers of an edition start with its event code, a Value of the edition.
    invoicePrefix: '@annotation("/eventCode", "Context")',
    ...(env === 'production' ? {} : { paymentMode: 'test' }),
  }
}

/** The ticket sales of DevConf Prague 2026 step by step, in crowns; the release candidate adds a last-minute tier. */
function flagshipTicketSales(env: Env, next: boolean): Record<string, unknown>[] {
  const document = ticketingDocument(flagship, env)
  const [earlyBird, regular, student] = document.tiers as [Tier, Tier, Tier]
  const moreEarlyBirds = { ...earlyBird, quota: earlyBird.quota + 100 }
  const workshop = tier('workshop-day', 4900, 120)
  const dearer = { ...regular, price: 11500 }
  const steps: Record<number, Record<string, unknown>> = {
    2: { tiers: [moreEarlyBirds, regular, student] },
    3: { tiers: [moreEarlyBirds, regular, student, workshop] },
    // The early birds sold out in September.
    4: { tiers: [regular, student, workshop] },
    5: { tiers: [dearer, student, workshop] },
    6: { groupDiscount: { minTickets: 5, percent: 10 } },
  }
  const sales = evolve(document, 6, (version) => steps[version]!)
  if (next) {
    sales.push({ ...sales.at(-1)!, tiers: [dearer, student, workshop, tier('last-minute', 13500, 100)] })
  }
  return sales
}

const flagshipTicketing = 'exe.devconf.ticketing.prague-2026'

/** The flagship's current ticket sales, for the views that copy production without its history. */
const currentSales = (env: Env) => ({ [flagshipTicketing]: [flagshipTicketSales(env, false).at(-1)!] })

/** The setup of one service for one edition; none where the service has nothing of its own there. */
function executionDocument(service: string, edition: Edition, env: Env): Record<string, unknown> | undefined {
  const series = seriesOf(edition)
  switch (service) {
    case 'ticketing':
      return ticketingDocument(edition, env)
    case 'streaming':
      return {
        ingest: {
          protocol: 'srt',
          host: `ingest.${env === 'production' ? edition.region : env}.stream.gatherly.example`,
          port: 9000,
          streamId: `${series.name}-${edition.name}`,
        },
        rooms: edition.rooms,
        streamKey: secret(edition, env, 'mux-stream-key'),
        playerPage: '@config("/website")',
      }
    case 'badges':
      return {
        // The QA rig has one real printer on the office network; a venue gets one printer per 600 attendees.
        printers:
          env === 'development'
            ? [`badge-printer-${edition.name}.rig.gatherly.example`]
            : ['a', 'b', 'c', 'd']
                .slice(0, Math.ceil(edition.capacity / 600))
                .map((printer) => `badge-printer-${printer}.${edition.name}.${series.name}.onsite.gatherly.example`),
        model: 'zebra-zd621',
        template: `${series.name}-4x6`,
      }
    case 'call-for-papers':
      return edition.cfp && { opens: edition.cfp.opens, deadline: edition.cfp.deadline, tracks: series.tracks }
    case 'sponsorship':
      // Online sponsors get a virtual booth in the sponsor lobby instead of floor space, from a fixed package table.
      return edition.venue
        ? {
            packages: {
              platinum: edition.capacity >= 2000 ? 3 : 2,
              gold: Math.round(edition.capacity / 300),
              silver: Math.round(edition.capacity / 150),
            },
            boothPlan: `https://files.gatherly.example/${series.name}/${edition.name}/booth-plan.pdf`,
            leadRetrieval: edition.services.includes('badges'),
          }
        : {
            packages: { platinum: 2, gold: 4, silver: 8 },
            virtualBooths: true,
            sponsorLobby: `${websiteOf(edition, env)}/sponsors`,
          }
    case 'venue-wifi':
      return {
        ssid: `${series.title.replaceAll(' ', '')}-Guest`,
        password: secret(edition, env, 'wifi-password'),
        bandwidthMbps: Math.ceil(edition.capacity / 100) * 50,
        accessPoints: Math.ceil(edition.capacity / 40),
      }
    case 'event-app':
      return {
        appId: `example.gatherly.${series.name}`,
        features: edition.venue ? ['agenda', 'floor-plan', 'push'] : ['agenda', 'push', 'chat'],
        pushTopic: `${series.name}-${edition.name}`,
      }
    case 'matchmaking':
      return { interests: series.tracks, meetingPoints: ['networking-lounge', 'forum-foyer'], maxIntrosPerDay: 2 }
  }
  return undefined
}

/** The job runs that differ from their job: their own subject line or schedule. */
const jobRunDocuments: Record<string, Record<string, unknown>> = {
  'uxe.devconf.ticketing.prague-2026.reminder-mail': {
    subjectLine: 'DevConf Prague 2026 starts soon: your badge is waiting',
    ticketLink: '@config("/website")',
    sendDaysBefore: [14, 3, 1],
  },
  'uxe.devconf.ticketing.online-2026.reminder-mail': {
    subjectLine: 'Your DevConf Online stream link',
    streamLink: '@config("/website")',
    sendDaysBefore: [3, 0],
  },
  'uxe.data-summit.ticketing.london-2026.reminder-mail': {
    subjectLine: 'Data Summit London: badge pickup opens at 8:00',
    sendDaysBefore: [7, 1],
  },
  'uxe.devconf.call-for-papers.online-2026.review-reminder': { cron: '0 8 * * 1,4', timeZone: 'UTC' },
  // Development mails every few minutes, to the QA inbox only.
  'uxe.devconf.ticketing.dev.reminder-mail': { cron: '*/5 * * * *', dryRun: true },
  'uxe.devconf.ticketing.qa.reminder-mail': { cron: '*/15 * * * *', redirectTo: 'qa@gatherly.example' },
}

/** The services of the view, with their jobs. */
function addServices(b: ScopeBuilder, names: readonly string[], jobNames = jobs.map((job) => job.name)) {
  for (const service of services.filter((candidate) => names.includes(candidate.name))) {
    b.responsibility(service.name, {
      title: service.title,
      description: service.description,
      labels: service.labels,
      link: `${docs}/services/${service.name}`,
      values: { vendor: service.vendor, owner: service.owner },
      createdDaysAgo: service.since,
    })
  }
  for (const job of jobs.filter(
    (candidate) => names.includes(candidate.service) && jobNames.includes(candidate.name),
  )) {
    b.unit(job.service, job.name, {
      unitType: job.unitType,
      definition: job.definition,
      title: job.title,
      description: job.description,
      createdDaysAgo: serviceOf(job.service).since,
    })
  }
}

/** The ticketing defaults over the seasons, one change per version: payments, invoices, refunds and fraud checks. */
const ticketingChanges: Record<number, Record<string, unknown>> = {
  2: { invoice: { dueDays: 30, footer: 'Gatherly s.r.o., Prague' } },
  3: { paymentMethods: ['card', 'bank-transfer'] },
  4: { maxTicketsPerOrder: 15 },
  5: { invoice: { dueDays: 14 } },
  6: { waitlist: { enabled: true, offerHours: 48 } },
  7: { attendeeFields: ['name', 'company'] },
  8: { attendeeFields: ['name', 'company', 'dietary'] },
  9: { refundPolicy: { untilDaysBefore: 14 } },
  10: { transfers: { allowNameChange: true, untilDaysBefore: 2 } },
  11: { maxTicketsPerOrder: 20 },
  12: { paymentMethods: ['card', 'bank-transfer', 'apple-pay', 'google-pay'] },
  13: { invoice: { reverseChargeEu: true } },
  14: { attendeeFields: ['name', 'company', 'dietary', 'tshirt-size'] },
  15: { checkoutTimeoutMinutes: 15 },
  16: { fraudCheck: { provider: 'stripe-radar', blockScore: 75 } },
  17: { refundPolicy: { feePercent: 5 } },
  18: { waitlist: { offerHours: 24 } },
  19: { fraudCheck: { blockScore: 65 } },
}

/** An edition's ticketing needs its currency and tiers; the next release also asks for the VAT and every quota. */
function ticketingSchema(strict: boolean) {
  return {
    type: 'object',
    properties: {
      currency: { type: 'string', pattern: '^[A-Z]{3}$' },
      vatRate: { type: 'number', minimum: 0, maximum: 30 },
      tiers: {
        type: 'array',
        minItems: 1,
        items: {
          type: 'object',
          properties: {
            name: { type: 'string', pattern: '^[a-z0-9-]+$' },
            price: { type: 'number', minimum: 0 },
            quota: { type: 'integer', minimum: 1 },
          },
          required: strict ? ['name', 'price', 'quota'] : ['name', 'price'],
        },
      },
    },
    required: strict ? ['currency', 'tiers', 'vatRate'] : ['currency', 'tiers'],
  }
}

/** The service defaults, the jobs, the mail sender of every job run and the ticketing schema. */
function serviceDocuments(b: ScopeBuilder, env: Env, next: boolean) {
  const configure = (key: string, versions: Parameters<ScopeBuilder['configure']>[1], options?: HistoryOptions) => {
    if (b.has(key)) {
      b.configure(key, versions, options)
    }
  }

  // Ticketing defaults, tuned over the seasons: the long history of the project.
  const ticketing = evolve(
    {
      provider: 'pretix',
      apiBase: `https://tickets.${envPrefix[env]}gatherly.example/api/v1`,
      currency: 'EUR',
      checkoutTimeoutMinutes: 20,
      maxTicketsPerOrder: 10,
      refundPolicy: { untilDaysBefore: 30, feePercent: 10 },
      paymentMethods: ['card'],
      waitlist: { enabled: false },
    },
    19,
    (version) => ticketingChanges[version]!,
  )
  if (env === 'development') {
    ticketing.push({ ...ticketing.at(-1)!, logLevel: 'debug', checkoutTimeoutMinutes: 60 })
  }
  if (next) {
    ticketing.push({ ...ticketing.at(-1)!, walletPasses: ['apple', 'google'] })
  }
  configure('rst.ticketing', ticketing, { every: 7, lastDaysAgo: next ? 1 : 2 })

  const streaming = [
    { provider: 'mux', latencyMode: 'standard', captions: 'none', recordingDelayHours: 48 },
    { provider: 'mux', latencyMode: 'standard', captions: 'auto-en', recordingDelayHours: 24 },
    { provider: 'mux', latencyMode: 'low', captions: 'auto-en', recordingDelayHours: 24 },
  ]
  if (next) {
    streaming.push({ ...streaming.at(-1)!, captions: 'auto-multilingual' })
  }
  configure('rst.streaming', streaming, { every: 20 })
  configure('rst.badges', { format: 'label-4x6in', qrPayload: 'ticket-id', printOnArrival: true, reprintLimit: 2 })
  configure(
    'rst.call-for-papers',
    [
      { platform: 'sessionize', reviewersPerTalk: 2, anonymousReview: false, maxSubmissionsPerSpeaker: 3 },
      { platform: 'sessionize', reviewersPerTalk: 3, anonymousReview: true, maxSubmissionsPerSpeaker: 3 },
    ],
    { every: 24 },
  )
  configure('rst.sponsorship', { packageNames: ['platinum', 'gold', 'silver', 'startup'], invoiceTermsDays: 30 })
  configure('rst.venue-wifi', { provider: 'meraki', captivePortal: true, clientIsolation: true })
  configure('rst.event-app', { stores: ['app-store', 'google-play'], pushProvider: 'fcm', offlineAgenda: true })
  configure('rst.matchmaking', { algorithm: 'interest-overlap', minScore: 0.6, optIn: 'explicit', maxIntrosPerDay: 3 })

  configure(
    'unt.ticketing.reminder-mail',
    [
      { template: 'ticket-reminder', sendDaysBefore: [7, 1], audience: 'ticket-holders' },
      { template: 'ticket-reminder', sendDaysBefore: [14, 3, 1], audience: 'ticket-holders' },
      { template: 'ticket-reminder-v2', sendDaysBefore: [14, 3, 1], audience: 'ticket-holders' },
    ],
    { every: 9, lastDaysAgo: 4 },
  )
  configure('unt.call-for-papers.review-reminder', { template: 'review-nudge', onlyPendingReviews: true })
  configure('unt.sponsorship.lead-digest', { template: 'lead-digest', batchMinutes: 60, attachment: 'csv' })
  configure('unt.matchmaking.intro-mail', { template: 'intro-mail', introsPerMail: 2 })

  // Every job run mails as Gatherly and answers to the organizer of its series (a Value of the series).
  const sender = evolve(
    { sender: { name: 'Gatherly', address: `no-reply@${envPrefix[env]}gatherly.example` }, retries: 2 },
    3,
    (version) =>
      version === 2
        ? { replyTo: '@annotation("/organizerEmail", "Subject")' }
        : { quietHours: { from: '21:00', to: '08:00' } },
  )
  if (env === 'development') {
    sender.push({ ...sender.at(-1)!, smtpRelay: 'mailpit.dev.gatherly.example:1025' })
  }
  b.template('uxe', sender, { every: 15 })

  if (b.has('rst.ticketing')) {
    b.descendantSchema(
      'rst.ticketing',
      'exe',
      next ? [ticketingSchema(false), ticketingSchema(true)] : ticketingSchema(false),
    )
  }
}

/** A series with its bookings of the services in the view. */
function addSeries(b: ScopeBuilder, series: Series, env: Env) {
  b.subject(series.name, {
    title: series.title,
    description: series.description,
    labels: series.labels,
    link: `https://${series.domain}`,
    // Outside production the organizer mails go to Gatherly's own inbox of the series.
    values:
      env === 'production'
        ? series.values
        : { ...series.values, organizerEmail: `${series.name}-${env}@gatherly.example` },
    timeZone: series.timeZone,
    createdDaysAgo: series.since,
    createdBy: 'donald',
  })
  const brand = {
    brand: series.title,
    website: `https://${envPrefix[env]}${series.domain}`,
    codeOfConduct: `https://${series.domain}/code-of-conduct`,
    language: 'en',
  }
  b.configure(
    `sbt.${series.name}`,
    series.name === 'devconf'
      ? [{ ...brand, language: 'cs' }, brand, { ...brand, accessibility: 'https://devconf.example/accessibility' }]
      : brand,
    { every: 30, lastDaysAgo: 12 },
  )

  for (const service of series.services.filter((name) => b.has(`rst.${name}`))) {
    // Design Week goes in-person only: its streaming booking ends with the year.
    const ending = series.name === 'design-week' && service === 'streaming'
    const key = b.usage(series.name, service, {
      title: serviceTitle(series, service),
      labels: ending ? [series.package, 'notice-given'] : [series.package],
      values: { contract: `GTH-${series.values.crmId.slice(-2)}-${service.slice(0, 3).toUpperCase()}` },
      timeZone: series.timeZone,
      createdDaysAgo: bookedDaysAgo(series, service),
      createdBy: 'ada',
      ...(ending ? { expiresIn: daysUntil('2026-12-31'), modifiedDaysAgo: 21, modifiedBy: 'ada' as const } : {}),
    })
    if (service === 'ticketing') {
      b.configure(key, {
        package: series.package,
        feePercent: series.package === 'full-service' ? 3.5 : 1.9,
        payoutAccount: `@(${envPrefix[env]}${series.name}.stripe-account, primaryVault)`,
      })
    } else if (service === 'streaming') {
      b.configure(key, { includedHours: series.package === 'full-service' ? 40 : 16, maxViewers: 5000 })
    } else if (service === 'matchmaking') {
      // A pilot on top of the series' package, free until the end of January.
      b.configure(key, { plan: 'pilot', freeUntil: '2027-01-31' })
    }
  }
}

/** An edition with the services of its series that the view has, and the job runs of those services. */
function addEdition(
  b: ScopeBuilder,
  edition: Edition,
  env: Env,
  histories: Record<string, Record<string, unknown>[]> = {},
) {
  const series = seriesOf(edition)
  const { timeZone } = edition
  const lifecycle = lifecycleOf(edition)
  const createdBy = b.rng.pick(editors)
  const key = b.context(series.name, edition.name, {
    title: edition.title,
    description: edition.venue
      ? `${edition.start} to ${edition.end} at **${edition.venue.name}**, ${edition.venue.city}.`
      : `${edition.start} to ${edition.end}, online.`,
    labels: [edition.venue ? 'in-person' : 'online', edition.venue?.country.toLowerCase() ?? 'global'],
    link: websiteOf(edition, env),
    values: {
      eventCode: edition.eventCode,
      ...(edition.venue ? { venueContact: edition.venue.contact } : {}),
      ...edition.values,
    },
    timeZone,
    ...lifecycle,
    createdDaysAgo: edition.createdDaysAgo,
    createdBy,
  })
  const document = {
    ...(edition.venue
      ? { venue: { name: edition.venue.name, city: edition.venue.city, country: edition.venue.country } }
      : { platform: 'gatherly-live' }),
    dates: { start: edition.start, end: edition.end },
    capacity: edition.capacity,
    website: websiteOf(edition, env),
    ...(env === 'development' ? { logLevel: 'debug' } : {}),
  }
  b.configure(
    key,
    [...(edition.grewFrom ?? []), edition.capacity].map((capacity) => ({ ...document, capacity })),
    { every: 20, lastDaysAgo: 6 },
  )

  for (const service of edition.services.filter((name) => b.has(`usg.${series.name}.${name}`))) {
    const off = service === 'streaming' && edition.inPersonOnly
    // Set up once both the edition and the booking exist.
    const since = Math.min(edition.createdDaysAgo, bookedDaysAgo(series, service))
    const createdDaysAgo = since - b.rng.int(0, Math.min(10, since - 1))
    const execution = b.execution(series.name, service, edition.name, {
      title: serviceTitle(edition, service),
      timeZone,
      ...lifecycle,
      createdDaysAgo,
      createdBy,
      ...(off
        ? {
            disabled: true,
            description: 'In person only: the halls have no camera crew this year.',
            modifiedDaysAgo: 21,
            modifiedBy: 'ada' as const,
          }
        : {}),
    })
    // Past editions keep the records of their ticket sales; the rest of their setup was cleaned up.
    const own = isPast(edition) && service !== 'ticketing' ? undefined : executionDocument(service, edition, env)
    if (histories[execution]) {
      b.configure(execution, histories[execution], { every: 12, lastDaysAgo: 3 })
    } else if (own) {
      b.configure(execution, own)
    }

    for (const job of jobs.filter((candidate) => candidate.service === service && candidate.runs(edition))) {
      if (!b.has(`unt.${service}.${job.name}`)) {
        continue
      }
      // Once the programme is final, the review reminders are switched off.
      const finished = job.name === 'review-reminder' && edition.cfp?.programmeFinal && !isPast(edition)
      const run = b.unitOfExecution(series.name, service, edition.name, job.name, {
        title: `${edition.title} ${lowerFirst(job.title)}`,
        timeZone,
        ...lifecycle,
        createdDaysAgo,
        createdBy,
        ...(finished ? { disabled: true, description: 'The programme is final; no more reviews to chase.' } : {}),
      })
      if (jobRunDocuments[run]) {
        b.configure(run, jobRunDocuments[run])
      }
    }
  }
}

function production(b: ScopeBuilder, next: boolean) {
  // The next release brings the event app to DevConf.
  addServices(b, next ? [...currentServices, 'event-app'] : currentServices)
  serviceDocuments(b, 'production', next)
  seriesList.forEach((series) => addSeries(b, series, 'production'))
  const histories = { [flagshipTicketing]: flagshipTicketSales('production', next) }
  for (const edition of editions.filter((candidate) => !next || !candidate.cancelled)) {
    addEdition(b, edition, 'production', histories)
  }
  if (next) {
    return
  }

  // Lisbon was cancelled when the venue’s renovation ran late, with its services; Wi-Fi had been booked for the
  // online edition by mistake and was removed alone.
  const online = editionNamed('online-2026')
  b.execution('devconf', 'venue-wifi', online.name, {
    title: serviceTitle(online, 'venue-wifi'),
    timeZone: online.timeZone,
    ...lifecycleOf(online),
    createdDaysAgo: 15,
    createdBy: 'frances',
  })
  b.trash('cnt.design-week.lisbon-2026', { daysAgo: 52, by: 'donald' })
  b.trash('exe.devconf.venue-wifi.online-2026', { daysAgo: 9, by: 'frances' })
}

/** DevConf's test editions: a bare online one and a QA rig, on the sandbox endpoints with debug logging. */
function development(b: ScopeBuilder) {
  addServices(b, ['ticketing', 'streaming', 'badges'])
  serviceDocuments(b, 'development', false)
  addSeries(b, seriesNamed('devconf'), 'development')
  testEditions.forEach((edition) => addEdition(b, edition, 'development'))
}

/** Staging copies of the editions on sale, with test payments and the staging hosts. */
function staging(b: ScopeBuilder) {
  addServices(b, currentServices)
  serviceDocuments(b, 'staging', false)
  seriesList.forEach((series) => addSeries(b, series, 'staging'))
  editions.filter(onSale).forEach((edition) => addEdition(b, edition, 'staging', currentSales('staging')))
}

/** The board's feature-delivery1: DevConf Prague 2026 with ticketing and the new matchmaking and its intro mail. */
function featureDelivery(b: ScopeBuilder) {
  addServices(b, ['ticketing', 'matchmaking'], ['intro-mail'])
  serviceDocuments(b, 'production', false)
  addSeries(b, seriesNamed('devconf'), 'production')
  addEdition(b, flagship, 'production', currentSales('production'))
}

export const conferences: ShowcaseProject = {
  org: 'gatherly',
  project: 'conferences',
  description:
    'Conference series and their yearly editions, with ticketing, streaming, badges and the mail jobs booked for each edition.',
  members: { ada: 'Owner', donald: 'Administrator', frances: 'Contributor' },
  settings: {
    types: {
      rst: { label: 'Event service', color: 'violet', icon: 'i-lucide-component' },
      unt: { label: 'Job', color: 'fuchsia', icon: 'i-lucide-clock' },
      sbt: { label: 'Event series', plural: 'Event series', color: 'blue', icon: 'i-lucide-layers' },
      usg: { label: 'Booking', color: 'amber', icon: 'i-lucide-bookmark' },
      cnt: { label: 'Edition', color: 'teal', icon: 'i-lucide-calendar' },
      exe: { label: 'Edition service', color: 'emerald', icon: 'i-lucide-play' },
      uxe: { label: 'JobRun', color: 'orange', icon: 'i-lucide-workflow' },
    },
    defaultTimeZone: 'Europe/Prague',
    dateFormat: 'd mmm yyyy',
  },
  views: environmentViews({
    development: 'Development: the DevConf dev and QA editions',
    staging: 'Staging copies of the four editions on sale',
    'release-candidate': 'Release 2026.11: wallet passes and the event app',
    'feature-delivery1': 'Matchmaking for DevConf Prague 2026',
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
