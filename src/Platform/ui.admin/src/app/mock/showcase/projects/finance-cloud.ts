import type { Lifecycle, ScopeBuilder } from '../builder'
import { evolve } from '../history'
import { people, type PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// Example 1 of the Miro board, a B2B SaaS without multi-tenancy. Fabrikam sells a finance back-office suite; the
// product is single-tenant, so every customer gets instances of its own (demo, production, UAT, training). An
// application the customer bought is a usage, the application activated in one of its instances an AppRun, and a job
// of the application scheduled in that instance a JobRun. Contoso is the customer drawn on the board; eleven more
// make the catalog a product team of this size would have.

const docs = 'https://docs.finance-cloud.fabrikam.example'
const opsMail = 'finance-cloud-ops@fabrikam.example'

/** Production runs patches of 2026.9; the release candidate brings this release to every running instance. */
const nextRelease = '2026.10.0'

const isProduction = (instance: string) => instance.startsWith('production')

const hostname = (customer: string, instance: string) =>
  `${instance === 'production' ? customer : `${customer}-${instance}`}.finance-cloud.fabrikam.example`

type CountryCode = 'AT' | 'AU' | 'CA' | 'CH' | 'GB' | 'IE' | 'NL' | 'SG' | 'US'

interface Country {
  currency: string
  locale: string
  /** Where the customer's data has to stay. */
  residency: string
  /** The tax engine invoicing calls for the country. */
  taxProvider: 'avalara' | 'fonoa' | 'sovos' | 'vertex'
  /** The open-banking aggregator bank sync imports the statements through. */
  aggregator: 'basiq' | 'plaid' | 'tink' | 'truelayer'
  /** The mileage allowance expense claims pay, in the country's currency. */
  mileage: { unit: 'km' | 'mi'; rate: number }
  /**
   * Where tax filing submits the returns, in the countries Fabrikam files for: the return's name and the CRON of a
   * quarterly filer, some days before the deadline in the month after the quarter.
   */
  filing?: { authority: string; scheme: string; title: string; quarterlyCron: string }
}

const countries: Record<CountryCode, Country> = {
  AT: {
    currency: 'EUR',
    locale: 'de-AT',
    residency: 'eu',
    taxProvider: 'fonoa',
    aggregator: 'tink',
    mileage: { unit: 'km', rate: 0.5 },
  },
  AU: {
    currency: 'AUD',
    locale: 'en-AU',
    residency: 'au',
    taxProvider: 'avalara',
    aggregator: 'basiq',
    mileage: { unit: 'km', rate: 0.88 },
    // The quarterly BAS is due on the 28th of the month after the quarter (February for the December quarter).
    filing: { authority: 'ato', scheme: 'bas', title: 'BAS submission', quarterlyCron: '0 9 21 1,4,7,10 *' },
  },
  CA: {
    currency: 'CAD',
    locale: 'en-CA',
    residency: 'ca',
    taxProvider: 'vertex',
    aggregator: 'plaid',
    mileage: { unit: 'km', rate: 0.72 },
  },
  CH: {
    currency: 'CHF',
    locale: 'de-CH',
    residency: 'ch',
    taxProvider: 'fonoa',
    aggregator: 'tink',
    mileage: { unit: 'km', rate: 0.7 },
  },
  GB: {
    currency: 'GBP',
    locale: 'en-GB',
    residency: 'uk',
    taxProvider: 'sovos',
    aggregator: 'truelayer',
    mileage: { unit: 'mi', rate: 0.45 },
    // Making Tax Digital: one month and seven days after the end of the VAT quarter.
    filing: { authority: 'hmrc', scheme: 'mtd-vat', title: 'VAT submission', quarterlyCron: '0 10 5 2,5,8,11 *' },
  },
  IE: {
    currency: 'EUR',
    locale: 'en-IE',
    residency: 'eu',
    taxProvider: 'fonoa',
    aggregator: 'tink',
    mileage: { unit: 'km', rate: 0.43 },
  },
  NL: {
    currency: 'EUR',
    locale: 'nl-NL',
    residency: 'eu',
    taxProvider: 'fonoa',
    aggregator: 'tink',
    mileage: { unit: 'km', rate: 0.23 },
    filing: {
      authority: 'belastingdienst',
      scheme: 'vat-return',
      title: 'VAT submission',
      quarterlyCron: '0 9 20 1,4,7,10 *',
    },
  },
  SG: {
    currency: 'SGD',
    locale: 'en-SG',
    residency: 'sg',
    taxProvider: 'avalara',
    aggregator: 'plaid',
    mileage: { unit: 'km', rate: 0.6 },
  },
  US: {
    currency: 'USD',
    locale: 'en-US',
    residency: 'us',
    taxProvider: 'avalara',
    aggregator: 'plaid',
    mileage: { unit: 'mi', rate: 0.7 },
  },
}

/** The Azure regions Fabrikam runs instances in; an instance outside them cannot be saved. */
const cloudRegions = [
  'westeurope',
  'northeurope',
  'uksouth',
  'switzerlandnorth',
  'germanywestcentral',
  'eastus',
  'eastus2',
  'centralus',
  'westus2',
  'canadacentral',
  'australiasoutheast',
  'southeastasia',
]

interface Application {
  name: string
  title: string
  labels: string[]
  /** Markdown. */
  description: string
  /** Days since the application shipped. */
  shipped: number
}

const applications: Application[] = [
  {
    name: 'invoicing',
    title: 'Invoicing',
    labels: ['core'],
    shipped: 900,
    description:
      'Issues invoices and credit notes and numbers them per legal entity.\n\n- PDF and e-invoices (PEPPOL BIS 3, XRechnung, Factur-X)\n- Tax calculated by the **tax provider** of each AppRun',
  },
  {
    name: 'payments',
    title: 'Payments',
    labels: ['core', 'pci'],
    shipped: 900,
    description: 'Collects card and bank payments through the payment provider and matches them to open invoices.',
  },
  {
    name: 'reporting',
    title: 'Reporting',
    labels: ['core'],
    shipped: 820,
    description: 'Revenue, aging and cash-flow reports, refreshed by the nightly job.',
  },
  {
    name: 'expenses',
    title: 'Expenses',
    labels: ['add-on'],
    shipped: 520,
    description: 'Expense claims with receipt OCR and approval chains.',
  },
  {
    name: 'payroll',
    title: 'Payroll',
    labels: ['add-on', 'pii'],
    shipped: 460,
    description: 'Payslips, payroll journals and the filings of the payroll country.',
  },
  {
    name: 'tax-filing',
    title: 'Tax filing',
    labels: ['add-on', 'compliance'],
    shipped: 380,
    description: 'Prepares the VAT or GST return of a closed period and submits it to the tax authority.',
  },
  {
    name: 'bank-sync',
    title: 'Bank sync',
    labels: ['integration'],
    shipped: 330,
    description: 'Imports bank statements through an open-banking aggregator.',
  },
  {
    name: 'customer-portal',
    title: 'Customer portal',
    labels: ['web'],
    shipped: 270,
    description: 'Where the customer’s own clients download their invoices and pay them online.',
  },
]

/** The application of feature-delivery1, not released yet. */
const aiReconciliation: Application = {
  name: 'ai-reconciliation',
  title: 'AI reconciliation',
  labels: ['ml', 'preview'],
  shipped: 40,
  description:
    'Matches imported bank lines to open invoices with a model.\n\nMatches above the **auto-match** confidence are booked; the others wait in the review queue.',
}

interface Job {
  application: string
  name: string
  title: string
  unitType: 'time' | 'event'
  /** The default schedule (a CRON in the instance's time zone) or the event that starts the job. */
  definition: string
  /** Where the larger customers move the schedule: out of their business hours, earlier, or more often. */
  tunedCron?: string
  batchSize: number
  timeoutMinutes: number
  description: string
}

/** The jobs of the applications that need one. */
const jobs: Job[] = [
  {
    application: 'reporting',
    name: 'nightly',
    title: 'Nightly reports',
    unitType: 'time',
    definition: '0 2 * * *',
    tunedCron: '30 3 * * *',
    batchSize: 2000,
    timeoutMinutes: 60,
    description: 'Refreshes the report snapshots of the previous day.',
  },
  {
    application: 'invoicing',
    name: 'dunning-run',
    title: 'Dunning run',
    unitType: 'time',
    definition: '0 7 * * 1-5',
    tunedCron: '0 6 * * 1-5',
    batchSize: 500,
    timeoutMinutes: 30,
    description: 'Sends the reminders of overdue invoices on business days.',
  },
  {
    application: 'payments',
    name: 'settlement-export',
    title: 'Settlement export',
    unitType: 'time',
    definition: '30 22 * * *',
    tunedCron: '45 23 * * *',
    batchSize: 5000,
    timeoutMinutes: 45,
    description: 'Exports the settlements of the day to the general ledger.',
  },
  {
    application: 'bank-sync',
    name: 'statement-import',
    title: 'Statement import',
    unitType: 'time',
    definition: '0 */4 * * *',
    tunedCron: '15 */2 * * *',
    batchSize: 1000,
    timeoutMinutes: 20,
    description: 'Imports the new statement lines of every connected bank account.',
  },
  {
    application: 'payroll',
    name: 'payslip-run',
    title: 'Payslip run',
    unitType: 'time',
    definition: '0 6 25 * *',
    tunedCron: '0 5 24 * *',
    batchSize: 250,
    timeoutMinutes: 180,
    description: 'Computes and publishes the payslips of the month.',
  },
  {
    application: 'tax-filing',
    name: 'vat-submission',
    title: 'Tax return submission',
    unitType: 'time',
    // Monthly filers submit on the 7th; a JobRun of a quarterly filer follows the filing calendar of its country.
    definition: '0 9 7 * *',
    batchSize: 20,
    timeoutMinutes: 30,
    description: 'Submits the VAT or GST return of the closed period (a BAS in Australia).',
  },
]

/** The job of the new application: it starts whenever bank sync has imported a statement. */
const matchRun: Job = {
  application: 'ai-reconciliation',
  name: 'match-run',
  title: 'Match run',
  unitType: 'event',
  definition: 'bank-sync.statement-imported',
  batchSize: 500,
  timeoutMinutes: 15,
  description: 'Proposes a match for every new statement line.',
}

const applicationNames = applications.map((application) => application.name)
const applicationOf = (name: string) => [...applications, aiReconciliation].find((entry) => entry.name === name)!
const jobOf = (path: string) => [...jobs, matchRun].find((job) => `${job.application}.${job.name}` === path)!
/** The jobs of the applications, `<application>.<job>`. */
const jobsOf = (names: readonly string[]) =>
  [...jobs, matchRun].filter((job) => names.includes(job.application)).map((job) => `${job.application}.${job.name}`)

type Segment = 'smb' | 'mid-market' | 'enterprise'

/** A deletion of the default view: what production deleted is in Deleted there and gone from the other views. */
interface Deletion {
  daysAgo: number
  by: PersonaId
}

interface Instance {
  name: string
  /** The applications activated in the instance; a demo runs fewer than the customer bought. */
  applications: string[]
  /** The jobs scheduled in it, `<application>.<job>`. */
  jobs?: string[]
  /** Jobs of the list deleted on their own, by `<application>.<job>`. */
  deletedJobs?: Record<string, Deletion>
  /** Applications switched off in the instance for now, with the reason. */
  disabled?: Record<string, string>
  /** The country of the legal entity the instance serves; the customer's by default. */
  country?: CountryCode
  /** The customer's cloud region and time zone by default. */
  region?: string
  timeZone?: string
  /** Applies to the instance, its AppRuns and its JobRuns, in production and in the release candidate. */
  lifecycle?: Lifecycle
  description?: string
  link?: string
  /** Days since the instance was provisioned; the customer's start by default. */
  createdDaysAgo?: number
  /** The version it runs; the customer's patch of 2026.9 for production, the newest patch otherwise. */
  productVersion?: string
  /** Earlier versions of the instance's document, as changes of the current one, oldest first. */
  history?: Record<string, unknown>[]
}

interface Customer {
  name: string
  title: string
  legalName: string
  segment: Segment
  region: 'emea' | 'americas' | 'apac'
  country: CountryCode
  supportTier: 'standard' | 'premium' | 'enterprise'
  crmId: string
  accountManager: PersonaId
  timeZone: string
  cloudRegion: string
  /** MM-DD. */
  fiscalYearStart: string
  /** The customer's finance staff; an enterprise contract licenses a share of them per application. */
  users: number
  /** The applications the customer bought, in the order of their sales orders. */
  applications: string[]
  instances: Instance[]
  /** Where the payroll runs, when not in the customer's country. */
  payrollCountry?: CountryCode
  /** Contracts that end in this many days (notice given). */
  notice?: Record<string, number>
  /** Users bought on top of a list edition, contract change by contract change, oldest first. */
  seats?: Record<string, number[]>
  /** Days since the customer was onboarded (the trial for Litware). */
  since: number
  /** The patch of 2026.9 its production instances run. */
  patch: number
  /** The customer churned: in Deleted with its instances, usages, AppRuns and JobRuns. */
  deleted?: Deletion
  description?: string
}

const customers: Customer[] = [
  {
    // The board's customer: demo and production; the demo runs invoicing only, the JobRun is production reporting.
    name: 'contoso',
    title: 'Contoso',
    legalName: 'Contoso B.V.',
    segment: 'mid-market',
    region: 'emea',
    country: 'NL',
    supportTier: 'premium',
    crmId: 'CRM-10017',
    accountManager: 'grace',
    timeZone: 'Europe/Amsterdam',
    cloudRegion: 'westeurope',
    fiscalYearStart: '01-01',
    users: 60,
    applications: ['invoicing', 'payments', 'reporting'],
    seats: { invoicing: [25, 40, 60] },
    instances: [
      { name: 'demo', applications: ['invoicing'] },
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'reporting'],
        jobs: ['reporting.nightly'],
        createdDaysAgo: 610,
        // Three upgrades since the summer, and a larger database when the reporting load grew.
        history: [
          { productVersion: '2026.6.2', databaseTier: 'general-purpose-2' },
          { productVersion: '2026.7.1', databaseTier: 'general-purpose-2' },
          { productVersion: '2026.8.0' },
        ],
      },
    ],
    since: 640,
    patch: 3,
  },
  {
    name: 'tailspin-toys',
    title: 'Tailspin Toys',
    legalName: 'Tailspin Toys, Inc.',
    segment: 'smb',
    region: 'americas',
    country: 'US',
    supportTier: 'standard',
    crmId: 'CRM-10102',
    accountManager: 'alan',
    timeZone: 'America/Chicago',
    cloudRegion: 'centralus',
    fiscalYearStart: '01-01',
    users: 12,
    applications: ['invoicing', 'payments', 'customer-portal'],
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'customer-portal'],
        jobs: ['invoicing.dunning-run', 'payments.settlement-export'],
      },
      { name: 'demo', applications: ['invoicing'] },
    ],
    since: 410,
    patch: 4,
  },
  {
    name: 'wide-world-importers',
    title: 'Wide World Importers',
    legalName: 'Wide World Importers, Inc.',
    segment: 'enterprise',
    region: 'americas',
    country: 'US',
    supportTier: 'enterprise',
    crmId: 'CRM-10055',
    accountManager: 'grace',
    timeZone: 'America/Los_Angeles',
    cloudRegion: 'westus2',
    fiscalYearStart: '07-01',
    users: 240,
    applications: ['invoicing', 'payments', 'reporting', 'bank-sync', 'customer-portal'],
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'reporting', 'bank-sync', 'customer-portal'],
        jobs: [
          'invoicing.dunning-run',
          'payments.settlement-export',
          'reporting.nightly',
          'bank-sync.statement-import',
        ],
      },
      {
        name: 'uat',
        applications: ['invoicing', 'bank-sync'],
        disabled: {
          'bank-sync': 'Off until the test accounts are re-linked after the bank’s OAuth migration.',
        },
      },
      { name: 'training', applications: ['invoicing', 'customer-portal'] },
    ],
    since: 720,
    patch: 2,
  },
  {
    name: 'adventure-works',
    title: 'Adventure Works',
    legalName: 'Adventure Works Cycles, Inc.',
    segment: 'enterprise',
    region: 'americas',
    country: 'US',
    supportTier: 'enterprise',
    crmId: 'CRM-10031',
    accountManager: 'margaret',
    timeZone: 'America/Los_Angeles',
    cloudRegion: 'westus2',
    fiscalYearStart: '07-01',
    users: 420,
    applications: ['invoicing', 'payments', 'reporting', 'expenses', 'payroll', 'bank-sync'],
    description:
      'Two production instances: **EU** for the Dutch subsidiary, **US** for the head office. Payroll runs for the Dutch employees only.',
    instances: [
      {
        name: 'production-eu',
        applications: ['invoicing', 'payments', 'reporting', 'expenses', 'payroll'],
        jobs: ['reporting.nightly', 'payments.settlement-export', 'payroll.payslip-run'],
        country: 'NL',
        region: 'westeurope',
        timeZone: 'Europe/Amsterdam',
      },
      {
        name: 'production-us',
        applications: ['invoicing', 'payments', 'reporting', 'expenses', 'bank-sync'],
        jobs: ['reporting.nightly', 'payments.settlement-export', 'bank-sync.statement-import'],
      },
      {
        name: 'uat',
        applications: ['invoicing', 'payroll'],
        country: 'NL',
        region: 'westeurope',
        timeZone: 'Europe/Amsterdam',
      },
      { name: 'demo', applications: ['invoicing', 'expenses'] },
    ],
    payrollCountry: 'NL',
    since: 860,
    patch: 3,
  },
  {
    name: 'litware',
    title: 'Litware',
    legalName: 'Litware, Inc.',
    segment: 'smb',
    region: 'americas',
    country: 'US',
    supportTier: 'standard',
    crmId: 'CRM-10233',
    accountManager: 'alan',
    timeZone: 'America/New_York',
    cloudRegion: 'eastus',
    fiscalYearStart: '01-01',
    users: 8,
    applications: ['invoicing', 'expenses'],
    instances: [
      {
        // The trial ended when Litware signed; it stays in the catalog, expired, until it is cleaned up.
        name: 'trial',
        applications: ['invoicing', 'expenses'],
        lifecycle: { expiresIn: -20 },
        productVersion: '2026.8.3',
        description: 'Fifty-day trial; ended when Litware signed for production.',
      },
      {
        name: 'production',
        applications: ['invoicing', 'expenses'],
        jobs: ['invoicing.dunning-run'],
        createdDaysAgo: 25,
      },
    ],
    since: 70,
    patch: 4,
  },
  {
    name: 'coho-winery',
    title: 'Coho Winery',
    legalName: 'Coho Winery Pty Ltd',
    segment: 'smb',
    region: 'apac',
    country: 'AU',
    supportTier: 'standard',
    crmId: 'CRM-10178',
    accountManager: 'margaret',
    timeZone: 'Australia/Adelaide',
    cloudRegion: 'australiasoutheast',
    fiscalYearStart: '07-01',
    users: 15,
    applications: ['invoicing', 'payments', 'tax-filing'],
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'tax-filing'],
        jobs: ['tax-filing.vat-submission'],
      },
      { name: 'demo', applications: ['invoicing'] },
    ],
    since: 300,
    patch: 2,
  },
  {
    name: 'proseware',
    title: 'Proseware',
    legalName: 'Proseware, Inc.',
    segment: 'mid-market',
    region: 'americas',
    country: 'CA',
    supportTier: 'premium',
    crmId: 'CRM-10140',
    accountManager: 'grace',
    timeZone: 'America/Toronto',
    cloudRegion: 'canadacentral',
    fiscalYearStart: '04-01',
    users: 75,
    applications: ['invoicing', 'expenses', 'payroll', 'reporting'],
    // Proseware moves payroll to a Canadian provider when the contract ends.
    notice: { payroll: 45 },
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'expenses', 'payroll', 'reporting'],
        jobs: ['payroll.payslip-run', 'reporting.nightly'],
      },
      { name: 'uat', applications: ['payroll'] },
    ],
    since: 480,
    patch: 3,
  },
  {
    name: 'lucerne-publishing',
    title: 'Lucerne Publishing',
    legalName: 'Lucerne Publishing AG',
    segment: 'mid-market',
    region: 'emea',
    country: 'CH',
    supportTier: 'premium',
    crmId: 'CRM-10066',
    accountManager: 'ada',
    timeZone: 'Europe/Zurich',
    cloudRegion: 'switzerlandnorth',
    fiscalYearStart: '01-01',
    users: 90,
    applications: ['invoicing', 'payments', 'reporting', 'customer-portal'],
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'reporting', 'customer-portal'],
        jobs: ['reporting.nightly', 'payments.settlement-export'],
        // Lucerne settles through its bank's own file transfer now; its settlement export was deleted on its own.
        deletedJobs: { 'payments.settlement-export': { daysAgo: 9, by: 'margaret' } },
      },
      { name: 'uat', applications: ['invoicing', 'customer-portal'] },
      { name: 'training', applications: ['invoicing'] },
    ],
    since: 560,
    patch: 4,
  },
  {
    // Churned at the end of its contract: in Deleted with its instances, usages, AppRuns and JobRuns.
    name: 'margies-travel',
    title: 'Margie’s Travel',
    legalName: 'Margie’s Travel Ltd.',
    segment: 'smb',
    region: 'emea',
    country: 'IE',
    supportTier: 'standard',
    crmId: 'CRM-10121',
    accountManager: 'alan',
    timeZone: 'Europe/Dublin',
    cloudRegion: 'northeurope',
    fiscalYearStart: '01-01',
    users: 10,
    applications: ['invoicing', 'payments', 'expenses'],
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'expenses'],
        jobs: ['invoicing.dunning-run'],
      },
      { name: 'demo', applications: ['invoicing'] },
    ],
    since: 520,
    patch: 1,
    deleted: { daysAgo: 10, by: 'grace' },
  },
  {
    name: 'alpine-ski-house',
    title: 'Alpine Ski House',
    legalName: 'Alpine Ski House GmbH',
    segment: 'smb',
    region: 'emea',
    country: 'AT',
    supportTier: 'standard',
    crmId: 'CRM-10199',
    accountManager: 'margaret',
    timeZone: 'Europe/Vienna',
    cloudRegion: 'germanywestcentral',
    fiscalYearStart: '05-01',
    users: 20,
    applications: ['invoicing', 'payments', 'payroll'],
    instances: [
      { name: 'production', applications: ['invoicing', 'payments', 'payroll'], jobs: ['payroll.payslip-run'] },
      // Where the seasonal staff learn the payroll self-service before the winter season.
      { name: 'training', applications: ['payroll'] },
    ],
    since: 350,
    patch: 2,
  },
  {
    // Signed this summer: UAT and training run, production goes live in twelve days.
    name: 'woodgrove-bank',
    title: 'Woodgrove Bank',
    legalName: 'Woodgrove Bank plc',
    segment: 'enterprise',
    region: 'emea',
    country: 'GB',
    supportTier: 'enterprise',
    crmId: 'CRM-10260',
    accountManager: 'grace',
    timeZone: 'Europe/London',
    cloudRegion: 'uksouth',
    fiscalYearStart: '01-01',
    users: 600,
    applications: ['invoicing', 'payments', 'reporting', 'tax-filing', 'bank-sync', 'customer-portal'],
    description:
      'Regulated customer: a change to **production** needs an approved change ticket (outsourcing rules of the PRA).',
    instances: [
      {
        name: 'uat',
        applications: ['invoicing', 'payments', 'tax-filing', 'bank-sync'],
        // Test returns go to HMRC's sandbox.
        jobs: ['tax-filing.vat-submission'],
        createdDaysAgo: 80,
      },
      { name: 'training', applications: ['invoicing', 'customer-portal'], createdDaysAgo: 40 },
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'reporting', 'tax-filing', 'bank-sync', 'customer-portal'],
        jobs: [
          'invoicing.dunning-run',
          'payments.settlement-export',
          'reporting.nightly',
          'tax-filing.vat-submission',
          'bank-sync.statement-import',
        ],
        lifecycle: { startsIn: 12 },
        createdDaysAgo: 6,
        history: [],
        description:
          'Goes live after the UAT sign-off:\n\n1. Freeze UAT\n2. Migrate the opening balances\n3. Switch the bank feeds\n\nSee the [go-live runbook](https://docs.finance-cloud.fabrikam.example/runbooks/go-live).',
        link: `${docs}/runbooks/go-live`,
      },
    ],
    since: 95,
    patch: 4,
  },
  {
    // Bought the customer portal too; it is activated after the payments migration.
    name: 'fourth-coffee',
    title: 'Fourth Coffee',
    legalName: 'Fourth Coffee Pte. Ltd.',
    segment: 'mid-market',
    region: 'apac',
    country: 'SG',
    supportTier: 'premium',
    crmId: 'CRM-10211',
    accountManager: 'ada',
    timeZone: 'Asia/Singapore',
    cloudRegion: 'southeastasia',
    fiscalYearStart: '04-01',
    users: 55,
    applications: ['invoicing', 'payments', 'expenses', 'reporting', 'customer-portal'],
    instances: [
      {
        name: 'production',
        applications: ['invoicing', 'payments', 'expenses', 'reporting'],
        jobs: ['reporting.nightly', 'payments.settlement-export'],
      },
      { name: 'demo', applications: ['invoicing', 'expenses'] },
    ],
    since: 270,
    patch: 3,
  },
]

const customerOf = (name: string) => customers.find((customer) => customer.name === name)!

const instanceKinds: Record<string, { title: string; label: string }> = {
  production: { title: 'production', label: 'production' },
  'production-eu': { title: 'production EU', label: 'production' },
  'production-us': { title: 'production US', label: 'production' },
  uat: { title: 'UAT', label: 'test' },
  demo: { title: 'demo', label: 'demo' },
  training: { title: 'training', label: 'training' },
  trial: { title: 'trial', label: 'trial' },
  dev: { title: 'dev', label: 'test' },
  staging: { title: 'staging', label: 'test' },
  'staging-perf': { title: 'performance staging', label: 'test' },
}

/** What differs between the views that show the same customers. */
interface Stage {
  /** The default view and the release candidate: histories and lifecycle. */
  production?: boolean
  /**
   * Release 2026.10.0: its product defaults, AppRun template and instance schema, and in the release candidate the
   * upgrade of every running instance. Staging and the feature delivery run a build of it or of a later release.
   */
  next?: boolean
  /** The build every instance of a pre-production view runs. */
  productVersion?: string
}

/** The default view keeps what production deleted in Deleted; the other views do not have it. */
const keepsDeleted = (stage: Stage) => !!stage.production && !stage.next

/** Days between the versions of a document, so that the oldest one is not older than its annotation. */
const spacing = (span: number, count: number, every: number) =>
  count > 1 ? Math.max(1, Math.min(every, Math.floor(span / (count - 1)))) : every

/**
 * When a document was last edited: within the last weeks and after its annotation was created, or shortly before the
 * day it was frozen (the customer deleted, the instance expired).
 */
const editedDaysAgo = (b: ScopeBuilder, createdDaysAgo: number, frozenDaysAgo?: number) =>
  frozenDaysAgo === undefined
    ? Math.min(b.rng.int(2, 25), Math.max(1, createdDaysAgo - 1))
    : frozenDaysAgo + b.rng.int(2, 5)

/** Whether a document holds a secret of the vault: only Margaret may write those. */
const holdsSecret = (document: Record<string, unknown>) => JSON.stringify(document).includes(', primaryVault)')

// Invoicing's product defaults, changed patch by patch over the last five months: the long history of the project.
// The last change is the one release 2026.10.0 brings, so the release candidate has one version more.
const invoicingChanges: Record<string, unknown>[] = [
  { pdfTemplate: 'classic-2025.2' },
  { features: { recurringInvoices: true } },
  { numbering: { format: 'INV-{yyyy}-{seq:7}' } },
  { features: { partialPayments: true } },
  { pdfTemplate: 'modern-2026' },
  { features: { eInvoicing: true }, eInvoiceFormats: ['peppol-bis-3'] },
  { eInvoiceFormats: ['peppol-bis-3', 'xrechnung'] },
  { pdfTemplate: 'modern-2026.1' },
  { reminders: { firstAfterDays: 7 } },
  { features: { swissQrBill: true } },
  { eInvoiceFormats: ['peppol-bis-3', 'xrechnung', 'factur-x'] },
  { reminders: { secondAfterDays: 21 } },
  { pdfTemplate: 'modern-2026.2' },
  { features: { multiCurrency: true } },
  { eInvoiceFormats: ['peppol-bis-3', 'xrechnung', 'factur-x', 'ubl-2.1'] },
  { pdfTemplate: 'modern-2026.3' },
  { roundingMode: 'half-even' },
  // 2026.10.0: Poland's KSeF e-invoices and credit notes without approval below a limit.
  {
    eInvoiceFormats: ['peppol-bis-3', 'xrechnung', 'factur-x', 'ubl-2.1', 'ksef'],
    features: { instantCreditNotes: true },
  },
]

// An invoice goes out in the customer's base currency unless the AppRun names the currency of its legal entity.
const invoicingVersions = (next: boolean) =>
  evolve(
    {
      // An instance that does not name its tax engine calculates against the shared sandbox, never a live one.
      taxProvider: 'avalara',
      taxProviderEnvironment: 'sandbox',
      paymentTermsDays: 30,
      numbering: { format: 'INV-{yyyy}-{seq:6}', resetYearly: true },
      pdfTemplate: 'classic-2025',
      roundingMode: 'half-up',
      features: { creditNotes: true, recurringInvoices: false, partialPayments: false, eInvoicing: false },
    },
    invoicingChanges.length + (next ? 1 : 0),
    (version) => invoicingChanges[version - 2]!,
  )

/** The product defaults of the other applications, oldest first. */
const productDefaults: Record<string, Record<string, unknown>[]> = {
  payments: [
    { providers: ['adyen'], captureDelayHours: 24, threeDSecure: 'required', refundWindowDays: 180 },
    { providers: ['adyen', 'stripe'], captureDelayHours: 24, threeDSecure: 'required', refundWindowDays: 180 },
    { providers: ['adyen', 'stripe'], captureDelayHours: 0, threeDSecure: 'required', refundWindowDays: 180 },
  ],
  reporting: [{ snapshotRetentionDays: 400, exportFormats: ['xlsx', 'pdf', 'csv'], agingBuckets: [30, 60, 90] }],
  // The mileage allowance is the country's; every expenses AppRun names it.
  expenses: [{ receiptOcr: true, approvalLimit: 1000, mileageClaims: true }],
  payroll: [{ payslipFormat: 'pdf', retentionYears: 7, approvalsRequired: 2 }],
  'tax-filing': [
    { reviewBeforeSubmit: true, reminderDaysBefore: 5 },
    { reviewBeforeSubmit: true, reminderDaysBefore: 7, attachLedgerExtract: true },
  ],
  'bank-sync': [{ historyDays: 90, consentRenewalDays: 90, matchByReference: true }],
  'customer-portal': [
    { theme: 'light', sessionTimeoutMinutes: 30, languages: ['en', 'de', 'fr', 'nl'], payOnline: true },
  ],
  // Out of the box it only proposes matches; an AppRun turns auto-matching on above a confidence of its own.
  'ai-reconciliation': [
    { model: 'recon-match-2026-09', proposeAbove: 0.8, autoMatch: false, reviewQueue: 'reconciliation-review' },
  ],
}

/** Every AppRun inherits logging, retries and telemetry; the last version comes with release 2026.10.0. */
const appRunDefaults = evolve(
  {
    logLevel: 'info',
    retries: 3,
    telemetry: { endpoint: 'https://otel.finance-cloud.fabrikam.example:4318', sampling: 0.25 },
  },
  4,
  (version) =>
    [
      { telemetry: { sampling: 0.1 } },
      { telemetry: { endpoint: 'https://otel-collector.ops.fabrikam.example:4318' } },
      { logFormat: 'json', telemetry: { sampling: 0.05 } },
    ][version - 2]!,
)

/** The developers' defaults: verbose logs and every trace, sent to the development collector. */
const developerDefaults = {
  logLevel: 'debug',
  retries: 0,
  telemetry: { endpoint: 'https://otel.dev.finance-cloud.fabrikam.example:4318', sampling: 1 },
}

// An instance cannot be saved without its region and host name; release 2026.10.0 also needs the version, so an
// instance never runs an unknown build after the upgrade.
const instanceSchema = {
  type: 'object',
  properties: {
    hostname: { type: 'string', pattern: '^[a-z0-9-]+(\\.[a-z0-9-]+)+$' },
    region: { enum: cloudRegions },
    productVersion: { type: 'string', pattern: '^\\d{4}\\.\\d{1,2}\\.\\d+' },
    databaseTier: {
      enum: ['standard-s0', 'standard-s2', 'general-purpose-2', 'general-purpose-4', 'business-critical-8'],
    },
  },
  required: ['region', 'hostname'],
}
const releaseInstanceSchema = { ...instanceSchema, required: ['region', 'hostname', 'productVersion'] }

// Invoicing cannot run without a tax engine; Sovos joined the list when Woodgrove Bank signed.
const taxProviderSchemas = [
  {
    type: 'object',
    properties: {
      taxProvider: { enum: ['avalara', 'fonoa', 'vertex'] },
      taxProviderEnvironment: { enum: ['production', 'sandbox'] },
    },
    required: ['taxProvider'],
  },
  {
    type: 'object',
    properties: {
      taxProvider: { enum: ['avalara', 'fonoa', 'sovos', 'vertex'] },
      taxProviderEnvironment: { enum: ['production', 'sandbox'] },
    },
    required: ['taxProvider'],
  },
]

/** The AppRun template and the schemas every view has; views other than production keep the latest versions. */
function rules(b: ScopeBuilder, stage: Stage, template: Record<string, unknown>[] = appRunDefaults.slice(0, 3)) {
  const latest = <T>(versions: T[]) => (stage.production ? versions : versions.slice(-1))
  b.template('exe', latest(stage.next ? appRunDefaults : template), { every: 20, lastDaysAgo: stage.next ? 1 : 2 })
  b.typeSchema('cnt', latest(stage.next ? [instanceSchema, releaseInstanceSchema] : [instanceSchema]), { every: 30 })
  b.descendantSchema('rst.invoicing', 'exe', latest(taxProviderSchemas), { every: 60, lastDaysAgo: 10 })
}

/** The applications and their jobs (all of them by default), with the product defaults. */
function addApplications(b: ScopeBuilder, names: readonly string[], stage: Stage, jobPaths = jobsOf(names)) {
  for (const name of names) {
    const application = applicationOf(name)
    b.responsibility(name, {
      title: application.title,
      description: application.description,
      labels: application.labels,
      link: `${docs}/applications/${name}`,
      createdDaysAgo: application.shipped,
    })
    const versions = name === 'invoicing' ? invoicingVersions(!!stage.next) : productDefaults[name]!
    b.configure(`rst.${name}`, stage.production ? versions : versions.slice(-1), {
      every: name === 'invoicing' ? 9 : 40,
      lastDaysAgo: stage.next ? 1 : 3,
    })
  }
  for (const job of jobPaths.map(jobOf)) {
    b.unit(job.application, job.name, {
      unitType: job.unitType,
      definition: job.definition,
      title: job.title,
      description: job.description,
      link: `${docs}/jobs/${job.application}/${job.name}`,
      createdDaysAgo: applicationOf(job.application).shipped - 30,
    })
    const document = { batchSize: job.batchSize, timeoutMinutes: job.timeoutMinutes, notifyOnFailure: [opsMail] }
    // The nightly reports got more time when the aging report moved into the run.
    const versions =
      stage.production && job.name === 'nightly' ? [{ ...document, timeoutMinutes: 45 }, document] : [document]
    b.configure(`unt.${job.application}.${job.name}`, versions, { every: 25, lastDaysAgo: 4 })
  }
}

function instanceDocument(customer: Customer, instance: Instance, stage: Stage): Record<string, unknown> {
  const live = isProduction(instance.name)
  const tier =
    customer.segment === 'enterprise'
      ? 'business-critical-8'
      : customer.segment === 'mid-market'
        ? 'general-purpose-4'
        : 'general-purpose-2'
  const abroad = instance.country && instance.country !== customer.country ? instance.country : undefined
  return {
    hostname: hostname(customer.name, instance.name),
    region: instance.region ?? customer.cloudRegion,
    productVersion: stage.productVersion ?? instance.productVersion ?? `2026.9.${live ? customer.patch : 4}`,
    databaseTier: instance.name === 'dev' ? 'standard-s0' : live ? tier : 'standard-s2',
    // The instance of a legal entity abroad keeps its data where that entity's rules say.
    ...(abroad ? { dataResidency: countries[abroad].residency } : {}),
    // The developers' instance logs everything and sends every mail to a catch-all inbox.
    ...(instance.name === 'dev'
      ? { logLevel: 'debug', mailSink: 'https://mail.dev.finance-cloud.fabrikam.example' }
      : {}),
  }
}

/** Production keeps the upgrades of an instance; the release candidate upgrades every running one once more. */
function instanceVersions(customer: Customer, instance: Instance, stage: Stage): Record<string, unknown>[] {
  const current = instanceDocument(customer, instance, stage)
  if (!stage.production) {
    return [current]
  }
  const earlier = instance.history ?? (isProduction(instance.name) ? [{ productVersion: '2026.8.5' }] : [])
  const retired = (instance.lifecycle?.expiresIn ?? 0) < 0
  const upgrade = stage.next && !retired ? [{ ...current, productVersion: nextRelease }] : []
  return [...earlier.map((change) => ({ ...current, ...change })), current, ...upgrade]
}

/** The share of an enterprise's finance staff licensed per application; few people file the tax returns. */
const licensedShare: Record<string, number> = {
  invoicing: 0.5,
  payments: 0.25,
  reporting: 0.4,
  expenses: 1,
  payroll: 0.05,
  'tax-filing': 0.02,
  'bank-sync': 0.1,
  'ai-reconciliation': 0.05,
}

/** At least three users; contracts of more than twenty are rounded to fives. */
const licensedUsers = (users: number) => (users < 20 ? Math.max(3, Math.round(users)) : Math.round(users / 5) * 5)

/**
 * Enterprise contracts are negotiated per application, the customer portal by the invoices it serves to the
 * customer's own clients; the others buy a list edition, documented only for users bought on top.
 */
function contractVersions(customer: Customer, application: string, stage: Stage): Record<string, unknown>[] {
  const crmAccount = '@annotation("/crmId", "Subject")'
  if (customer.segment === 'enterprise') {
    if (applicationOf(application).labels.includes('preview')) {
      // The pilot is free of charge until the application is released.
      return [{ edition: 'pilot', users: licensedUsers(customer.users * licensedShare[application]!), crmAccount }]
    }
    const volume =
      application === 'customer-portal'
        ? { invoicesPerMonth: customer.users * 50 }
        : { users: licensedUsers(customer.users * licensedShare[application]!) }
    return [{ edition: 'enterprise', ...volume, billingCycle: 'annual', crmAccount }]
  }
  const seats = customer.seats?.[application] ?? []
  return (stage.production ? seats : seats.slice(-1)).map((users) => ({
    edition: 'professional',
    users,
    billingCycle: 'monthly',
    crmAccount,
  }))
}

/** The sales order of an application: numbered in the order the customer bought them, the pilot's after them. */
function salesOrder(customer: Customer, application: string) {
  const bought = (customers.find((entry) => entry.name === customer.name) ?? customer).applications
  const position = [...bought, aiReconciliation.name].indexOf(application) + 1
  return `SO-${customer.crmId.slice(4)}-${String(position).padStart(2, '0')}`
}

/** Where the customer rehearses its own setup: against its country's providers, in their sandboxes. */
const rehearses = (instance: string) => ['uat', 'training', 'staging'].includes(instance)

/** The settings of an application in one instance; outside production the AppRuns call the providers' sandboxes. */
function appRunDocument(
  customer: Customer,
  instance: Instance,
  application: string,
): Record<string, unknown> | undefined {
  const code = instance.country ?? customer.country
  const country = countries[code]
  const live = isProduction(instance.name)
  const secret = (name: string) => `@(${customer.name}.${name}, primaryVault)`
  switch (application) {
    case 'invoicing':
      // Production and the customer's test instances name the tax engine of the legal entity's country, the test
      // instances in its sandbox; demos, trials and the developers' instance keep the product's shared sandbox.
      return live || rehearses(instance.name)
        ? {
            taxProvider: country.taxProvider,
            taxProviderEnvironment: live ? 'production' : 'sandbox',
            taxProviderKey: secret(`${country.taxProvider}-${live ? 'api' : 'sandbox'}-key`),
            invoiceSeries: `${live ? 'INV' : 'TST'}-${code}`,
            invoiceCurrency: country.currency,
          }
        : undefined
    case 'payments': {
      const provider = customer.region === 'emea' ? 'adyen' : 'stripe'
      return {
        provider,
        merchantAccount: `${customer.title.replace(/[^A-Za-z0-9]/g, '')}${live ? code : 'TEST'}`,
        apiKey: secret(`${provider}-${live ? 'live' : 'test'}-key`),
        // The provider calls back on the instance's own host name.
        callbackHost: '@config("/hostname")',
        settlementCurrency: country.currency,
      }
    }
    case 'expenses':
      return { mileage: country.mileage }
    case 'tax-filing':
      return country.filing
        ? {
            authority: country.filing.authority,
            scheme: country.filing.scheme,
            ...(live ? {} : { authorityEnvironment: 'sandbox' }),
            filingFrequency: 'quarterly',
            country: '@annotation("/country", "Subject")',
            credentials: secret(`${country.filing.authority}-client-secret`),
          }
        : undefined
    case 'bank-sync':
      return {
        aggregator: country.aggregator,
        ...(live ? {} : { aggregatorEnvironment: 'sandbox' }),
        clientSecret: secret(`${country.aggregator}-client-secret`),
      }
    case 'customer-portal':
      return live
        ? {
            customDomain: `billing.${customer.name}.example`,
            sso: customer.segment === 'enterprise' ? 'saml' : 'none',
            languages: country.locale.startsWith('en') ? ['en'] : [country.locale.slice(0, 2), 'en'],
          }
        : undefined
    case 'payroll': {
      const payrollCountry = customer.payrollCountry ?? code
      return {
        payrollCountry,
        // Canadian employees are paid on the 15th and the last day of the month; Austrian ones get a 13th and a
        // 14th salary.
        payFrequency: payrollCountry === 'CA' ? 'semi-monthly' : 'monthly',
        ...(payrollCountry === 'AT' ? { specialPayments: ['june', 'november'] } : {}),
        ...(live ? {} : { testEmployeesOnly: true }),
      }
    }
    case 'ai-reconciliation':
      // The controllers agreed to book a match without review above this confidence.
      return { autoMatch: true, autoMatchAbove: customer.segment === 'enterprise' ? 0.97 : 0.95, matchWindowDays: 14 }
    default:
      // Reporting runs on the product defaults and the template.
      return undefined
  }
}

/**
 * The CRON a JobRun takes from the facts of its AppRun: a quarterly filer submits on the filing calendar of its
 * country, a semi-monthly payroll computes the payslips two days before each pay day.
 */
function calendarOf(customer: Customer, instance: Instance, job: Job): string | undefined {
  const appRun = appRunDocument(customer, instance, job.application)
  if (appRun?.filingFrequency === 'quarterly') {
    return countries[instance.country ?? customer.country].filing?.quarterlyCron
  }
  return appRun?.payFrequency === 'semi-monthly' ? '0 5 13,28 * *' : undefined
}

/**
 * Developers run a job every quarter of an hour. Elsewhere a JobRun follows the calendar of its AppRun, and in
 * production the larger customers tune the schedule and give the job larger batches and more time.
 */
function jobRunDocument(customer: Customer, instance: Instance, job: Job): Record<string, unknown> | undefined {
  if (instance.name === 'dev') {
    return { cron: '*/15 * * * *', batchSize: 50, timeoutMinutes: 5 }
  }
  const tuned = isProduction(instance.name) && customer.segment !== 'smb'
  const cron = calendarOf(customer, instance, job) ?? (tuned ? job.tunedCron : undefined)
  const factor = customer.segment === 'enterprise' ? 4 : 2
  const document = {
    ...(cron ? { cron } : {}),
    ...(tuned ? { batchSize: job.batchSize * factor, timeoutMinutes: job.timeoutMinutes * 2 } : {}),
  }
  return Object.keys(document).length ? document : undefined
}

/** A JobRun of tax filing is named after the return of its country (a BAS in Australia). */
const jobTitleOf = (job: Job, country: Country) =>
  (job.application === 'tax-filing' ? country.filing?.title : undefined) ?? job.title

/** An instance with its AppRuns and JobRuns, provisioned by Margaret, who holds the vault secrets. */
function addInstance(b: ScopeBuilder, customer: Customer, instance: Instance, stage: Stage) {
  const title = `${customer.title} ${instanceKinds[instance.name]?.title ?? instance.name}`
  const lifecycle = stage.production ? instance.lifecycle : undefined
  const createdDaysAgo = instance.createdDaysAgo ?? customer.since
  const shared = { timeZone: instance.timeZone ?? customer.timeZone, createdBy: 'margaret' as const, ...lifecycle }
  const country = countries[instance.country ?? customer.country]
  // Nobody edits the documents of a churned customer or of an expired instance any more.
  const expired = lifecycle?.expiresIn !== undefined && lifecycle.expiresIn < 0 ? -lifecycle.expiresIn : undefined
  const frozen = customer.deleted?.daysAgo ?? expired
  // The secrets are Margaret's; Alan schedules the jobs; the account manager keeps the rest.
  const configure = (key: string, created: number, document: Record<string, unknown>, until = frozen) =>
    b.configure(key, document, {
      lastDaysAgo: editedDaysAgo(b, created, until),
      authors: [holdsSecret(document) ? 'margaret' : key.startsWith('uxe.') ? 'alan' : customer.accountManager],
    })

  b.context(customer.name, instance.name, {
    title,
    description: instance.description,
    link: instance.link,
    labels: [instanceKinds[instance.name]?.label ?? 'test'],
    createdDaysAgo,
    ...shared,
  })
  const versions = instanceVersions(customer, instance, stage)
  // The last upgrade: yesterday in the release builds, otherwise the older the customer's patch the longer ago.
  const upgraded =
    frozen === undefined
      ? Math.min(stage.next ? 1 : 2 + (4 - customer.patch) * 5, Math.max(1, createdDaysAgo - 1))
      : editedDaysAgo(b, createdDaysAgo, frozen)
  b.configure(`cnt.${customer.name}.${instance.name}`, versions, {
    every: spacing(createdDaysAgo - upgraded, versions.length, 21),
    lastDaysAgo: upgraded,
    authors: ['margaret'],
  })

  for (const name of instance.applications) {
    const application = applicationOf(name)
    const reason = stage.production ? instance.disabled?.[name] : undefined
    const created = Math.min(createdDaysAgo, application.shipped - 14)
    const key = b.execution(customer.name, name, instance.name, {
      title: `${application.title} on ${title}`,
      createdDaysAgo: created,
      ...shared,
      ...(reason ? { disabled: true, description: reason, modifiedDaysAgo: 4, modifiedBy: 'alan' as const } : {}),
    })
    const document = appRunDocument(customer, instance, name)
    if (document) {
      configure(key, created, document)
    }
  }

  for (const path of instance.jobs ?? []) {
    const deletion = instance.deletedJobs?.[path]
    if (deletion && !keepsDeleted(stage)) {
      continue
    }
    const job = jobOf(path)
    const created = Math.min(createdDaysAgo, applicationOf(job.application).shipped - 30)
    const key = b.unitOfExecution(customer.name, job.application, instance.name, job.name, {
      title: `${jobTitleOf(job, country)} on ${title}`,
      createdDaysAgo: created,
      ...shared,
    })
    const document = jobRunDocument(customer, instance, job)
    if (document) {
      configure(key, created, document, deletion ? Math.max(deletion.daysAgo, frozen ?? 0) : frozen)
    }
    if (deletion) {
      b.trash(key, deletion)
    }
  }
}

/** A customer with its facts, the applications it bought and its instances. */
function addCustomer(b: ScopeBuilder, customer: Customer, stage: Stage) {
  const country = countries[customer.country]
  const frozen = customer.deleted?.daysAgo
  const authors = [customer.accountManager]
  b.subject(customer.name, {
    title: customer.title,
    description: customer.description,
    labels: [customer.segment, customer.region],
    values: {
      crmId: customer.crmId,
      supportTier: customer.supportTier,
      country: customer.country,
      accountManager: people[customer.accountManager].name,
    },
    timeZone: customer.timeZone,
    createdDaysAgo: customer.since,
    createdBy: customer.accountManager,
  })
  b.configure(
    `sbt.${customer.name}`,
    {
      legalName: customer.legalName,
      baseCurrency: country.currency,
      locale: country.locale,
      fiscalYearStart: customer.fiscalYearStart,
      dataResidency: country.residency,
    },
    { lastDaysAgo: editedDaysAgo(b, customer.since, frozen), authors },
  )

  for (const name of customer.applications) {
    const application = applicationOf(name)
    const ending = stage.production ? customer.notice?.[name] : undefined
    const created = Math.min(customer.since, application.shipped - 14)
    const key = b.usage(customer.name, name, {
      title: `${application.title} for ${customer.title}`,
      labels: ending ? ['notice-given'] : application.labels.includes('preview') ? ['pilot'] : undefined,
      expiresIn: ending,
      values: { salesOrder: salesOrder(customer, name) },
      timeZone: customer.timeZone,
      createdDaysAgo: created,
      createdBy: customer.accountManager,
    })
    const versions = contractVersions(customer, name, stage)
    if (versions.length) {
      const lastDaysAgo = editedDaysAgo(b, created, frozen)
      b.configure(key, versions, { every: spacing(created - lastDaysAgo, versions.length, 120), lastDaysAgo, authors })
    }
  }

  for (const instance of customer.instances) {
    addInstance(b, customer, instance, stage)
  }
  if (customer.deleted && keepsDeleted(stage)) {
    // The customer went to Deleted with its instances, usages, AppRuns and JobRuns.
    b.trash(`sbt.${customer.name}`, customer.deleted)
  }
}

/** Production, and the release candidate as its copy with release 2026.10.0. */
function production(b: ScopeBuilder, next: boolean) {
  const stage: Stage = { production: true, next }
  addApplications(b, applicationNames, stage)
  rules(b, stage)
  for (const customer of customers) {
    // Margie's Travel is in Deleted in production, so the release candidate does not have it.
    if (!customer.deleted || keepsDeleted(stage)) {
      addCustomer(b, customer, stage)
    }
  }
}

/** The board's development view: Contoso with a new dev instance and its demo; payments runs on the demo here. */
function development(b: ScopeBuilder) {
  const stage: Stage = {}
  addApplications(b, ['invoicing', 'payments', 'reporting'], stage, ['reporting.nightly'])
  rules(b, stage, [developerDefaults])
  addCustomer(
    b,
    {
      ...customerOf('contoso'),
      instances: [
        // The developers' instance: a nightly build, debug logs and the reporting job every quarter of an hour.
        {
          name: 'dev',
          applications: ['invoicing', 'reporting'],
          jobs: ['reporting.nightly'],
          productVersion: '2026.10.0-dev.14',
          createdDaysAgo: 30,
        },
        { name: 'demo', applications: ['invoicing', 'payments'] },
      ],
    },
    stage,
  )
}

/** Fabrikam's own test customer of the staging environment: every application, with generated test data. */
function qaCustomer(b: ScopeBuilder): Customer {
  return {
    name: 'fabrikam-qa',
    title: 'Fabrikam QA',
    legalName: 'Fabrikam, Inc. (test entity)',
    segment: 'enterprise',
    region: 'emea',
    country: 'NL',
    supportTier: 'enterprise',
    crmId: `CRM-QA${b.rng.int(100, 999)}`,
    accountManager: 'alan',
    timeZone: 'Europe/Amsterdam',
    cloudRegion: b.rng.pick(['westeurope', 'northeurope']),
    fiscalYearStart: '01-01',
    users: b.rng.int(5, 25) * 10,
    applications: applicationNames,
    instances: [
      { name: 'staging', applications: applicationNames, jobs: jobsOf(applicationNames) },
      {
        name: 'staging-perf',
        applications: ['invoicing', 'payments', 'reporting'],
        jobs: ['reporting.nightly', 'payments.settlement-export'],
        description: 'Load tests: **50,000** generated invoices and their payments every night.',
      },
    ],
    since: 200,
    patch: 4,
  }
}

/**
 * Staging: a staging instance for three customers who test the next release, and Fabrikam's QA customer. It runs a
 * release candidate build, so it has the product defaults, template and schema of release 2026.10.0.
 */
function staging(b: ScopeBuilder) {
  const stage: Stage = { next: true, productVersion: '2026.10.0-rc.3' }
  addApplications(b, applicationNames, stage)
  rules(b, stage)
  for (const name of ['contoso', 'adventure-works', 'woodgrove-bank']) {
    const customer = customerOf(name)
    addCustomer(
      b,
      {
        ...customer,
        instances: [{ name: 'staging', applications: customer.applications, jobs: jobsOf(customer.applications) }],
      },
      stage,
    )
  }
  addCustomer(b, qaCustomer(b), stage)
}

/**
 * AI reconciliation, delivered first to the US production instances of Adventure Works and Wide World Importers:
 * both import their bank statements already, and the match run starts on every import.
 */
function featureDelivery(b: ScopeBuilder) {
  const stage: Stage = { next: true, productVersion: '2026.11.0-preview.2' }
  const pilot = ['invoicing', 'bank-sync', aiReconciliation.name]
  const pilotJobs = ['bank-sync.statement-import', 'ai-reconciliation.match-run']
  addApplications(b, pilot, stage, pilotJobs)
  rules(b, stage)
  // A match is booked without review only above a confidence the controllers agreed to.
  b.descendantSchema('rst.ai-reconciliation', 'exe', {
    type: 'object',
    properties: { autoMatch: { type: 'boolean' }, autoMatchAbove: { type: 'number', minimum: 0.9, maximum: 1 } },
    required: ['autoMatchAbove'],
  })
  for (const [name, instanceName] of [
    ['adventure-works', 'production-us'],
    ['wide-world-importers', 'production'],
  ] as const) {
    const customer = customerOf(name)
    const instance = customer.instances.find((candidate) => candidate.name === instanceName)!
    addCustomer(
      b,
      { ...customer, applications: pilot, instances: [{ ...instance, applications: pilot, jobs: pilotJobs }] },
      stage,
    )
  }
}

export const financeCloud: ShowcaseProject = {
  org: 'fabrikam',
  project: 'finance-cloud',
  summary:
    'Single-tenant B2B SaaS: customers, their instances, the applications they bought and the jobs that run in each instance.',
  members: {
    ada: 'Owner',
    grace: 'Administrator',
    alan: 'Contributor',
    margaret: 'ContributorWithSecrets',
    ken: 'Reader',
  },
  settings: {
    types: {
      rst: { label: 'Application', color: 'violet', icon: 'i-lucide-layout-grid' },
      unt: { label: 'Job', color: 'fuchsia', icon: 'i-lucide-clock' },
      sbt: { label: 'Customer', color: 'blue', icon: 'i-lucide-building-2' },
      usg: { label: 'Usage', color: 'amber', icon: 'i-lucide-tag' },
      cnt: { label: 'Instance', color: 'teal', icon: 'i-lucide-server' },
      exe: { label: 'AppRun', color: 'emerald', icon: 'i-lucide-play' },
      uxe: { label: 'JobRun', color: 'orange', icon: 'i-lucide-workflow' },
    },
    defaultTimeZone: 'Europe/London',
    dateFormat: 'd mmm yyyy',
  },
  views: environmentViews({
    development: 'Development: Contoso’s dev and demo instances',
    staging: 'Staging instances of three customers and Fabrikam QA',
    'release-candidate': 'Release 2026.10.0 on every running instance',
    'feature-delivery1': 'AI reconciliation for Adventure Works and Wide World Importers',
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
