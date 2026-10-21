import type { Meta, ScopeBuilder, UnitMeta } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// The versioned monolith of the documentation (docs/42for.net/platform/examples/versioned-monolith.md), example 6 of
// the Miro board. Ledgerline ships its ERP as one deployable and still runs several versions of it: the version is
// the subject, a channel still pinned to that version is the context, a module inside the process is the
// responsibility, and the build of a module on a version (the usage) says whether that line contains the module.
// v2025 is in long-term support, v2026 is current and Contoso has a contractual pin on v2025. The retired v2024, the
// v2027 preview, more modules and more pinned customers make it a catalog of hundreds.

const docs = 'https://docs.ledgerline.example/erp'

/** https://v2026.erp.ledgerline.example for a production channel, https://contoso.v2025.erp.ledgerline.example else. */
const endpoint = (version: string, channel: string, environment?: string) =>
  `https://${[channel === 'production' ? undefined : channel, version, environment, 'erp.ledgerline.example']
    .filter(Boolean)
    .join('.')}`

interface Module {
  name: string
  title: string
  labels: string[]
  description: string
  /** The CODEOWNERS team and the folder of the module in the monolith (facts, so Values). */
  team: string
  folder: string
  /** The first line that contained the module; an older line that gets it has a backport. */
  introduced: number
  /** Days since the module was added to the codebase. */
  since: number
  /** Worker instances of the module on the production channel of the current version. */
  instances: number
  /** The behavior of the module as it exists in code today: its product defaults, oldest version first. */
  defaults: Record<string, unknown>[]
  history?: HistoryOptions
  /** The feature set a line was born with (the document of the build). */
  build: (year: number) => Record<string, unknown>
}

// Billing's product defaults over the last five months of releases, one every nine days: the flags the code
// understands. Credit notes came with the 2026 line, so the v2025 build turns them off.
const billingDefaults = evolve(
  {
    vat: true,
    creditNote: false,
    legacyExport: false,
    invoiceCurrency: 'NOK',
    invoiceNumbering: 'yearly',
    paymentTermsDays: 30,
    reminderFee: 35,
    rounding: 'half-up',
    pdfRenderer: 'v1',
  },
  19,
  (version) =>
    [
      { reminderFee: 70 },
      { rounding: 'half-even' },
      { pdfRenderer: 'v2' },
      { creditNote: true },
      { paymentTermsDays: 14 },
      { batchSize: 500 },
      { batchSize: 1000 },
      { invoiceNumbering: 'continuous' },
      { pdfRenderer: 'v3' },
      { reminderFee: 75 },
      { paymentTermsDays: 30 },
      { batchSize: 2000 },
      { currencies: ['NOK', 'SEK', 'DKK', 'EUR'] },
      { currencies: ['NOK', 'SEK', 'DKK', 'EUR', 'USD'] },
      { currencies: ['NOK', 'SEK', 'DKK', 'EUR', 'USD', 'GBP', 'CHF'] },
      { creditNoteApproval: 'above-10000' },
      { reminderFee: 80 },
      { batchSize: 2500 },
    ][version - 2]!,
)

const modules: Module[] = [
  {
    name: 'auth',
    title: 'Authentication',
    labels: ['platform'],
    description: 'Sign-in, single sign-on and the API tokens of every other module.',
    team: '@ledgerline/identity',
    folder: 'src/Modules/Auth',
    introduced: 2024,
    since: 1300,
    instances: 3,
    // The single sign-on providers are flags, so a channel can switch one off; arrays are unioned along the merge.
    defaults: evolve(
      {
        tokenLifetimeMinutes: 60,
        mfa: 'admins',
        sso: { saml: true },
        signingKey: '@(erp.auth.jwt-signing-key, primaryVault)',
      },
      3,
      (version) => (version === 2 ? { sso: { oidc: true } } : { tokenLifetimeMinutes: 30, mfa: 'all' }),
    ),
    history: { every: 80, lastDaysAgo: 9 },
    build: (year) => ({ passkeys: year >= 2026, scim: year >= 2025 }),
  },
  {
    name: 'billing',
    title: 'Billing',
    labels: ['finance'],
    description:
      'Invoices, credit notes and VAT. The **flags** in its document are what the code understands; a version ' +
      'or a channel turns a flag off with `false`.',
    team: '@ledgerline/finance',
    folder: 'src/Modules/Billing',
    introduced: 2024,
    since: 1300,
    instances: 6,
    defaults: billingDefaults,
    history: { every: 9, lastDaysAgo: 3 },
    build: (year) => ({ creditNote: year >= 2026, ...(year >= 2027 ? { eInvoice: true } : {}) }),
  },
  {
    name: 'reporting',
    title: 'Reporting',
    labels: ['finance', 'analytics'],
    description: 'Financial and operational reports on a columnar copy of the ledger. New in the 2026 line.',
    team: '@ledgerline/analytics',
    folder: 'src/Modules/Reporting',
    introduced: 2026,
    since: 330,
    instances: 2,
    defaults: [{ engine: 'columnar', cacheMinutes: 15, exportFormats: ['xlsx', 'pdf', 'csv'] }],
    build: (year) => ({ pivotTables: true, scheduledExports: year >= 2026 }),
  },
  {
    name: 'inventory',
    title: 'Inventory',
    labels: ['logistics'],
    description: 'Items, stock levels, lots and the valuation of stock.',
    team: '@ledgerline/supply-chain',
    folder: 'src/Modules/Inventory',
    introduced: 2024,
    since: 1300,
    instances: 4,
    defaults: [
      { valuation: 'fifo', negativeStock: false, reservationMinutes: 30 },
      { valuation: 'fifo', negativeStock: false, reservationMinutes: 45 },
    ],
    history: { every: 50, lastDaysAgo: 17 },
    build: (year) => ({ lotTracking: true, serialNumbers: year >= 2025, consignment: year >= 2026 }),
  },
  {
    name: 'hr',
    title: 'HR and payroll',
    labels: ['people'],
    description: 'Employees, absence and the payroll export to the authorities.',
    team: '@ledgerline/people',
    folder: 'src/Modules/Hr',
    introduced: 2024,
    since: 1300,
    instances: 2,
    defaults: [{ payrollCountry: 'NO', leaveAccrual: 'monthly', selfService: true }],
    build: (year) => ({ payslipPortal: true, absenceApi: year >= 2025 }),
  },
  {
    name: 'crm',
    title: 'CRM',
    labels: ['sales'],
    description: 'Accounts, contacts and opportunities, synchronized with the mailbox of the sales team.',
    team: '@ledgerline/sales',
    folder: 'src/Modules/Crm',
    introduced: 2024,
    since: 1300,
    instances: 3,
    defaults: [{ duplicateCheck: true, leadScoring: false, mailProvider: 'graph' }],
    build: (year) => ({ mailSync: true, leadScoring: year >= 2026 }),
  },
  {
    name: 'document-archive',
    title: 'Document archive',
    labels: ['compliance'],
    description: 'Immutable archive of vouchers and contracts for the statutory retention period.',
    team: '@ledgerline/compliance',
    folder: 'src/Modules/DocumentArchive',
    introduced: 2024,
    since: 1300,
    instances: 2,
    defaults: [{ retentionYears: 10, ocr: true, storage: 'blob-immutable' }],
    build: (year) => ({ legalHold: true, ocr: year >= 2025 }),
  },
  {
    name: 'audit-log',
    title: 'Audit log',
    labels: ['compliance'],
    description: 'Tamper-evident log of every change to master data and postings.',
    team: '@ledgerline/compliance',
    folder: 'src/Modules/AuditLog',
    introduced: 2025,
    since: 700,
    instances: 2,
    defaults: [{ retentionDays: 2555, tamperEvident: true, sink: 'append-only-table' }],
    build: (year) => ({ tamperEvident: true, exportFormat: year >= 2026 ? 'ocsf' : 'csv' }),
  },
  {
    name: 'warehouse',
    title: 'Warehouse management',
    labels: ['logistics'],
    description: 'Bins, picking waves and shipping docks of the customer’s warehouses.',
    team: '@ledgerline/supply-chain',
    folder: 'src/Modules/Warehouse',
    introduced: 2025,
    since: 700,
    instances: 4,
    defaults: [
      { pickingStrategy: 'wave', barcode: 'gs1-128', slotting: false },
      { pickingStrategy: 'wave', barcode: 'gs1-128', slotting: true },
    ],
    history: { every: 40, lastDaysAgo: 22 },
    build: (year) => ({ wavePicking: true, voicePicking: year >= 2026 }),
  },
]

const moduleOf = (name: string) => modules.find((module) => module.name === name)!

interface Job {
  module: string
  name: string
  title: string
  description?: string
  unitType: UnitMeta['unitType']
  definition: string
  /** The parameters of the job, one content per version. */
  parameters: Record<string, unknown> | Record<string, unknown>[]
}

const jobs: Job[] = [
  {
    module: 'billing',
    name: 'month-end-close',
    title: 'Month-end close',
    description: 'Locks the period, posts the accruals and opens the next month.',
    unitType: 'time',
    definition: '30 1 1 * *',
    parameters: [
      { lockPeriod: true, postAccruals: true, timeoutMinutes: 180 },
      { lockPeriod: true, postAccruals: true, timeoutMinutes: 240 },
      { lockPeriod: true, postAccruals: true, timeoutMinutes: 240, fxRevaluation: true },
    ],
  },
  {
    module: 'billing',
    name: 'dunning-run',
    title: 'Dunning run',
    unitType: 'time',
    definition: '0 6 * * 1-5',
    parameters: { levels: 3, minimumAmount: 50 },
  },
  {
    module: 'reporting',
    name: 'nightly-refresh',
    title: 'Nightly report refresh',
    description: 'Copies the day’s postings to the columnar store the reports read.',
    unitType: 'time',
    definition: '0 3 * * *',
    parameters: { snapshot: 'ledger', timeoutMinutes: 90 },
  },
  {
    module: 'inventory',
    name: 'stock-revaluation',
    title: 'Stock revaluation',
    unitType: 'time',
    definition: '0 4 * * 0',
    parameters: { postToLedger: true },
  },
  {
    module: 'inventory',
    name: 'supplier-price-import',
    title: 'Supplier price import',
    description: 'Reads the price catalogs suppliers drop on the EDI server.',
    unitType: 'file',
    definition: 'inbound/supplier-prices/*.edi',
    parameters: { format: 'edifact-pricat', rejectUnknownItems: true },
  },
  {
    module: 'hr',
    name: 'payroll-export',
    title: 'Payroll export',
    unitType: 'time',
    definition: '0 18 25 * *',
    parameters: { target: 'altinn', format: 'a-melding' },
  },
  {
    module: 'crm',
    name: 'contact-sync',
    title: 'Contact sync',
    unitType: 'message',
    definition: 'crm-contact-changes',
    parameters: { batchSize: 200, dedupe: true },
  },
  {
    module: 'document-archive',
    name: 'retention-sweep',
    title: 'Retention sweep',
    unitType: 'time',
    definition: '0 1 * * 6',
    parameters: { dryRun: false, legalHoldAware: true },
  },
  {
    module: 'audit-log',
    name: 'log-shipping',
    title: 'Audit log shipping',
    unitType: 'event',
    definition: 'AuditBatchSealed',
    parameters: { compression: 'zstd', maxBatch: 5000 },
  },
  {
    module: 'auth',
    name: 'key-rotation',
    title: 'Signing key rotation',
    unitType: 'time',
    definition: '0 0 1 */3 *',
    parameters: { overlapHours: 48 },
  },
  {
    module: 'warehouse',
    name: 'wave-release',
    title: 'Wave release',
    unitType: 'time',
    definition: '*/15 6-22 * * 1-6',
    parameters: { maxLinesPerWave: 400, cutoff: '15:30' },
  },
]

/** The feature of feature-delivery1: billing reads the bank's statements and matches them to open invoices. */
const bankStatementImport: Job = {
  module: 'billing',
  name: 'bank-statement-import',
  title: 'Bank statement import',
  description: 'Reads the CAMT.053 statements of the bank and matches the payments to open invoices.',
  unitType: 'file',
  definition: 'inbound/camt053/*.xml',
  parameters: { format: 'camt.053', matchOn: ['reference', 'amount'], autoPost: false },
}

const jobKey = (job: Job) => `${job.module}.${job.name}`
const jobOf = (key: string) => [...jobs, bankStatementImport].find((job) => jobKey(job) === key)!
const jobsOf = (names: readonly string[]) => jobs.filter((job) => names.includes(job.module)).map(jobKey)

type VersionName = 'v2024' | 'v2025' | 'v2026' | 'v2027'

interface Version {
  name: VersionName
  year: number
  title: string
  labels: string[]
  description: string
  releaseTag: string
  /** The branch the line is built from; `release/<year>` by default. */
  branch?: string
  releaseManager: string
  runtime: string
  /** The database schema of the line, one entry per migration it shipped (the versions of its document). */
  schemaVersions: number[]
  modules: string[]
  since: number
  /** On the version and everything under it: the end of support, the start of a preview. */
  lifecycle?: Meta
  /** On the version only. */
  meta?: Meta
  /** What the release candidate changes on the line. */
  next?: { releaseTag: string; schemaVersion: number }
  /** Days since the documents of a frozen line last changed; nobody edits a line after its end of support. */
  lastChangeDaysAgo?: number
}

const versions: Version[] = [
  {
    name: 'v2024',
    year: 2024,
    title: 'ERP 2024',
    labels: ['retired'],
    description:
      'Out of support. The record stays after the last day, so the reason its channels went away is still in ' +
      'the catalog.',
    releaseTag: '2024.12.7',
    releaseManager: 'Barbara Liskov',
    runtime: 'dotnet-6',
    schemaVersions: [11],
    modules: ['auth', 'billing', 'inventory', 'hr', 'crm', 'document-archive'],
    since: 1050,
    lifecycle: { expiresIn: -12 },
    lastChangeDaysAgo: 16,
  },
  {
    name: 'v2025',
    year: 2025,
    title: 'ERP 2025 LTS',
    labels: ['lts'],
    description:
      'Long-term support: security and legal fixes only, until the end of support (the expiry date). See the ' +
      `[support policy](${docs}/support-policy).`,
    releaseTag: '2025.9.3',
    releaseManager: 'Barbara Liskov',
    runtime: 'dotnet-8',
    schemaVersions: [12, 13, 14],
    modules: ['auth', 'billing', 'inventory', 'hr', 'crm', 'document-archive', 'audit-log', 'warehouse'],
    since: 680,
    meta: { expiresIn: 252 },
  },
  {
    name: 'v2026',
    year: 2026,
    title: 'ERP 2026',
    labels: ['current'],
    description: 'The current version. Every module, quarterly updates through the early-access ring.',
    releaseTag: '2026.3.1',
    releaseManager: 'Edsger Dijkstra',
    runtime: 'dotnet-10',
    schemaVersions: [15, 16, 17, 18],
    modules: ['auth', 'billing', 'reporting', 'inventory', 'hr', 'crm', 'document-archive', 'audit-log', 'warehouse'],
    since: 300,
    next: { releaseTag: '2026.4.0-rc.1', schemaVersion: 19 },
  },
  {
    name: 'v2027',
    year: 2027,
    title: 'ERP 2027 preview',
    labels: ['preview'],
    description:
      'Preview of the next major version for early adopters, from the start date. Reporting is being rewritten ' +
      'and joins a later preview drop.',
    releaseTag: '2027.0.0-preview.2',
    releaseManager: 'Ada Lovelace',
    runtime: 'dotnet-10',
    schemaVersions: [20],
    modules: ['auth', 'billing', 'inventory', 'hr', 'crm', 'document-archive', 'audit-log', 'warehouse'],
    since: 25,
    lifecycle: { startsIn: 40 },
  },
]

const versionOf = (name: VersionName) => versions.find((version) => version.name === name)!

interface Customer {
  title: string
  crmId: string
  contact: string
  contract: string
}

interface Channel {
  version: VersionName
  name: string
  title: string
  description?: string
  labels: string[]
  timeZone: string
  region: string
  maintenanceWindow?: string
  /** The modules rolled out to the channel. */
  modules: string[]
  /** The jobs that run in the channel, `<module>.<job>`. */
  jobs: string[]
  customer?: Customer
  values?: Record<string, unknown>
  /** Settings of the channel besides its endpoint, maintenance window and region. */
  settings?: Record<string, unknown>
  /** Earlier versions of the channel document as changes against the current one, oldest first. */
  history?: Record<string, unknown>[]
  /** Own documents of the rollouts by module: the contractual exceptions of the channel; a list is a history. */
  rollouts?: Record<string, Record<string, unknown> | Record<string, unknown>[]>
  /** Own documents of the job runs by job. */
  runs?: Record<string, Record<string, unknown>>
  /** On the channel and everything in it; production adds the version's lifecycle. */
  lifecycle?: Meta
  /** On the channel only. */
  meta?: Meta
  rolloutMeta?: Record<string, Meta>
  runMeta?: Record<string, Meta>
  since: number
  createdBy?: PersonaId
  /** The endpoint's environment for the channels outside production (`staging`). */
  environment?: string
  /** Days since the documents of the channel last changed; a deleted or retired channel is not edited after. */
  lastChangeDaysAgo?: number
}

/** The rollouts of a pinned channel deploy the pinned update, not the latest build of the version. */
const pinnedBuild = { releaseTag: '@annotation("/pinnedBuild", "Context")' }

/** The worker instances per module on a production channel, a share of the current version's load. */
const instances = (names: readonly string[], share: number) =>
  Object.fromEntries(
    names.map((name) => [name, { instances: Math.max(1, Math.round(moduleOf(name).instances * share)) }]),
  )

const v2025Modules = versionOf('v2025').modules
const v2026Modules = versionOf('v2026').modules

const channels: Channel[] = [
  {
    version: 'v2024',
    name: 'production',
    title: 'v2024 production',
    labels: ['production'],
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'sun 02:00-04:00 Europe/Oslo',
    modules: versionOf('v2024').modules,
    jobs: ['billing.month-end-close', 'hr.payroll-export'],
    values: { tenants: 0 },
    since: 1050,
  },
  {
    // The pin was lifted when Northwind moved to v2025: the channel is deleted and its rollouts go with it.
    version: 'v2024',
    name: 'northwind',
    title: 'Northwind (v2024 pin)',
    description: 'Pinned until Northwind passed its regression on v2025.',
    labels: ['pin'],
    customer: {
      title: 'Northwind Traders',
      crmId: 'LL-CRM-0107',
      contact: 'erp-admin@northwind.example',
      contract: 'PIN-2024-003',
    },
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'sun 03:00-05:00 Europe/Oslo',
    modules: ['auth', 'billing', 'inventory', 'crm'],
    jobs: ['billing.month-end-close'],
    rollouts: { billing: { legacyExport: true } },
    since: 900,
    createdBy: 'barbara',
    lastChangeDaysAgo: 22,
  },
  {
    version: 'v2025',
    name: 'production',
    title: 'v2025 production',
    labels: ['production'],
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'sun 02:00-04:00 Europe/Oslo',
    modules: v2025Modules,
    jobs: jobsOf(v2025Modules),
    values: { tenants: 184, statusPage: 'https://status.ledgerline.example/v2025' },
    // Billing keeps no document of its own here, so the Contoso rollout's document is the whole difference.
    rollouts: instances(
      v2025Modules.filter((name) => name !== 'billing'),
      0.5,
    ),
    since: 680,
  },
  {
    version: 'v2025',
    name: 'contoso',
    title: 'Contoso (v2025 pin)',
    description:
      'Contractual pin: Contoso stays on v2025 until their own regression suite passes on v2026. Billing only, ' +
      'with the legacy export their auditors still read.',
    labels: ['pin'],
    customer: {
      title: 'Contoso Ltd.',
      crmId: 'LL-CRM-0412',
      contact: 'erp-ops@contoso.example',
      contract: 'PIN-2025-007',
    },
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'sun 01:00-03:00 Europe/Oslo',
    history: [{ maintenanceWindow: 'sun 02:00-04:00 Europe/Oslo' }],
    modules: ['billing'],
    jobs: [],
    rollouts: { billing: { legacyExport: true } },
    // The switch for the week the pin is frozen during Contoso's own change window.
    rolloutMeta: {
      billing: {
        disabled: true,
        description: 'Frozen for the week of Contoso’s own change window.',
        modifiedDaysAgo: 2,
        modifiedBy: 'barbara',
      },
    },
    since: 520,
    createdBy: 'barbara',
  },
  {
    version: 'v2025',
    name: 'woodgrove',
    title: 'Woodgrove Bank (v2025 pin)',
    description: 'Woodgrove has not passed regression on v2026 yet; the pin ends with the contract.',
    labels: ['pin', 'regulated'],
    customer: {
      title: 'Woodgrove Bank',
      crmId: 'LL-CRM-0233',
      contact: 'erp-change@woodgrove.example',
      contract: 'PIN-2025-011',
    },
    timeZone: 'Europe/London',
    region: 'uksouth',
    maintenanceWindow: 'sat 21:00-23:00 Europe/London',
    modules: ['auth', 'billing', 'crm', 'document-archive', 'audit-log'],
    jobs: ['billing.month-end-close', 'document-archive.retention-sweep', 'audit-log.log-shipping'],
    rollouts: {
      // The bank allows SAML only; the allow-list is an array, because the channel only adds to it.
      auth: { mfa: 'all', sso: { oidc: false }, ipAllowList: ['198.51.100.0/24'] },
      billing: { invoiceCurrency: 'GBP', dualControl: true },
      'document-archive': { retentionYears: 12, storageKey: '@(woodgrove.archive-storage-key, primaryVault)' },
      'audit-log': {
        sink: 'syslog+tls://siem.woodgrove.example:6514',
        notify: '@annotation("$.customerContact", "Context")',
      },
    },
    runs: { 'billing.month-end-close': { cron: '0 2 2 * *', timeZone: 'Europe/London' } },
    meta: { expiresIn: 75 },
    since: 610,
    createdBy: 'barbara',
  },
  {
    version: 'v2025',
    name: 'litware',
    title: 'Litware (v2025 pin)',
    labels: ['pin'],
    customer: { title: 'Litware, Inc.', crmId: 'LL-CRM-0388', contact: 'it@litware.example', contract: 'PIN-2025-019' },
    timeZone: 'America/Chicago',
    region: 'centralus',
    maintenanceWindow: 'sun 03:00-05:00 America/Chicago',
    modules: ['auth', 'billing', 'hr'],
    jobs: ['billing.dunning-run'],
    // US sales tax instead of VAT.
    rollouts: { billing: { invoiceCurrency: 'USD', salesTax: 'avalara', vat: false }, hr: { payrollCountry: 'US' } },
    runs: { 'billing.dunning-run': { cron: '0 7 * * 1-5', timeZone: 'America/Chicago' } },
    since: 450,
    createdBy: 'edsger',
    // The last change before payroll moved to a local provider.
    lastChangeDaysAgo: 8,
  },
  {
    version: 'v2025',
    name: 'proseware',
    title: 'Proseware (v2025 pin)',
    labels: ['pin'],
    customer: {
      title: 'Proseware, Inc.',
      crmId: 'LL-CRM-0451',
      contact: 'erp@proseware.example',
      contract: 'PIN-2025-022',
    },
    timeZone: 'Europe/Stockholm',
    region: 'swedencentral',
    maintenanceWindow: 'sun 00:00-02:00 Europe/Stockholm',
    modules: ['auth', 'billing', 'inventory', 'warehouse'],
    jobs: ['inventory.stock-revaluation', 'warehouse.wave-release'],
    rollouts: {
      billing: { invoiceCurrency: 'SEK' },
      inventory: { valuation: 'weighted-average' },
      warehouse: { barcode: 'ean-13' },
    },
    since: 380,
    createdBy: 'barbara',
  },
  {
    version: 'v2025',
    name: 'margies-travel',
    title: 'Margie’s Travel (v2025 pin)',
    labels: ['pin'],
    customer: {
      title: 'Margie’s Travel',
      crmId: 'LL-CRM-0476',
      contact: 'finance-it@margiestravel.example',
      contract: 'PIN-2025-024',
    },
    timeZone: 'Europe/Copenhagen',
    region: 'northeurope',
    maintenanceWindow: 'sun 01:00-03:00 Europe/Copenhagen',
    modules: ['auth', 'billing', 'hr', 'crm'],
    jobs: ['hr.payroll-export'],
    rollouts: { billing: { invoiceCurrency: 'DKK', oioubl: true }, hr: { payrollCountry: 'DK' } },
    // Denmark reports payroll to eIndkomst, not to the Norwegian A-melding.
    runs: { 'hr.payroll-export': { target: 'eindkomst', format: 'eindkomst-xml', timeZone: 'Europe/Copenhagen' } },
    since: 340,
    createdBy: 'edsger',
  },
  {
    version: 'v2026',
    name: 'production',
    title: 'v2026 production',
    labels: ['production'],
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'sun 02:00-04:00 Europe/Oslo',
    history: [{ logLevel: 'info' }, { logLevel: 'debug' }, { logLevel: 'info' }],
    modules: v2026Modules,
    jobs: jobsOf(v2026Modules),
    values: { tenants: 412, statusPage: 'https://status.ledgerline.example/v2026' },
    rollouts: instances(v2026Modules, 1),
    runs: {
      // The board's job run: the close waits for the bank feeds and reports to the channel's own API.
      'billing.month-end-close': {
        cron: '0 4 1 * *',
        timeZone: 'Europe/Oslo',
        waitForBankFeeds: true,
        ledgerApi: '@config("/endpoint")',
      },
      'reporting.nightly-refresh': { cron: '30 2 * * *', timeZone: 'Europe/Oslo' },
    },
    since: 290,
  },
  {
    version: 'v2026',
    name: 'early-access',
    title: 'v2026 early access',
    description: 'Every update of v2026 reaches this ring two weeks before production. Tenants opt in.',
    labels: ['ring'],
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'wed 20:00-22:00 Europe/Oslo',
    settings: { logLevel: 'info' },
    modules: v2026Modules,
    jobs: ['reporting.nightly-refresh', 'crm.contact-sync'],
    values: { tenants: 23 },
    rollouts: instances(['billing', 'reporting'], 0.2),
    runMeta: {
      'crm.contact-sync': {
        disabled: true,
        description: 'Paused while the CRM connector is rebuilt for 2026.4.',
        modifiedDaysAgo: 9,
        modifiedBy: 'edsger',
      },
    },
    since: 240,
    createdBy: 'barbara',
  },
  {
    version: 'v2026',
    name: 'alpine',
    title: 'Alpine Ski House (v2026 pin)',
    description: 'Pinned to update 2026.2 for the ski season: no updates between December and April.',
    labels: ['pin', 'seasonal'],
    customer: {
      title: 'Alpine Ski House',
      crmId: 'LL-CRM-0517',
      contact: 'it@alpineskihouse.example',
      contract: 'PIN-2026-002',
    },
    values: { pinnedBuild: '2026.2.4' },
    timeZone: 'Europe/Zurich',
    region: 'switzerlandnorth',
    maintenanceWindow: 'tue 22:00-23:30 Europe/Zurich',
    modules: ['auth', 'billing', 'inventory', 'warehouse'],
    jobs: ['inventory.stock-revaluation', 'warehouse.wave-release'],
    // Every rollout deploys the pinned update, and the database stays on the schema of 2026.2.
    settings: { schemaVersion: 17 },
    rollouts: {
      auth: { ...pinnedBuild },
      billing: { invoiceCurrency: 'CHF', ...pinnedBuild },
      inventory: { rentalPools: true, ...pinnedBuild },
      warehouse: { pickingStrategy: 'zone', ...pinnedBuild },
    },
    since: 200,
    createdBy: 'barbara',
  },
  {
    version: 'v2026',
    name: 'fourth-coffee',
    title: 'Fourth Coffee (v2026 pin)',
    labels: ['pin'],
    customer: {
      title: 'Fourth Coffee',
      crmId: 'LL-CRM-0533',
      contact: 'erp-admins@fourthcoffee.example',
      contract: 'PIN-2026-005',
    },
    timeZone: 'America/Los_Angeles',
    region: 'westus2',
    maintenanceWindow: 'sun 01:00-03:00 America/Los_Angeles',
    modules: ['billing', 'inventory', 'warehouse', 'reporting'],
    jobs: ['inventory.supplier-price-import', 'reporting.nightly-refresh'],
    rollouts: { billing: { invoiceCurrency: 'USD', salesTax: 'avalara', vat: false } },
    runs: { 'reporting.nightly-refresh': { cron: '0 2 * * *', timeZone: 'America/Los_Angeles' } },
    since: 150,
    createdBy: 'edsger',
  },
  {
    version: 'v2026',
    name: 'tailspin',
    title: 'Tailspin Toys (v2026 pin)',
    labels: ['pin'],
    customer: {
      title: 'Tailspin Toys',
      crmId: 'LL-CRM-0548',
      contact: 'erp@tailspintoys.example',
      contract: 'PIN-2026-008',
    },
    timeZone: 'Europe/Berlin',
    region: 'germanywestcentral',
    maintenanceWindow: 'sun 02:00-04:00 Europe/Berlin',
    modules: ['auth', 'billing', 'crm', 'reporting'],
    jobs: ['billing.month-end-close', 'crm.contact-sync'],
    rollouts: { billing: { invoiceCurrency: 'EUR', xRechnung: true } },
    since: 120,
    createdBy: 'barbara',
  },
  {
    version: 'v2026',
    name: 'wide-world-importers',
    title: 'Wide World Importers (v2026 pin)',
    labels: ['pin'],
    customer: {
      title: 'Wide World Importers',
      crmId: 'LL-CRM-0562',
      contact: 'ops@wideworldimporters.example',
      contract: 'PIN-2026-011',
    },
    timeZone: 'Europe/Amsterdam',
    region: 'westeurope',
    maintenanceWindow: 'sun 00:00-02:00 Europe/Amsterdam',
    modules: ['auth', 'billing', 'inventory', 'warehouse', 'crm'],
    jobs: ['inventory.supplier-price-import', 'warehouse.wave-release'],
    rollouts: {
      billing: { invoiceCurrency: 'EUR' },
      warehouse: { pickingStrategy: 'batch', docks: 14 },
      inventory: { consignment: true },
    },
    runs: { 'warehouse.wave-release': { cron: '*/10 5-23 * * 1-6', timeZone: 'Europe/Amsterdam' } },
    since: 90,
    createdBy: 'edsger',
  },
  {
    // A new customer: the pin starts at their go-live.
    version: 'v2026',
    name: 'adventure-works',
    title: 'Adventure Works (v2026 pin)',
    description: 'New customer; the channel opens at their go-live.',
    labels: ['pin'],
    customer: {
      title: 'Adventure Works Cycles',
      crmId: 'LL-CRM-0590',
      contact: 'erp-project@adventure-works.example',
      contract: 'PIN-2026-014',
    },
    timeZone: 'Europe/Helsinki',
    region: 'northeurope',
    maintenanceWindow: 'sun 03:00-05:00 Europe/Helsinki',
    modules: ['auth', 'billing', 'hr', 'crm', 'reporting'],
    jobs: ['billing.month-end-close', 'hr.payroll-export'],
    rollouts: { billing: { invoiceCurrency: 'EUR' }, hr: { payrollCountry: 'FI' } },
    runs: {
      'hr.payroll-export': { target: 'incomes-register', format: 'tulorekisteri-xml', timeZone: 'Europe/Helsinki' },
    },
    lifecycle: { startsIn: 10 },
    since: 6,
    createdBy: 'edsger',
  },
  {
    version: 'v2027',
    name: 'preview',
    title: 'v2027 preview',
    description: 'Early adopters try the next major version on copies of their data.',
    labels: ['preview'],
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    maintenanceWindow: 'wed 18:00-20:00 Europe/Oslo',
    settings: { logLevel: 'info', copyOfProductionData: true },
    modules: versionOf('v2027').modules,
    jobs: ['billing.month-end-close', 'auth.key-rotation'],
    values: { waitlist: 37 },
    since: 20,
    createdBy: 'barbara',
  },
]

const channelOf = (version: VersionName, name: string) =>
  channels.find((channel) => channel.version === version && channel.name === name)!

/**
 * Deleted in production: Northwind's v2024 pin was lifted, so the channel went to Deleted with its rollouts (as the
 * documentation describes), and Litware moved payroll to a local provider.
 */
const deletions: { key: string; daysAgo: number; by: PersonaId }[] = [
  { key: 'cnt.v2024.northwind', daysAgo: 20, by: 'barbara' },
  { key: 'exe.v2025.hr.litware', daysAgo: 6, by: 'edsger' },
]

function addModules(b: ScopeBuilder, names: readonly string[], next: boolean) {
  for (const module of modules.filter((candidate) => names.includes(candidate.name))) {
    b.responsibility(module.name, {
      title: module.title,
      description: module.description,
      labels: module.labels,
      link: `${docs}/modules/${module.name}`,
      values: { codeOwner: module.team, sourcePath: module.folder },
      createdDaysAgo: module.since,
    })
    // Release 2026.4 teaches billing e-invoicing, off by default: only the channels that get the e-invoicing module
    // turn it on, so a pinned channel or an older line never inherits a feature its build does not have.
    const defaults =
      next && module.name === 'billing'
        ? [...module.defaults, { ...module.defaults.at(-1), eInvoice: false }]
        : module.defaults
    b.configure(`rst.${module.name}`, defaults, next && module.name === 'billing' ? { every: 9 } : module.history)
  }
}

function addJobs(b: ScopeBuilder, list: readonly Job[]) {
  for (const job of list) {
    b.unit(job.module, job.name, {
      unitType: job.unitType,
      definition: job.definition,
      title: job.title,
      description: job.description,
      link: `${docs}/jobs/${job.module}-${job.name}`,
      createdDaysAgo: Math.min(moduleOf(job.module).since, 900),
    })
    b.configure(`unt.${job.module}.${job.name}`, job.parameters, { every: 30, lastDaysAgo: 4 })
  }
}

/** The version, its database defaults and the builds of its modules. */
function addVersion(b: ScopeBuilder, version: Version, names: readonly string[], next: boolean) {
  const release = next ? version.next : undefined
  b.subject(version.name, {
    title: version.title,
    description: version.description,
    labels: version.labels,
    link: `${docs}/versions/${version.name}`,
    // The release tag is a fact of the version; the rollout template reads it with a binding.
    values: {
      releaseTag: release?.releaseTag ?? version.releaseTag,
      branch: version.branch ?? `release/${version.year}`,
      releaseManager: version.releaseManager,
    },
    createdDaysAgo: version.since,
    ...version.lifecycle,
    ...version.meta,
  })
  const schemaVersions = release ? [...version.schemaVersions, release.schemaVersion] : version.schemaVersions
  b.configure(
    `sbt.${version.name}`,
    schemaVersions.map((schemaVersion) => ({ schemaVersion, runtime: version.runtime, database: 'sql-server-2022' })),
    { every: 75, lastDaysAgo: release ? 1 : (version.lastChangeDaysAgo ?? 12) },
  )

  for (const name of names) {
    const module = moduleOf(name)
    const backport = version.year < module.introduced
    b.usage(version.name, name, {
      title: backport ? `${module.title} backport to ${version.name}` : `${module.title} in ${version.name}`,
      description: backport
        ? `The ${module.title.toLowerCase()} module of v${module.introduced}, backported.`
        : undefined,
      labels: backport ? ['backport'] : undefined,
      createdDaysAgo: backport ? 15 : Math.min(version.since, module.since),
      createdBy: backport ? 'edsger' : undefined,
      ...version.lifecycle,
    })
    b.configure(`usg.${version.name}.${name}`, module.build(version.year), { lastDaysAgo: version.lastChangeDaysAgo })
  }
}

/** The template and the schemas; each needs the annotations it hangs on. */
function rules(b: ScopeBuilder, next: boolean) {
  // Every rollout inherits the maintenance defaults; its release tag comes from the version it rolls out.
  b.template(
    'exe',
    [
      { drainMinutes: 10, readOnlyDuringMigration: true, backupBeforeMigration: true },
      {
        drainMinutes: 15,
        readOnlyDuringMigration: true,
        backupBeforeMigration: true,
        healthCheck: '/health/ready',
        releaseTag: '@annotation("/releaseTag", "Subject")',
      },
    ],
    { every: 60 },
  )

  // A version cannot be saved without the schema version of its database; the release candidate also asks for the
  // runtime, which its migrations depend on.
  const schemaVersion = { type: 'integer', minimum: 1 }
  b.typeSchema('sbt', [
    { type: 'object', properties: { schemaVersion }, required: ['schemaVersion'] },
    ...(next
      ? [
          {
            type: 'object',
            properties: { schemaVersion, runtime: { enum: ['dotnet-6', 'dotnet-8', 'dotnet-10'] } },
            required: ['schemaVersion', 'runtime'],
          },
        ]
      : []),
  ])

  // A build of billing is a set of flags: booleans, so a later level can turn one off (an array would only grow).
  if (b.has('rst.billing')) {
    b.descendantSchema('rst.billing', 'usg', { type: 'object', additionalProperties: { type: 'boolean' } })
  }
  // LTS patches roll out only inside the window a channel agreed, so every v2025 channel names one.
  if (b.has('sbt.v2025')) {
    b.descendantSchema('sbt.v2025', 'cnt', {
      type: 'object',
      properties: {
        endpoint: { type: 'string', pattern: '^https://' },
        maintenanceWindow: {
          type: 'string',
          pattern: '^(mon|tue|wed|thu|fri|sat|sun) [0-2][0-9]:[0-5][0-9]-[0-2][0-9]:[0-5][0-9] [A-Za-z]+/[A-Za-z_]+$',
        },
      },
      required: ['endpoint', 'maintenanceWindow'],
    })
  }
}

/** The channel, its document, the rollouts of its modules and the runs of its jobs. */
function addChannel(b: ScopeBuilder, channel: Channel, skip: (key: string) => boolean = () => false) {
  const { version, name } = channel
  if (skip(`cnt.${version}.${name}`)) {
    return
  }
  const lifecycle = channel.lifecycle
  const timeZone = channel.timeZone
  const customer = channel.customer
  b.context(version, name, {
    title: channel.title,
    description: channel.description,
    labels: channel.labels,
    timeZone,
    values: customer
      ? {
          customer: customer.title,
          crmId: customer.crmId,
          customerContact: customer.contact,
          contract: customer.contract,
          ...channel.values,
        }
      : channel.values,
    createdDaysAgo: channel.since,
    createdBy: channel.createdBy,
    ...lifecycle,
    ...channel.meta,
  })
  const current = {
    endpoint: endpoint(version, name, channel.environment),
    ...(channel.maintenanceWindow ? { maintenanceWindow: channel.maintenanceWindow } : {}),
    region: channel.region,
    logLevel: 'warn',
    ...channel.settings,
  }
  const lastDaysAgo = channel.lastChangeDaysAgo
  b.configure(
    `cnt.${version}.${name}`,
    [...(channel.history ?? []).map((change) => ({ ...current, ...change })), current],
    { every: 40, lastDaysAgo: lastDaysAgo ?? 5 },
  )

  for (const module of channel.modules) {
    if (skip(`exe.${version}.${module}.${name}`)) {
      continue
    }
    b.execution(version, module, name, {
      title: `${moduleOf(module).title}, ${channel.title}`,
      timeZone,
      createdDaysAgo: Math.min(channel.since, moduleOf(module).since),
      createdBy: channel.createdBy,
      ...lifecycle,
      ...channel.rolloutMeta?.[module],
    })
    const content = channel.rollouts?.[module]
    if (content) {
      b.configure(`exe.${version}.${module}.${name}`, content, { lastDaysAgo })
    }
  }

  for (const key of channel.jobs) {
    const job = jobOf(key)
    b.unitOfExecution(version, job.module, name, job.name, {
      title: `${job.title}, ${channel.title}`,
      timeZone,
      createdDaysAgo: Math.min(channel.since, moduleOf(job.module).since),
      createdBy: channel.createdBy,
      ...lifecycle,
      ...channel.runMeta?.[key],
    })
    const content = channel.runs?.[key]
    if (content) {
      b.configure(`uxe.${version}.${job.module}.${name}.${job.name}`, content, { lastDaysAgo })
    }
  }
}

/** The v2026 channels that get the e-invoicing module in release 2026.4. */
const eInvoicingChannels = ['production', 'early-access']

/** In release 2026.4, billing turns e-invoicing on where the module is rolled out, as a new version of the rollout. */
function withEInvoicing(channel: Channel): Channel {
  if (channel.version !== 'v2026' || !eInvoicingChannels.includes(channel.name)) {
    return channel
  }
  const billing = channel.rollouts?.billing
  const history = billing ? [billing].flat() : []
  return { ...channel, rollouts: { ...channel.rollouts, billing: [...history, { ...history.at(-1), eInvoice: true }] } }
}

/** Release 2026.4: e-invoicing joins v2026 on production and its early-access ring. */
function releaseChanges(b: ScopeBuilder) {
  const meta = { createdDaysAgo: 30, createdBy: 'edsger' as const }
  b.responsibility('e-invoicing', {
    title: 'E-invoicing',
    labels: ['finance', 'new'],
    description: 'Sends and receives PEPPOL BIS Billing 3 invoices through the access point.',
    link: `${docs}/modules/e-invoicing`,
    values: { codeOwner: '@ledgerline/finance', sourcePath: 'src/Modules/EInvoicing' },
    ...meta,
  })
  b.configure('rst.e-invoicing', {
    accessPoint: 'https://ap.peppol.ledgerline.example',
    profile: 'peppol-bis-billing-3',
    certificate: '@(erp.peppol.ap-certificate, primaryVault)',
  })
  b.usage('v2026', 'e-invoicing', { title: 'E-invoicing in v2026', ...meta })
  b.configure('usg.v2026.e-invoicing', { inbound: true, outbound: true })
  for (const channel of eInvoicingChannels) {
    b.execution('v2026', 'e-invoicing', channel, {
      title: `E-invoicing, ${channelOf('v2026', channel).title}`,
      ...meta,
    })
  }
  b.configure('exe.v2026.e-invoicing.production', { instances: 2 })
  // The 2027 preview is built from main, so it gets the module as well, from the start of the preview.
  const preview = { ...versionOf('v2027').lifecycle, createdDaysAgo: 18, createdBy: 'edsger' as const }
  b.usage('v2027', 'e-invoicing', { title: 'E-invoicing in v2027', ...preview })
  b.execution('v2027', 'e-invoicing', 'preview', {
    title: `E-invoicing, ${channelOf('v2027', 'preview').title}`,
    ...preview,
  })
}

/** Production, the full catalog; the release candidate is a copy with the next release's changes. */
function catalog(b: ScopeBuilder, next: boolean) {
  // The release candidate copies production as it is now, without what production has deleted.
  const skip = (key: string) => next && deletions.some((deletion) => deletion.key === key)
  addModules(
    b,
    modules.map((module) => module.name),
    next,
  )
  addJobs(b, jobs)
  for (const version of versions) {
    addVersion(b, version, version.modules, next)
  }
  rules(b, next)
  // The end of support and the start of the preview apply to everything rolled out on the version, and the channels
  // of a frozen line are not edited after it.
  for (const channel of channels) {
    const version = versionOf(channel.version)
    const current: Channel = {
      ...channel,
      lifecycle: { ...version.lifecycle, ...channel.lifecycle },
      lastChangeDaysAgo: channel.lastChangeDaysAgo ?? version.lastChangeDaysAgo,
    }
    addChannel(b, next ? withEInvoicing(current) : current, skip)
  }

  if (next) {
    releaseChanges(b)
    return
  }
  for (const { key, daysAgo, by } of deletions) {
    b.trash(key, { daysAgo, by })
  }
}

/** Development: v2026 and the v2027 work in progress on developer channels, with sandbox endpoints and debug logs. */
function development(b: ScopeBuilder) {
  addModules(b, ['auth', 'billing', 'reporting', 'inventory'], false)
  addJobs(b, [jobOf('billing.month-end-close'), jobOf('reporting.nightly-refresh')])
  addVersion(b, versionOf('v2026'), ['auth', 'billing', 'reporting', 'inventory'], false)
  // On the developers' machines the next major version is no preview yet; it is where the work happens, on main, a
  // nightly ahead of the published preview.
  const main: Version = {
    ...versionOf('v2027'),
    title: 'ERP 2027 (main)',
    labels: ['next'],
    description: 'The next major version as it is built from main every night.',
    branch: 'main',
    releaseTag: '2027.0.0-preview.3.dev.14',
    lifecycle: undefined,
  }
  addVersion(b, main, ['auth', 'billing', 'inventory'], false)
  rules(b, false)

  const developer = (version: VersionName, name: string, owner: PersonaId, branch: string): Channel => ({
    version,
    name,
    title: `${version} ${name}`,
    labels: ['dev'],
    timeZone: 'Europe/Oslo',
    region: 'norwayeast',
    modules: [],
    jobs: [],
    values: { owner: `${owner}@ledgerline.example`, branch },
    // Developer settings: debug logs, the bank's sandbox, a mail catcher and a generated set of test companies.
    settings: {
      logLevel: 'debug',
      bankApi: 'https://sandbox.bank.example/v1',
      mailSink: 'smtp://mailpit.dev.ledgerline.example:1025',
      seedCompanies: b.rng.int(3, 9),
      seedInvoices: b.rng.int(20, 80) * 25,
    },
    since: b.rng.int(20, 120),
    createdBy: owner,
  })
  addChannel(b, {
    ...developer('v2026', 'dev', 'edsger', 'release/2026'),
    modules: ['auth', 'billing', 'reporting', 'inventory'],
    jobs: ['reporting.nightly-refresh'],
  })
  addChannel(b, {
    ...developer('v2026', 'qa', 'barbara', 'release/2026'),
    modules: ['auth', 'billing', 'reporting'],
    jobs: ['billing.month-end-close'],
    // QA closes a fake month every half hour, so the regression suite can check the close.
    runs: {
      'billing.month-end-close': { cron: '*/30 * * * *', timeZone: 'Europe/Oslo', ledgerApi: '@config("/endpoint")' },
    },
  })
  addChannel(b, {
    ...developer('v2027', 'dev', 'ada', 'main'),
    modules: ['auth', 'billing', 'inventory'],
    jobs: ['billing.month-end-close'],
    // The 2027 build already sends e-invoices; Ada tries the next PDF renderer.
    rollouts: { billing: { debugPdf: true, pdfRenderer: 'v4' } },
  })
}

/** The board's staging view: v2025 on Contoso's channel, where the reporting backport is tested before it ships. */
function staging(b: ScopeBuilder) {
  addModules(b, ['billing', 'reporting'], false)
  addVersion(b, versionOf('v2025'), ['billing', 'reporting'], false)
  rules(b, false)
  const contoso = channelOf('v2025', 'contoso')
  addChannel(b, {
    ...contoso,
    title: 'Contoso staging (v2025)',
    environment: 'staging',
    settings: { logLevel: 'info' },
    history: undefined,
    rolloutMeta: undefined,
    modules: ['billing', 'reporting'],
    rollouts: { ...contoso.rollouts, reporting: { reportPack: 'contoso-group', vatReturn: 'altinn-3' } },
    since: 30,
  })
}

/** Feature delivery 1: the bank statement import of billing on v2026, piloted with Tailspin Toys. */
function featureDelivery(b: ScopeBuilder) {
  addModules(b, ['billing'], false)
  addJobs(b, [jobOf('billing.month-end-close'), bankStatementImport])
  addVersion(b, versionOf('v2026'), ['billing'], false)
  rules(b, false)
  const production = channelOf('v2026', 'production')
  addChannel(b, {
    ...production,
    history: undefined,
    modules: ['billing'],
    jobs: ['billing.month-end-close', 'billing.bank-statement-import'],
    // Only the runs of the channel's jobs get a document, so the board's month-end close keeps its own.
    runs: { ...production.runs, 'billing.bank-statement-import': { pollMinutes: 15, autoPostBelow: 0 } },
  })
  addChannel(b, {
    ...channelOf('v2026', 'tailspin'),
    modules: ['billing'],
    jobs: ['billing.bank-statement-import'],
    runs: {
      'billing.bank-statement-import': {
        sftpKey: '@(tailspin.bank-sftp-key, primaryVault)',
        bankAccount: 'DE89 3704 0044 0532 0130 00',
        autoPostBelow: 500,
      },
    },
  })
}

export const erp: ShowcaseProject = {
  org: 'ledgerline',
  project: 'erp',
  summary:
    'The versioned monolith of the documentation: one ERP codebase with v2025 in long-term support and v2026 current, their channels, customer pins and modules.',
  members: { ada: 'Owner', barbara: 'Administrator', edsger: 'Contributor', alan: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Module', color: 'indigo', icon: 'i-lucide-puzzle' },
      unt: { label: 'Job', color: 'fuchsia', icon: 'i-lucide-clock' },
      sbt: { label: 'Version', color: 'sky', icon: 'i-lucide-git-branch' },
      usg: { label: 'Build', color: 'amber', icon: 'i-lucide-package' },
      cnt: { label: 'Channel', color: 'teal', icon: 'i-lucide-network' },
      exe: { label: 'Rollout', color: 'green', icon: 'i-lucide-rocket' },
      uxe: { label: 'JobRun', color: 'orange', icon: 'i-lucide-play' },
    },
    defaultTimeZone: 'Europe/Oslo',
    dateFormat: 'yyyy-mm-dd',
  },
  views: environmentViews({
    development: 'Development: v2026 and v2027 on developer channels',
    staging: 'Staging: the reporting backport to v2025 for Contoso',
    'release-candidate': 'Release 2026.4: e-invoicing and database schema 19',
    'feature-delivery1': 'Bank statement import in billing on v2026',
  }),
  build: (b) => {
    switch (b.view) {
      case 'default':
        return catalog(b, false)
      case 'release-candidate':
        return catalog(b, true)
      case 'development':
        return development(b)
      case 'staging':
        return staging(b)
      case 'feature-delivery1':
        return featureDelivery(b)
    }
  },
}
