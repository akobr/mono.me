import { seededRng, type Meta, type Rng, type ScopeBuilder } from '../builder'
import { evolve } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// Example 2 of the Miro board: a multi-tenant B2B SaaS with end customers. Gym chains (the tenants) buy Fabrikam's
// fitness platform, enable its applications with their own price lists (app enablements) and sell them to their
// members (contexts of the tenant). An app assignment gives one member one application on a plan. Units and units of
// execution are not used: nothing in this product runs on a schedule of its own.

const docs = 'https://docs.fitness-cloud.fabrikam.example'

/** Where a view runs; production also carries the price-list history and the lifecycle of the enablements. */
type Environment = 'production' | 'staging' | 'dev' | 'feature1'

const apiOf = (app: string, environment: Environment) =>
  `https://${app}.${environment === 'production' ? '' : `${environment}.`}api.fitness-cloud.fabrikam.example`

interface App {
  name: string
  title: string
  description: string
  labels: string[]
  launchedDaysAgo: number
  /** List price of the reference plan (factor 1) in EUR, before the tenant's currency and positioning. */
  price: number
  billingPeriod: 'month' | 'session'
  trialDays: number
  /** The plans, cheapest first, priced relative to the reference plan; an assignment's plan must be one of them. */
  plans: Record<string, number>
  /** How often members take the application, relative to the others. */
  popularity: number
  /** The product defaults, oldest version first. */
  defaults: Record<string, unknown>[]
}

const apps: App[] = [
  {
    name: 'class-booking',
    title: 'Class booking',
    description:
      'Group classes with **waitlists** and check-in at the studio door. Members book up to two weeks ahead.',
    labels: ['core'],
    launchedDaysAgo: 1100,
    price: 19,
    billingPeriod: 'month',
    trialDays: 14,
    plans: { 'four-per-month': 1, 'eight-per-month': 1.6, unlimited: 2.2 },
    popularity: 26,
    defaults: [
      { bookingWindowDays: 7, cancellationHours: 12, waitlist: { enabled: false } },
      { bookingWindowDays: 7, cancellationHours: 12, waitlist: { enabled: true, maxSize: 10 } },
      { bookingWindowDays: 14, cancellationHours: 12, waitlist: { enabled: true, maxSize: 10 }, noShowFee: true },
      {
        bookingWindowDays: 14,
        cancellationHours: 6,
        waitlist: { enabled: true, maxSize: 15, autoPromote: true },
        noShowFee: true,
      },
    ],
  },
  {
    name: 'gym-access',
    title: 'Gym access',
    description: [
      'Opens the turnstiles with a QR code or NFC in the member’s phone.',
      '',
      '- anti-passback against shared codes',
      '- offline tokens when the club’s network is down',
    ].join('\n'),
    labels: ['core', 'hardware'],
    launchedDaysAgo: 1100,
    price: 29,
    billingPeriod: 'month',
    trialDays: 7,
    plans: { 'off-peak': 0.75, full: 1, 'all-clubs': 1.4 },
    popularity: 34,
    defaults: [
      { methods: ['qr', 'nfc'], antiPassbackSeconds: 60, offlineTokenHours: 24, controllerProtocol: 'osdp-v2' },
    ],
  },
  {
    name: 'nutrition-coach',
    title: 'Nutrition coach',
    description: 'Meal plans from the member’s goals and body scans; the coached plan adds a dietitian in the chat.',
    labels: ['wellness'],
    launchedDaysAgo: 700,
    price: 12,
    billingPeriod: 'month',
    trialDays: 14,
    plans: { 'self-guided': 1, coached: 2.5 },
    popularity: 9,
    defaults: [
      { mealPlanRefreshDays: 7, foodDatabase: 'eu-2025', coachChat: false },
      { mealPlanRefreshDays: 7, foodDatabase: 'eu-2026', coachChat: true, coachResponseHours: 24 },
    ],
  },
  {
    name: 'personal-training',
    title: 'Personal training',
    description: 'One-to-one sessions with the club’s trainers, booked and paid in the app.',
    labels: ['coaching'],
    launchedDaysAgo: 900,
    price: 45,
    billingPeriod: 'session',
    trialDays: 0,
    plans: { 'single-session': 1, monthly: 3.6, 'five-pack': 4.6, 'ten-pack': 8.8 },
    popularity: 8,
    defaults: [{ sessionMinutes: 60, cancellationHours: 24, trainerPayoutPercent: 60 }],
  },
  {
    name: 'online-classes',
    title: 'Online classes',
    description: 'Live and on-demand video classes for the days a member trains at home.',
    labels: ['digital'],
    launchedDaysAgo: 800,
    price: 9,
    billingPeriod: 'month',
    trialDays: 30,
    plans: { 'on-demand': 1, 'live-and-on-demand': 1.8 },
    popularity: 11,
    defaults: [
      {
        streaming: { cdn: 'https://stream.fitness-cloud.fabrikam.example', maxResolution: '1080p' },
        liveClassesPerWeek: 12,
      },
    ],
  },
  {
    name: 'spa-access',
    title: 'Spa access',
    description: 'Time slots in the spa, with the towel service at the reception.',
    labels: ['wellness'],
    launchedDaysAgo: 600,
    price: 25,
    billingPeriod: 'month',
    trialDays: 0,
    plans: { 'day-pass': 0.4, monthly: 1 },
    popularity: 5,
    defaults: [{ slotMinutes: 120, maxGuests: 1, towelService: true }],
  },
  {
    name: 'locker-rental',
    title: 'Locker rental',
    description: 'A permanent locker opened with a PIN from the app.',
    labels: ['facility', 'hardware'],
    launchedDaysAgo: 650,
    price: 6,
    billingPeriod: 'month',
    trialDays: 0,
    plans: { small: 1, large: 1.5 },
    popularity: 6,
    defaults: [{ pinLength: 6, autoReleaseDays: 3 }],
  },
  {
    name: 'body-scan',
    title: 'Body scan',
    description: 'Body composition from the scanner at the reception, kept as a history in the member’s profile.',
    labels: ['wellness', 'hardware'],
    launchedDaysAgo: 400,
    price: 8,
    billingPeriod: 'month',
    trialDays: 0,
    plans: { quarterly: 1, monthly: 2.4 },
    popularity: 5,
    defaults: [{ device: 'bia-8-electrode', exportFormat: 'fhir-observation', resultsRetentionMonths: 24 }],
  },
  {
    name: 'kids-club',
    title: 'Kids club',
    description: 'Supervised play for the children while their parents train. Only for members with the family label.',
    labels: ['facility'],
    launchedDaysAgo: 500,
    price: 15,
    billingPeriod: 'month',
    trialDays: 0,
    plans: { 'one-child': 1, siblings: 1.7 },
    popularity: 30,
    defaults: [{ maxMinutesPerVisit: 90, minAgeMonths: 6, maxAgeYears: 10, childrenPerCarer: 6 }],
  },
  {
    name: 'sauna',
    title: 'Sauna',
    description: 'Sauna sessions with a capacity per slot; some slots are for women only.',
    labels: ['wellness'],
    launchedDaysAgo: 450,
    price: 10,
    billingPeriod: 'month',
    trialDays: 7,
    plans: { 'ten-visits': 1, unlimited: 1.8 },
    popularity: 7,
    defaults: [{ slotMinutes: 90, capacity: 12, womenOnlySlots: ['tue 18:00-21:00', 'sat 09:00-12:00'] }],
  },
]

/** The new application of feature-delivery1. */
const aiCoach: App = {
  name: 'ai-coach',
  title: 'AI coach',
  description: 'Workout plans and a coach chat from a language model that knows the club’s classes. **Beta.**',
  labels: ['feature', 'ml'],
  launchedDaysAgo: 20,
  price: 6,
  billingPeriod: 'month',
  trialDays: 30,
  plans: { beta: 1 },
  popularity: 0,
  defaults: [
    {
      model: 'coach-2026-09',
      gateway: {
        endpoint: 'https://llm-gateway.fitness-cloud.fabrikam.example/v1',
        apiKey: '@(ai-coach.gateway-key, primaryVault)',
      },
      features: { workoutPlans: true, chat: true, nutritionAdvice: false },
      medicalDisclaimer: true,
    },
  ],
}

/** The new application of the next release. */
const challenges: App = {
  name: 'challenges',
  title: 'Challenges',
  description: 'Monthly step and class challenges with a leaderboard per club.',
  labels: ['community'],
  launchedDaysAgo: 15,
  price: 3,
  billingPeriod: 'month',
  trialDays: 30,
  plans: { monthly: 1 },
  popularity: 0,
  defaults: [{ leaderboard: 'per-club', rewards: { badges: true, freeGuestPass: true } }],
}

const appOf = (name: string) => [...apps, aiCoach, challenges].find((app) => app.name === name)!

type Currency = 'CZK' | 'EUR' | 'GBP' | 'USD'
type Pool = 'cz' | 'sk' | 'de' | 'en'

interface Tenant {
  name: string
  title: string
  legalName: string
  country: string
  currency: Currency
  supportTier: 'standard' | 'premium' | 'enterprise'
  crmId: string
  description: string
  labels: string[]
  timeZone: string
  locale: string
  /** The cloud region of the tenant's data. */
  region: string
  domain: string
  brandColor: string
  payments: 'adyen' | 'stripe'
  /** Months on the platform. */
  since: number
  /** Generated members; the hand-written ones come on top. */
  roster: number
  /** The districts of the clubs; a member's home club is a label. */
  clubs: string[]
  /** The name pools of the members, weighted. */
  pools: Partial<Record<Pool, number>>
  /** The applications the tenant enabled. */
  apps: string[]
  /** Price level against the list price. */
  positioning: number
  /** The share of members with the family label: parents, whose children go to the kids club where there is one. */
  familyShare: number
}

const tenants: Tenant[] = [
  {
    name: 'fitfactory',
    title: 'FitFactory',
    legalName: 'FitFactory s.r.o.',
    country: 'CZ',
    currency: 'CZK',
    supportTier: 'premium',
    crmId: 'CRM-2101',
    description: 'Prague’s largest chain: **six clubs**, open 24/7 in Karlín and Chodov.',
    labels: ['chain', 'cee'],
    timeZone: 'Europe/Prague',
    locale: 'cs-CZ',
    region: 'polandcentral',
    domain: 'fitfactory.example',
    brandColor: '#e4002b',
    payments: 'adyen',
    since: 30,
    roster: 124,
    clubs: ['karlin', 'vinohrady', 'smichov', 'dejvice', 'holesovice', 'chodov'],
    pools: { cz: 70, sk: 15, de: 8, en: 7 },
    apps: [
      'class-booking',
      'gym-access',
      'nutrition-coach',
      'personal-training',
      'online-classes',
      'spa-access',
      'locker-rental',
      'body-scan',
      'sauna',
    ],
    positioning: 1,
    familyShare: 0.08,
  },
  {
    name: 'pulse-gyms',
    title: 'Pulse Gyms',
    legalName: 'Pulse Gyms GmbH',
    country: 'DE',
    currency: 'EUR',
    supportTier: 'enterprise',
    crmId: 'CRM-2102',
    description: 'Berlin chain with six clubs and a strong personal-training business.',
    labels: ['chain', 'dach'],
    timeZone: 'Europe/Berlin',
    locale: 'de-DE',
    region: 'germanywestcentral',
    domain: 'pulsegyms.example',
    brandColor: '#0057b8',
    payments: 'adyen',
    since: 26,
    roster: 112,
    clubs: ['mitte', 'kreuzberg', 'prenzlauer-berg', 'charlottenburg', 'friedrichshain', 'neukoelln'],
    pools: { de: 82, en: 10, cz: 4, sk: 4 },
    apps: [
      'gym-access',
      'class-booking',
      'personal-training',
      'online-classes',
      'locker-rental',
      'body-scan',
      'sauna',
      'nutrition-coach',
    ],
    positioning: 1.1,
    familyShare: 0.08,
  },
  {
    name: 'yoga-loft',
    title: 'Yoga Loft',
    legalName: 'Yoga Loft Studios Ltd',
    country: 'GB',
    currency: 'GBP',
    supportTier: 'standard',
    crmId: 'CRM-2103',
    description: 'Boutique yoga and pilates studios in London; no gym floor, so no gym access.',
    labels: ['boutique', 'uk'],
    timeZone: 'Europe/London',
    locale: 'en-GB',
    region: 'uksouth',
    domain: 'yogaloft.example',
    brandColor: '#7a5195',
    payments: 'stripe',
    since: 14,
    roster: 66,
    clubs: ['shoreditch', 'camden', 'clapham', 'islington'],
    pools: { en: 85, de: 5, cz: 5, sk: 5 },
    apps: ['class-booking', 'online-classes', 'nutrition-coach', 'personal-training', 'spa-access'],
    positioning: 1.35,
    familyShare: 0.05,
  },
  {
    name: 'aqua-arena',
    title: 'Aqua Arena',
    legalName: 'Aqua Arena a.s.',
    country: 'SK',
    currency: 'EUR',
    supportTier: 'premium',
    crmId: 'CRM-2104',
    description: 'Pools, gyms and a spa in Bratislava.',
    labels: ['chain', 'cee'],
    timeZone: 'Europe/Bratislava',
    locale: 'sk-SK',
    region: 'germanywestcentral',
    domain: 'aqua-arena.example',
    brandColor: '#00a3e0',
    payments: 'adyen',
    since: 20,
    roster: 74,
    clubs: ['petrzalka', 'ruzinov', 'stare-mesto', 'nove-mesto'],
    pools: { sk: 80, cz: 15, en: 5 },
    apps: ['gym-access', 'class-booking', 'spa-access', 'sauna', 'locker-rental', 'kids-club', 'body-scan'],
    positioning: 0.85,
    familyShare: 0.15,
  },
  {
    name: 'ironworks',
    title: 'Ironworks Fitness',
    legalName: 'Ironworks Fitness Inc.',
    country: 'US',
    currency: 'USD',
    supportTier: 'enterprise',
    crmId: 'CRM-2105',
    description: 'Strength-training gyms in Chicago and Evanston.',
    labels: ['chain', 'us'],
    timeZone: 'America/Chicago',
    locale: 'en-US',
    region: 'centralus',
    domain: 'ironworksfitness.example',
    brandColor: '#333f48',
    payments: 'stripe',
    since: 17,
    roster: 92,
    clubs: ['the-loop', 'wicker-park', 'lincoln-park', 'west-loop', 'evanston'],
    pools: { en: 88, de: 6, cz: 3, sk: 3 },
    apps: [
      'gym-access',
      'class-booking',
      'personal-training',
      'nutrition-coach',
      'body-scan',
      'locker-rental',
      'kids-club',
    ],
    positioning: 1.2,
    familyShare: 0.1,
  },
  {
    name: 'family-fit',
    title: 'Family Fit',
    legalName: 'Family Fit GmbH',
    country: 'AT',
    currency: 'EUR',
    supportTier: 'standard',
    crmId: 'CRM-2106',
    description: 'Family clubs in Vienna with a kids club in every location.',
    labels: ['family', 'dach'],
    timeZone: 'Europe/Vienna',
    locale: 'de-AT',
    region: 'westeurope',
    domain: 'familyfit.example',
    brandColor: '#43b02a',
    payments: 'stripe',
    since: 9,
    roster: 58,
    clubs: ['leopoldstadt', 'favoriten', 'donaustadt', 'meidling'],
    pools: { de: 80, cz: 10, sk: 6, en: 4 },
    apps: ['gym-access', 'class-booking', 'kids-club', 'online-classes', 'sauna', 'spa-access', 'nutrition-coach'],
    positioning: 0.95,
    familyShare: 0.4,
  },
]

/** The developers' tenant of the development view. */
const sandboxGym: Tenant = {
  name: 'sandbox-gym',
  title: 'Sandbox Gym',
  legalName: 'Fabrikam a.s. (test tenant)',
  country: 'CZ',
  currency: 'CZK',
  supportTier: 'standard',
  crmId: 'CRM-TEST',
  description: 'The developers’ tenant: payments go to the provider’s test environment, turnstiles are simulated.',
  labels: ['test'],
  timeZone: 'Europe/Prague',
  locale: 'cs-CZ',
  region: 'polandcentral',
  domain: 'sandbox-gym.example',
  brandColor: '#6b7280',
  payments: 'adyen',
  since: 30,
  roster: 0,
  clubs: ['lab'],
  pools: {},
  apps: ['gym-access', 'class-booking', 'nutrition-coach', 'kids-club'],
  positioning: 1,
  familyShare: 0,
}

const tenantOf = (name: string) => tenants.find((tenant) => tenant.name === name)!

/** Enablements that are not plainly active in production. */
const enablements: Record<string, Meta> = {
  'yoga-loft.spa-access': {
    expiresIn: -40,
    createdDaysAgo: 132,
    createdBy: 'barbara',
    labels: ['trial'],
    description: 'Three-month trial with the partner spa next door; not extended.',
  },
  'aqua-arena.kids-club': {
    expiresIn: 71,
    labels: ['notice-given'],
    description: 'The kids’ area closes at the end of the year for the pool extension.',
  },
  'ironworks.kids-club': { disabled: true, description: 'Paused until the new childcare staff start.' },
  'family-fit.sauna': {
    startsIn: 25,
    createdDaysAgo: 6,
    createdBy: 'barbara',
    description: 'The new sauna world in Donaustadt opens on 15 November; members can buy it ahead.',
  },
}
/** The calendar date of an enablement that starts later, for the assignments sold ahead. */
const openings: Record<string, string> = { 'family-fit.sauna': '2026-11-15' }

const sellable = (tenant: string, app: string) => {
  const enablement = enablements[`${tenant}.${app}`]
  return !enablement?.disabled && (enablement?.expiresIn ?? 0) >= 0
}

/** A tenant enables an application when it comes onto the platform, or when the application launches later. */
const enabledDaysAgo = (tenant: Tenant, app: App) =>
  enablements[`${tenant.name}.${app.name}`]?.createdDaysAgo ?? Math.min(tenant.since * 30, app.launchedDaysAgo)

// Configurations hold calendar dates. They are written for the day the dataset was made (21 October 2026), while
// the annotations' own dates (ValidFrom, ExpiresAt, Created) follow the clock.
const currentMonth = 2026 * 12 + 9
const monthOf = (monthsAgo: number) => {
  const month = currentMonth - monthsAgo
  return `${Math.floor(month / 12)}-${String((month % 12) + 1).padStart(2, '0')}`
}
const dateOf = (monthsAgo: number, day: number) => `${monthOf(monthsAgo)}-${String(day).padStart(2, '0')}`
const asOf = Date.parse('2026-10-21')
/** Days from a calendar date to 21 October 2026; negative for a later date. */
const daysSince = (date: string) => Math.round((asOf - Date.parse(date)) / 86_400_000)
/** The calendar date `days` days before 21 October 2026. */
function dateAgo(days: number): string {
  let months = 0
  while (daysSince(dateOf(months, 1)) < days) {
    months++
  }
  return dateOf(months, 1 + daysSince(dateOf(months, 1)) - days)
}
/** Student plans end with the winter semester, on 31 January. */
const semesterEnd = 102
const nextMonth = { startsIn: 11, startDate: '2026-11-01' }

type Tier = 'basic' | 'plus' | 'premium'
type Group = 'student' | 'corporate' | 'family'
type Discount = { percent: number; reason: 'student' | 'corporate' | 'family' | 'loyalty' | 'promo' }

interface Assignment {
  app: string
  plan: string
  startDate: string
  discount?: Discount
  meta?: Meta
  /** Earlier configurations, oldest first. */
  earlier?: { plan: string; startDate: string }[]
  deleted?: { daysAgo: number; by: PersonaId }
}

interface Member {
  tenant: string
  /** The username: first initial and surname, lowercase ASCII. */
  name: string
  fullName: string
  email: string
  /** Year and month the person joined the club, which can be before the club moved to the platform. */
  memberSince: string
  createdDaysAgo: number
  tier: Tier
  groups: Group[]
  club: string
  employer?: string
  /** Facts beyond the e-mail and the member-since. */
  values?: Record<string, unknown>
  /** The member's own settings, over the defaults of the template. */
  preferences?: Record<string, unknown>
  meta?: Meta
  deleted?: { daysAgo: number; by: PersonaId }
  assignments: Assignment[]
}

/** The four members of FitFactory on the board, with their five assignments. */
const boardMembers: Member[] = [
  {
    tenant: 'fitfactory',
    name: 'jnovak',
    fullName: 'Jan Novák',
    email: 'jnovak@posta.example',
    memberSince: '2024-03',
    createdDaysAgo: 900,
    tier: 'plus',
    groups: [],
    club: 'karlin',
    assignments: [{ app: 'class-booking', plan: 'eight-per-month', startDate: '2025-02-03' }],
  },
  {
    tenant: 'fitfactory',
    name: 'kschwarz',
    fullName: 'Klaus Schwarz',
    email: 'kschwarz@postfach.example',
    memberSince: '2025-01',
    createdDaysAgo: 650,
    tier: 'premium',
    groups: ['corporate'],
    club: 'smichov',
    employer: 'Contoso',
    preferences: { locale: 'de-DE' },
    assignments: [
      {
        app: 'class-booking',
        plan: 'unlimited',
        startDate: '2025-01-13',
        discount: { percent: 15, reason: 'corporate' },
      },
    ],
  },
  {
    tenant: 'fitfactory',
    name: 'mdvorak',
    fullName: 'Martin Dvořák',
    email: 'mdvorak@schranka.example',
    memberSince: '2019-09',
    createdDaysAgo: 900,
    tier: 'premium',
    groups: [],
    club: 'vinohrady',
    assignments: [
      { app: 'gym-access', plan: 'all-clubs', startDate: '2024-05-06', discount: { percent: 5, reason: 'loyalty' } },
    ],
  },
  {
    tenant: 'fitfactory',
    name: 'epetrova',
    fullName: 'Elena Petrová',
    email: 'epetrova@mailik.example',
    memberSince: '2025-09',
    createdDaysAgo: 410,
    tier: 'plus',
    groups: ['student'],
    club: 'dejvice',
    preferences: { locale: 'sk-SK', marketingConsent: true },
    assignments: [
      {
        app: 'gym-access',
        plan: 'full',
        startDate: '2025-09-08',
        discount: { percent: 20, reason: 'student' },
        meta: { expiresIn: semesterEnd },
      },
      {
        app: 'nutrition-coach',
        plan: 'coached',
        startDate: '2026-03-02',
        earlier: [{ plan: 'self-guided', startDate: '2025-11-03' }],
      },
    ],
  },
]

/** Members whose story the lifecycle shows. */
const notableMembers: Member[] = [
  ...boardMembers,
  {
    tenant: 'fitfactory',
    name: 'pkucera',
    fullName: 'Petr Kučera',
    email: 'pkucera@posta.example',
    memberSince: '2022-06',
    createdDaysAgo: 900,
    tier: 'plus',
    groups: [],
    club: 'holesovice',
    assignments: [
      {
        app: 'gym-access',
        plan: 'full',
        startDate: '2024-05-13',
        meta: {
          disabled: true,
          description: 'Membership frozen until mid-January after a knee surgery; the turnstiles stay closed.',
          modifiedDaysAgo: 9,
          modifiedBy: 'barbara',
        },
      },
    ],
  },
  {
    tenant: 'fitfactory',
    name: 'tsvobodova',
    fullName: 'Tereza Svobodová',
    email: 'tsvobodova@schranka.example',
    memberSince: '2026-02',
    createdDaysAgo: 250,
    tier: 'basic',
    groups: [],
    club: 'chodov',
    assignments: [
      { app: 'gym-access', plan: 'off-peak', startDate: '2026-02-16' },
      // Booked twice at the front desk; the second booking was removed.
      { app: 'locker-rental', plan: 'small', startDate: '2026-10-01', deleted: { daysAgo: 4, by: 'barbara' } },
    ],
  },
  {
    tenant: 'pulse-gyms',
    name: 'lbecker',
    fullName: 'Lena Becker',
    email: 'lbecker@webpost.example',
    memberSince: '2024-11',
    createdDaysAgo: 720,
    tier: 'plus',
    groups: [],
    club: 'kreuzberg',
    // A data-erasure request (GDPR Art. 17): the member and her assignments wait in Deleted until the purge.
    deleted: { daysAgo: 12, by: 'ada' },
    assignments: [
      { app: 'gym-access', plan: 'full', startDate: '2024-11-04' },
      { app: 'class-booking', plan: 'eight-per-month', startDate: '2025-05-12' },
    ],
  },
  {
    tenant: 'yoga-loft',
    name: 'cwright',
    fullName: 'Chloe Wright',
    email: 'cwright@mailbox.example',
    memberSince: '2025-01',
    createdDaysAgo: 420,
    tier: 'premium',
    groups: [],
    club: 'shoreditch',
    meta: { expiresIn: -51, description: 'Cancelled her membership at the end of August.' },
    assignments: [
      { app: 'class-booking', plan: 'unlimited', startDate: '2025-09-01', meta: { expiresIn: -51 } },
      { app: 'online-classes', plan: 'live-and-on-demand', startDate: '2026-01-05', meta: { expiresIn: -51 } },
    ],
  },
  {
    tenant: 'aqua-arena',
    name: 'zhorvathova',
    fullName: 'Zuzana Horváthová',
    email: 'zhorvathova@mailik.example',
    memberSince: '2023-05',
    createdDaysAgo: 600,
    tier: 'premium',
    groups: [],
    club: 'ruzinov',
    assignments: [
      { app: 'gym-access', plan: 'all-clubs', startDate: '2025-03-03' },
      // Bought ahead: the spa membership starts next month.
      { app: 'spa-access', plan: 'monthly', startDate: nextMonth.startDate, meta: { startsIn: nextMonth.startsIn } },
    ],
  },
]

interface NamePool {
  male: string[]
  female: string[]
  /** Surnames, with the female form after a slash where the language has one. */
  surnames: string[]
  /** Mail providers of the members (fictional). */
  mail: string[]
  locale: string
}

const words = (text: string) => text.trim().split(/\s+/)

const pools: Record<Pool, NamePool> = {
  cz: {
    male: words(
      'Jan Petr Tomáš Martin Jakub Lukáš Ondřej Pavel Jiří David Michal Filip Vojtěch Adam Marek Radek Zdeněk',
    ),
    female: words(
      'Jana Eva Lucie Kateřina Tereza Petra Veronika Markéta Barbora Hana Lenka Zuzana Klára Anna Michaela',
    ),
    surnames: words(`
      Novák/Nováková Svoboda/Svobodová Novotný/Novotná Dvořák/Dvořáková Černý/Černá Procházka/Procházková
      Kučera/Kučerová Veselý/Veselá Horák/Horáková Němec/Němcová Pokorný/Pokorná Pospíšil/Pospíšilová Hájek/Hájková
      Jelínek/Jelínková Král/Králová Růžička/Růžičková Beneš/Benešová Fiala/Fialová Sedláček/Sedláčková
      Doležal/Doležalová Zeman/Zemanová Kolář/Kolářová Navrátil/Navrátilová Čermák/Čermáková Urban/Urbanová
      Vaněk/Vaňková Blažek/Blažková Kříž/Křížová Kovář/Kovářová Bartoš/Bartošová Vlček/Vlčková
    `),
    mail: ['posta.example', 'schranka.example'],
    locale: 'cs-CZ',
  },
  sk: {
    male: words('Peter Martin Michal Juraj Marek Tomáš Lukáš Ján Matej Andrej Róbert Milan Dušan Rastislav Branislav'),
    female: words('Mária Zuzana Katarína Monika Lucia Jana Veronika Ivana Silvia Andrea Dominika Simona Natália Elena'),
    surnames: words(`
      Horváth/Horváthová Kováč/Kováčová Varga/Vargová Tóth/Tóthová Baláž/Balážová Molnár/Molnárová Lukáč/Lukáčová
      Kollár/Kollárová Oravec/Oravcová Šimko/Šimková Hudák/Hudáková Kráľ/Kráľová Polák/Poláková Gajdoš/Gajdošová
      Kubík/Kubíková Mráz/Mrázová Bielik/Bieliková Hruška/Hrušková Sloboda/Slobodová Ďurica/Ďuricová Čierny/Čierna
    `),
    mail: ['mailik.example', 'schranka.example'],
    locale: 'sk-SK',
  },
  de: {
    male: words('Lukas Maximilian Felix Jonas Leon Paul Tobias Florian Sebastian Moritz Niklas Julian Stefan Thomas'),
    female: words('Anna Laura Lea Lena Hannah Sophie Julia Katharina Sarah Lisa Marie Johanna Sabine Claudia Katrin'),
    surnames: words(`
      Müller Schmidt Schneider Fischer Weber Meyer Wagner Becker Schulz Hoffmann Schäfer Koch Bauer Richter Klein
      Wolf Schröder Neumann Schwarz Zimmermann Braun Krüger Hofmann Hartmann Lange Werner Krause Lehmann Huber Kaiser
      Fuchs Brandt Vogel Keller Berger Winkler Lorenz Gruber Steiner Moser
    `),
    mail: ['postfach.example', 'webpost.example'],
    locale: 'de-DE',
  },
  en: {
    male: words('James Oliver Harry Jack George Noah Thomas William Daniel Samuel Benjamin Ethan Michael Ryan Liam'),
    female: words('Olivia Emily Amelia Isla Sophie Grace Chloe Ella Charlotte Jessica Lucy Emma Mia Ruby Megan Rachel'),
    surnames: words(`
      Smith Jones Taylor Brown Williams Wilson Johnson Davies Robinson Wright Thompson Evans Walker White Roberts
      Green Hall Wood Jackson Clarke Patel Khan Lewis Harris Cooper Hughes Edwards Turner Hill Moore Baker Carter
      Mitchell Young Allen King Scott Adams Murphy Garcia Nguyen Kowalski Sullivan
    `),
    mail: ['mailbox.example', 'inbox.example'],
    locale: 'en-GB',
  },
}

const employers = ['Contoso', 'Northwind Traders', 'Adatum', 'Litware', 'Woodgrove Bank', 'Fourth Coffee', 'Proseware']

function weighted<T extends string>(rng: Rng, weights: Partial<Record<T, number>>): T {
  const entries = Object.entries(weights) as [T, number][]
  let roll = rng.next() * entries.reduce((sum, [, weight]) => sum + weight, 0)
  for (const [item, weight] of entries) {
    roll -= weight
    if (roll < 0) {
      return item
    }
  }
  return entries.at(-1)![0]
}

/** "Dvořák" → "dvorak", "Müller" → "mueller". */
const ascii = (text: string) =>
  text
    .toLowerCase()
    .replaceAll('ä', 'ae')
    .replaceAll('ö', 'oe')
    .replaceAll('ü', 'ue')
    .replaceAll('ß', 'ss')
    .normalize('NFD')
    .replaceAll(/\p{M}/gu, '')
    .replaceAll(/[^a-z]/g, '')

/** First initial and surname; a clash gets a letter (jnovak, jnovakb, jnovakc), never a number. */
function usernameOf(first: string, surname: string, taken: Set<string>): string {
  const base = ascii(first.slice(0, 1)) + ascii(surname)
  let name = base
  for (let suffix = 1; taken.has(name); suffix++) {
    name = base + 'bcdefghijklmnopqrstuvwxyz'.charAt(suffix - 1)
  }
  taken.add(name)
  return name
}

/** A basic member often uses the club with the plastic card only, so without any app assignment. */
const assignmentCount = (rng: Rng, tier: Tier) =>
  tier === 'basic' ? (rng.chance(0.3) ? 1 : 0) : tier === 'plus' ? (rng.chance(0.85) ? 1 : 2) : rng.pick([1, 1, 2, 3])

function discountOf(rng: Rng, groups: Group[], app: string, months: number): Discount | undefined {
  if (groups.includes('student') && app !== 'kids-club') {
    return { percent: 20, reason: 'student' }
  }
  if (groups.includes('corporate')) {
    return { percent: rng.pick([10, 15]), reason: 'corporate' }
  }
  if (groups.includes('family') && app === 'kids-club') {
    return { percent: 10, reason: 'family' }
  }
  if (months >= 60 && rng.chance(0.5)) {
    return { percent: 5, reason: 'loyalty' }
  }
  return rng.chance(0.04) ? { percent: 25, reason: 'promo' } : undefined
}

/** The applications a studio without turnstiles is entered with: its members book every visit in the app. */
const entryApps = ['class-booking', 'online-classes']

function generatedAssignments(
  rng: Rng,
  tenant: Tenant,
  member: { tier: Tier; groups: Group[]; months: number; createdDaysAgo: number; expiresIn?: number },
): Assignment[] {
  // A member whose membership ended cannot buy ahead an application that opens later.
  const candidates = tenant.apps.filter(
    (app) =>
      sellable(tenant.name, app) &&
      (app !== 'kids-club' || member.groups.includes('family')) &&
      !(member.expiresIn !== undefined && enablements[`${tenant.name}.${app}`]?.startsIn),
  )
  // Parents book the kids club in the app, so they have it first; without turnstiles, a class app comes first.
  const entry = tenant.apps.includes('gym-access') ? [] : candidates.filter((app) => entryApps.includes(app))
  const picked = member.groups.includes('family') && candidates.includes('kids-club') ? ['kids-club'] : []
  const needsApp = picked.length > 0 || entry.length > 0
  const count = Math.max(needsApp ? 1 : 0, assignmentCount(rng, member.tier))
  while (picked.length < Math.min(count, candidates.length)) {
    const from = picked.length === 0 && entry.length ? entry : candidates
    const left = from.filter((app) => !picked.includes(app))
    picked.push(weighted(rng, Object.fromEntries(left.map((app) => [app, appOf(app).popularity]))))
  }

  const level = { basic: 0, plus: 1, premium: 2 }[member.tier]
  return picked.map((app) => {
    const plans = Object.keys(appOf(app).plans)
    const index = Math.round((level * (plans.length - 1)) / 2) + rng.int(-1, 1)
    const plan = plans[Math.min(plans.length - 1, Math.max(0, index))]!
    // An assignment starts once the member and the tenant's enablement exist, and before the membership ended.
    const oldest = Math.min(member.createdDaysAgo, enabledDaysAgo(tenant, appOf(app)))
    const newest = Math.min(oldest, member.expiresIn === undefined ? 1 : 1 - member.expiresIn)
    let startDate = dateAgo(rng.int(newest, oldest))
    const discount = discountOf(rng, member.groups, app, member.months)

    const meta: Meta = {}
    const enablement = enablements[`${tenant.name}.${app}`]
    if (enablement?.startsIn) {
      meta.startsIn = enablement.startsIn
      startDate = openings[`${tenant.name}.${app}`]!
    } else if (member.expiresIn === undefined && rng.chance(0.03)) {
      meta.startsIn = nextMonth.startsIn
      startDate = nextMonth.startDate
    }
    if (rng.chance(0.015)) {
      meta.disabled = true
      meta.description = 'Membership frozen at the member’s request.'
    }
    const ends = [
      member.expiresIn,
      discount?.reason === 'student' ? semesterEnd : undefined,
      enablement?.expiresIn,
    ].filter((days) => days !== undefined)
    if (ends.length) {
      meta.expiresIn = Math.min(...ends)
    }
    return { app, plan, startDate, discount, meta }
  })
}

function preferencesOf(rng: Rng, tenant: Tenant, pool: NamePool): Record<string, unknown> | undefined {
  const preferences: Record<string, unknown> = {}
  if (pool.locale.slice(0, 2) !== tenant.locale.slice(0, 2) && rng.chance(0.7)) {
    preferences.locale = pool.locale
  }
  if (rng.chance(0.12)) {
    preferences.pushNotifications = false
  }
  if (rng.chance(0.1)) {
    preferences.marketingConsent = true
  }
  if (rng.chance(0.05)) {
    preferences.weeklySummary = false
  }
  return Object.keys(preferences).length ? preferences : undefined
}

function generatedMember(rng: Rng, tenant: Tenant, taken: Set<string>): Member {
  const pool = pools[weighted(rng, tenant.pools)]
  const female = rng.chance(0.52)
  const first = rng.pick(female ? pool.female : pool.male)
  const [male, femaleForm = male] = rng.pick(pool.surnames).split('/') as [string, string?]
  const surname = female ? femaleForm : male
  const name = usernameOf(first, surname, taken)
  // Members who joined before the club moved to the platform were imported on the day it did.
  const months = rng.int(0, tenant.since + 30)
  const createdDaysAgo = Math.min(months * 30 + rng.int(0, 20), tenant.since * 30)

  const tier = weighted<Tier>(rng, { basic: 55, plus: 32, premium: 13 })
  const groups: Group[] = []
  if (tier !== 'premium' && rng.chance(0.14)) {
    groups.push('student')
  } else if (rng.chance(0.12)) {
    groups.push('corporate')
  }
  if (rng.chance(tenant.familyShare)) {
    groups.push('family')
  }
  // A cancelled membership ended at least a month after it was created.
  const expiresIn =
    rng.chance(0.045) && createdDaysAgo > 40 ? -rng.int(3, Math.min(160, createdDaysAgo - 30)) : undefined

  return {
    tenant: tenant.name,
    name,
    fullName: `${first} ${surname}`,
    email: `${name}@${rng.pick(pool.mail)}`,
    memberSince: monthOf(months),
    createdDaysAgo,
    tier,
    groups,
    club: rng.pick(tenant.clubs),
    employer: groups.includes('corporate') ? rng.pick(employers) : undefined,
    preferences: preferencesOf(rng, tenant, pool),
    meta: expiresIn === undefined ? undefined : { expiresIn },
    assignments: generatedAssignments(rng, tenant, { tier, groups, months, createdDaysAgo, expiresIn }),
  }
}

let roster: Map<string, Member[]> | undefined

/**
 * The members of a tenant: the hand-written ones, then the generated ones. Each tenant has a PRNG and usernames of
 * its own (the key holds the tenant), so every view that shows a tenant shows the same members.
 */
function membersOf(tenant: Tenant): Member[] {
  if (!roster) {
    roster = new Map(
      tenants.map((candidate) => {
        const rng = seededRng(`fitness-cloud/members/${candidate.name}`)
        const notable = notableMembers.filter((member) => member.tenant === candidate.name)
        const taken = new Set(notable.map((member) => member.name))
        return [
          candidate.name,
          [...notable, ...Array.from({ length: candidate.roster }, () => generatedMember(rng, candidate, taken))],
        ]
      }),
    )
  }
  return roster.get(tenant.name) ?? []
}

const rates: Record<Currency, number> = { CZK: 25, EUR: 1, GBP: 0.86, USD: 1.08 }

/** A price in the tenant's currency, rounded the way gyms print them: 729 CZK, 31.99 EUR. */
function localPrice(eur: number, currency: Currency): number {
  const amount = eur * rates[currency]
  return currency === 'CZK'
    ? Math.max(1, Math.round(amount / 10)) * 10 - 1
    : Number((Math.max(1, Math.round(amount)) - 0.01).toFixed(2))
}

/** The 2027 prices of the release candidate: 6 % more, from 1 January. */
const nextPrices = { raise: 1.06, validFrom: '2027-01-01' }

/** The tenant's price list of one application; the currency comes from the tenant's values. */
function priceList(tenant: Tenant, app: App, next = false): Record<string, unknown> {
  const raise = next ? nextPrices.raise : 1
  const price = (factor: number) => localPrice(app.price * tenant.positioning * factor * raise, tenant.currency)
  return {
    price: price(1),
    currency: '@annotation("/currency", "Subject")',
    billingPeriod: app.billingPeriod,
    trialDays: app.trialDays,
    planPrices: Object.fromEntries(Object.entries(app.plans).map(([plan, factor]) => [plan, price(factor)])),
    ...(next ? { validFrom: nextPrices.validFrom } : {}),
  }
}

// FitFactory's gym access price list of 2026, a version every two to three weeks since January: the all-clubs plan,
// the off-peak hours, campaigns without a joining fee or with a month's trial, the currency moved to the tenant's
// values and the one price rise of the year, on 1 October. The release candidate brings the 2027 prices.
const gymAccessChanges: Record<number, Record<string, unknown>> = {
  2: { joiningFee: 300 },
  3: { planPrices: { 'all-clubs': 920 } },
  4: { trialDays: 14 },
  5: { annualPrepayDiscountPercent: 10 },
  // The spring campaign: no joining fee until the end of May.
  6: { joiningFee: 0 },
  7: { offPeakHours: { weekdays: '06:00-15:00', weekends: 'all-day' } },
  8: { joiningFee: 300 },
  // The summer campaign: a month's trial until the end of August.
  9: { trialDays: 30 },
  10: { annualPrepayDiscountPercent: 12 },
  11: { currency: '@annotation("/currency", "Subject")' },
  12: { offPeakHours: { weekdays: '06:00-16:00' } },
  13: { trialDays: 14 },
  // The autumn campaign, again without a joining fee.
  14: { joiningFee: 0 },
  15: { price: 690, planPrices: { 'off-peak': 520, full: 690, 'all-clubs': 950 } },
  16: { trialDays: 7 },
  17: { price: 729, planPrices: { 'off-peak': 549, full: 729, 'all-clubs': 990 }, validFrom: nextPrices.validFrom },
}

const gymAccessPriceList = (next: boolean) =>
  evolve(
    {
      price: 650,
      currency: 'CZK',
      billingPeriod: 'month',
      trialDays: 7,
      planPrices: { 'off-peak': 490, full: 650 },
    },
    next ? 17 : 16,
    (version) => gymAccessChanges[version] ?? {},
  )

// Fabrikam's support edits the members and their assignments for the tenants; the account managers keep the tenants
// and their price lists, the product team the applications.
const support: PersonaId[] = ['barbara', 'barbara', 'barbara', 'ada', 'margaret']
const accountManagers: PersonaId[] = ['ada', 'margaret']
const productTeam: PersonaId[] = ['margaret', 'ada']

/**
 * Who wrote a document and when, seeded by its key so that every view shows the same history: the newest version
 * between `newest` and `oldest` days ago, and at most 25 days ago, so that it keeps its content.
 */
function historyOf(key: string, authors: readonly PersonaId[], oldest: number, newest = 1) {
  const rng = seededRng(`fitness-cloud/history/${key}`)
  const from = Math.min(newest, oldest)
  return {
    lastDaysAgo: rng.int(from, Math.max(from, Math.min(25, oldest))),
    authors: [rng.pick(authors), rng.pick(authors)],
  }
}

function addApplications(b: ScopeBuilder, list: readonly App[], environment: Environment, next = false) {
  for (const app of list) {
    const key = b.responsibility(app.name, {
      title: app.title,
      description: app.description,
      labels: app.labels,
      link: `${docs}/apps/${app.name}`,
      createdDaysAgo: app.launchedDaysAgo,
      createdBy: 'margaret',
    })
    // Developers log everything and simulate the turnstiles, lockers and scanners.
    const debugging =
      environment === 'dev'
        ? { logLevel: 'debug', ...(app.labels.includes('hardware') ? { simulateHardware: true } : {}) }
        : {}
    const versions = (environment === 'production' ? app.defaults : app.defaults.slice(-1)).map(
      (defaults): Record<string, unknown> => ({ endpoint: apiOf(app.name, environment), ...defaults, ...debugging }),
    )
    if (next && app.name === 'class-booking') {
      // Release 2026.11: the waitlist offers a freed place to the member most likely to come.
      versions.push({ ...versions.at(-1)!, smartWaitlist: true })
    }
    b.configure(key, versions, { ...historyOf(key, productTeam, app.launchedDaysAgo), every: 45 })
  }
}

/** The templates and schemas of the product, for the applications of the view. */
function productDocuments(b: ScopeBuilder, list: readonly App[], next: boolean) {
  b.template(
    'exe',
    [
      { autoRenew: true, gracePeriodDays: 7, notifications: { channels: ['email'], renewalReminderDays: 3 } },
      { autoRenew: true, gracePeriodDays: 7, notifications: { channels: ['email', 'push'], renewalReminderDays: 3 } },
      {
        autoRenew: true,
        gracePeriodDays: 7,
        notifications: {
          // The member's address from the member's values, the reply-to from the tenant's configuration.
          email: '@annotation("/email", "Context")',
          replyTo: '@config("/supportEmail")',
          channels: ['email', 'push'],
          renewalReminderDays: 3,
        },
      },
    ],
    { every: 40 },
  )
  b.template('cnt', { pushNotifications: true, marketingConsent: false, weeklySummary: true })

  // A tenant cannot enable an application without a price; the release tightens the billing period and the trial.
  const priceSchema = (strict: boolean) => ({
    type: 'object',
    properties: {
      price: { type: 'number', exclusiveMinimum: 0 },
      currency: { type: 'string', pattern: '^[A-Z]{3}$' },
      billingPeriod: { enum: ['month', 'session'] },
      trialDays: { type: 'integer', minimum: 0, maximum: strict ? 30 : 60 },
      planPrices: { type: 'object', additionalProperties: { type: 'number', exclusiveMinimum: 0 } },
    },
    required: strict ? ['price', 'currency', 'billingPeriod'] : ['price', 'currency'],
  })
  b.typeSchema('usg', next ? [priceSchema(false), priceSchema(true)] : priceSchema(false), { every: 30 })

  // Every assignment names a plan; each application narrows the plans to its own (the enum array is replaced).
  b.typeSchema('exe', {
    type: 'object',
    properties: {
      plan: { type: 'string', enum: [...new Set(list.flatMap((app) => Object.keys(app.plans)))] },
      startDate: { type: 'string', format: 'date' },
      discount: {
        type: 'object',
        properties: {
          percent: { type: 'integer', minimum: 5, maximum: 50 },
          reason: { enum: ['student', 'corporate', 'family', 'loyalty', 'promo'] },
        },
        required: ['percent', 'reason'],
      },
    },
    required: ['plan'],
  })
  for (const app of list) {
    b.descendantSchema(`rst.${app.name}`, 'exe', {
      type: 'object',
      properties: { plan: { enum: Object.keys(app.plans) } },
    })
  }
}

function tenantConfiguration(tenant: Tenant, environment: Environment): Record<string, unknown> {
  const live = environment === 'production'
  // Outside production, payments go to the provider's test environment, with a webhook secret of their own.
  const secret = `${tenant.name}.${tenant.payments}-webhook${live ? '' : `-${environment}`}`
  return {
    region: tenant.region,
    locale: tenant.locale,
    units: tenant.country === 'US' ? 'imperial' : 'metric',
    supportEmail: `support@${tenant.domain}`,
    appHost: `${tenant.name}.${live ? '' : `${environment}.`}fitness-cloud.fabrikam.example`,
    branding: { appName: tenant.title, primaryColor: tenant.brandColor },
    payments: {
      provider: tenant.payments,
      ...(live ? {} : { environment: 'test' }),
      webhookSecret: `@(${secret}, primaryVault)`,
    },
    ...(environment === 'dev' ? { logLevel: 'debug' } : {}),
  }
}

/** The tenant, its configuration and its enablements of the given applications (all of the tenant's by default). */
function addTenant(
  b: ScopeBuilder,
  tenant: Tenant,
  environment: Environment,
  options: { apps?: readonly string[]; next?: boolean } = {},
) {
  const live = environment === 'production'
  const next = !!options.next
  const key = b.subject(tenant.name, {
    title: tenant.title,
    description: tenant.description,
    link: `${docs}/tenants/${tenant.name}`,
    labels: tenant.labels,
    values: {
      legalName: tenant.legalName,
      country: tenant.country,
      currency: tenant.currency,
      supportTier: tenant.supportTier,
      crmId: tenant.crmId,
    },
    timeZone: tenant.timeZone,
    createdDaysAgo: tenant.since * 30,
    createdBy: 'margaret',
  })
  const configuration = tenantConfiguration(tenant, environment)
  const customDomain = { ...configuration, customDomain: 'app.fitfactory.example' }
  // FitFactory moved its app to its own domain, then turned on guest passes.
  const versions =
    live && tenant.name === 'fitfactory'
      ? [configuration, customDomain, { ...customDomain, features: { guestPasses: true } }]
      : [configuration]
  b.configure(key, versions, { ...historyOf(key, accountManagers, tenant.since * 30), every: 60 })

  for (const name of options.apps ?? tenant.apps) {
    const app = appOf(name)
    const createdDaysAgo = enabledDaysAgo(tenant, app)
    const usage = b.usage(tenant.name, name, {
      title: `${app.title} at ${tenant.title}`,
      timeZone: tenant.timeZone,
      createdDaysAgo,
      createdBy: 'margaret',
      ...(live ? enablements[`${tenant.name}.${name}`] : {}),
    })
    const history = historyOf(usage, accountManagers, createdDaysAgo)
    if (live && usage === 'usg.fitfactory.gym-access') {
      b.configure(usage, gymAccessPriceList(next), { every: 18, lastDaysAgo: next ? 1 : 4, authors: accountManagers })
    } else if (next) {
      // The release candidate adds the 2027 prices as a new version, written yesterday over production's price list.
      b.configure(usage, [priceList(tenant, app), priceList(tenant, app, true)], {
        authors: [history.authors[0]!, 'ada'],
        every: Math.max(1, history.lastDaysAgo - 1),
        lastDaysAgo: 1,
      })
    } else {
      b.configure(usage, priceList(tenant, app), history)
    }
  }
}

/** Nothing is written to a member or an assignment after it went to Deleted. */
const untilDeleted = (deleted?: { daysAgo: number }) => (deleted ? deleted.daysAgo + 1 : 1)

/** The members, their preferences and their assignments; deletions only where the view seeds them. */
function addMembers(b: ScopeBuilder, tenant: Tenant, members: readonly Member[], withDeletions: boolean) {
  for (const member of members) {
    if (member.deleted && !withDeletions) {
      continue
    }
    const key = b.context(tenant.name, member.name, {
      title: member.fullName,
      labels: [member.tier, ...member.groups, `club-${member.club}`],
      values: {
        email: member.email,
        memberSince: member.memberSince,
        ...(member.employer ? { employer: member.employer } : {}),
        ...member.values,
      },
      timeZone: tenant.timeZone,
      createdDaysAgo: member.createdDaysAgo,
      ...member.meta,
    })
    if (member.preferences) {
      b.configure(key, member.preferences, historyOf(key, support, member.createdDaysAgo, untilDeleted(member.deleted)))
    }

    for (const assignment of member.assignments) {
      if ((assignment.deleted && !withDeletions) || !b.has(`usg.${tenant.name}.${assignment.app}`)) {
        continue
      }
      const app = appOf(assignment.app)
      const { plan, startDate, discount, earlier = [] } = assignment
      // Entered when it first started, but not before the member and the tenant's enablement.
      const createdDaysAgo = Math.max(
        0,
        Math.min(daysSince(earlier[0]?.startDate ?? startDate), member.createdDaysAgo, enabledDaysAgo(tenant, app)),
      )
      const execution = b.execution(tenant.name, app.name, member.name, {
        title: `${app.title} for ${member.fullName}`,
        timeZone: tenant.timeZone,
        createdDaysAgo,
        ...assignment.meta,
      })
      b.configure(execution, [...earlier, { plan, startDate, ...(discount ? { discount } : {}) }], {
        ...historyOf(execution, support, createdDaysAgo, untilDeleted(assignment.deleted ?? member.deleted)),
        every: 30,
      })
      if (assignment.deleted) {
        b.trash(execution, assignment.deleted)
      }
    }
    if (member.deleted) {
      b.trash(key, member.deleted)
    }
  }
}

/** Production, or the release candidate: FitFactory and Pulse Gyms with the 2027 prices and the challenges app. */
function production(b: ScopeBuilder, next: boolean) {
  const list = next ? [...apps, challenges] : apps
  addApplications(b, list, 'production', next)
  productDocuments(b, list, next)
  for (const tenant of next ? [tenantOf('fitfactory'), tenantOf('pulse-gyms')] : tenants) {
    addTenant(b, tenant, 'production', { next })
    addMembers(b, tenant, membersOf(tenant), !next)
  }
  if (next) {
    // Pulse Gyms launches the challenges with the release, on today's prices; no member has them yet.
    const usage = b.usage('pulse-gyms', 'challenges', {
      title: 'Challenges at Pulse Gyms',
      timeZone: 'Europe/Berlin',
      startsIn: 10,
      createdDaysAgo: 5,
      createdBy: 'ada',
    })
    b.configure(usage, priceList(tenantOf('pulse-gyms'), challenges), historyOf(usage, accountManagers, 5))
  }
}

const testMember = (name: string, fields: Partial<Member> & Pick<Member, 'assignments'>): Member => ({
  tenant: 'fitfactory',
  name,
  fullName: name.replace(/^test-member(\d)$/, 'Test member $1').replace(/^test-(\w+)$/, 'Test $1'),
  email: `${name}@qa.fitness-cloud.fabrikam.example`,
  memberSince: '2026-01',
  createdDaysAgo: 290,
  tier: 'plus',
  groups: [],
  club: 'karlin',
  ...fields,
})

/** The board's staging members, plus the edge cases the testers keep: a student, a frozen and a renewing member. */
const stagingMembers: Member[] = [
  testMember('test-member1', {
    assignments: [
      { app: 'gym-access', plan: 'full', startDate: '2026-01-05' },
      { app: 'nutrition-coach', plan: 'self-guided', startDate: '2026-01-05' },
    ],
  }),
  testMember('test-member2', {
    assignments: [{ app: 'nutrition-coach', plan: 'coached', startDate: '2026-02-02' }],
  }),
  testMember('test-student', {
    groups: ['student'],
    assignments: [
      {
        app: 'gym-access',
        plan: 'off-peak',
        startDate: '2026-09-01',
        discount: { percent: 20, reason: 'student' },
        meta: { expiresIn: semesterEnd },
      },
    ],
  }),
  testMember('test-frozen', {
    assignments: [
      {
        app: 'gym-access',
        plan: 'full',
        startDate: '2026-03-02',
        meta: { disabled: true, description: 'Tests the frozen-membership flow.' },
      },
    ],
  }),
  testMember('test-renewal', {
    memberSince: '2025-10',
    createdDaysAgo: 365,
    tier: 'premium',
    assignments: [
      {
        app: 'nutrition-coach',
        plan: 'coached',
        startDate: '2025-10-23',
        meta: { expiresIn: 2, description: 'Renews in two days: tests the renewal reminder.' },
      },
    ],
  }),
]

/** The board's staging view, extended with a few test members. */
function staging(b: ScopeBuilder) {
  const list = apps.filter((app) => ['gym-access', 'nutrition-coach'].includes(app.name))
  addApplications(b, list, 'staging')
  productDocuments(b, list, false)
  addTenant(b, tenantOf('fitfactory'), 'staging', { apps: list.map((app) => app.name) })
  addMembers(b, tenantOf('fitfactory'), stagingMembers, false)
}

const developer = (name: string, fields: Partial<Member> & Pick<Member, 'assignments'>): Member =>
  testMember(name, {
    tenant: 'sandbox-gym',
    email: `${name}@dev.fabrikam.example`,
    club: 'lab',
    values: { testAccount: true },
    ...fields,
  })

/** Five test members of the sandbox gym, one of them the turnstile rig of the Prague lab. */
const developmentMembers: Member[] = [
  developer('dev-member', {
    fullName: 'Developer member',
    assignments: [
      { app: 'gym-access', plan: 'full', startDate: '2026-01-05' },
      { app: 'class-booking', plan: 'unlimited', startDate: '2026-01-05' },
    ],
  }),
  developer('qa-basic', {
    fullName: 'QA basic member',
    tier: 'basic',
    assignments: [{ app: 'class-booking', plan: 'four-per-month', startDate: '2026-03-02' }],
  }),
  developer('qa-premium', {
    fullName: 'QA premium member',
    tier: 'premium',
    preferences: { locale: 'en-GB', weeklySummary: false },
    assignments: [
      { app: 'gym-access', plan: 'all-clubs', startDate: '2026-03-02' },
      { app: 'class-booking', plan: 'unlimited', startDate: '2026-03-02' },
      { app: 'nutrition-coach', plan: 'coached', startDate: '2026-03-02' },
    ],
  }),
  developer('qa-family', {
    fullName: 'QA family member',
    groups: ['family'],
    assignments: [
      { app: 'gym-access', plan: 'full', startDate: '2026-04-01' },
      { app: 'kids-club', plan: 'siblings', startDate: '2026-04-01', discount: { percent: 10, reason: 'family' } },
    ],
  }),
  developer('turnstile-rig', {
    fullName: 'Turnstile rig (Prague lab)',
    email: 'lab-prague@fabrikam.example',
    values: { testAccount: true, serialNumber: 'TS-PRG-0042', owner: 'Barbara Liskov' },
    preferences: { pushNotifications: false },
    assignments: [{ app: 'gym-access', plan: 'off-peak', startDate: '2026-02-09' }],
  }),
]

function development(b: ScopeBuilder) {
  const list = apps.filter((app) => sandboxGym.apps.includes(app.name))
  addApplications(b, list, 'dev')
  productDocuments(b, list, false)
  addTenant(b, sandboxGym, 'dev')
  addMembers(b, sandboxGym, developmentMembers, false)
}

/** The AI coach beta for FitFactory, on the board's slice: two of its members test it. */
function featureDelivery(b: ScopeBuilder) {
  const list = [...apps.filter((app) => ['class-booking', 'gym-access', 'nutrition-coach'].includes(app.name)), aiCoach]
  addApplications(b, list, 'feature1')
  productDocuments(b, list, false)
  addTenant(b, tenantOf('fitfactory'), 'feature1', { apps: list.map((app) => app.name) })
  const testers: Record<string, string> = { epetrova: '2026-10-01', jnovak: '2026-10-12' }
  const members = boardMembers.map((member) => {
    const startDate = testers[member.name]
    return startDate
      ? {
          ...member,
          assignments: [
            ...member.assignments,
            { app: 'ai-coach', plan: 'beta', startDate, meta: { labels: ['beta-tester'] } },
          ],
        }
      : member
  })
  addMembers(b, tenantOf('fitfactory'), members, false)
}

export const fitnessCloud: ShowcaseProject = {
  org: 'fabrikam',
  project: 'fitness-cloud',
  description:
    'Multi-tenant platform for gym chains. Tenants enable applications and assign them to their members, the end customers of our customers.',
  members: { margaret: 'Owner', ada: 'Administrator', barbara: 'Contributor', annie: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Application', color: 'violet', icon: 'i-lucide-layout-grid' },
      sbt: { label: 'Tenant', color: 'blue', icon: 'i-lucide-building-2' },
      usg: { label: 'App enablement', color: 'amber', icon: 'i-lucide-zap' },
      cnt: { label: 'Member', color: 'teal', icon: 'i-lucide-user' },
      exe: { label: 'App assignment', color: 'emerald', icon: 'i-lucide-link' },
    },
    defaultTimeZone: 'Europe/Prague',
    dateFormat: 'dd.mm.yyyy',
  },
  views: environmentViews({
    development: 'Development: the sandbox gym and five test members',
    staging: 'Staging: FitFactory’s test members on gym access and nutrition coach',
    'release-candidate': 'Release 2026.11: the 2027 prices of FitFactory and Pulse Gyms, challenges',
    'feature-delivery1': 'AI coach beta at FitFactory',
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
