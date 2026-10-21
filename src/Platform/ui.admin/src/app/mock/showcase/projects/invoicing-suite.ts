import type { ScopeBuilder } from '../builder'
import { evolve } from '../history'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// The B2B SaaS of the documentation (docs/42for.net/platform/examples/b2b-saas.md), example 3 of the Miro board:
// Northwind buys invoicing, payments and the portal in production and a sandbox on the enterprise plan; Contoso buys
// invoicing in production on the standard plan. More customers of the same kind make it a catalog of hundreds.

const docs = 'https://docs.fabrikam.example/invoicing-suite'
const host = (customer: string, environment: string) => `${customer}.${environment}.invoicing.fabrikam.example`

/** The two plans of the documentation. */
type Plan = 'enterprise' | 'standard'

interface Customer {
  name: string
  title: string
  residency: 'eu' | 'uk' | 'us'
  region: string
  plan: Plan
  modules: string[]
  /** Environments besides production. */
  environments: string[]
  tier: 'premium' | 'standard'
  /** Months a customer for the Created date. */
  since: number
}

const seats: Record<Plan, number> = { enterprise: 250, standard: 40 }

/** The customers besides Northwind and Contoso, whose setup the documentation spells out. */
const customers: Customer[] = [
  {
    name: 'adatum',
    title: 'Adatum Corporation',
    residency: 'eu',
    region: 'westeurope',
    plan: 'enterprise',
    modules: ['invoicing', 'payments', 'dunning'],
    environments: ['sandbox'],
    tier: 'premium',
    since: 30,
  },
  {
    name: 'relecloud',
    title: 'Relecloud',
    residency: 'us',
    region: 'eastus',
    plan: 'standard',
    modules: ['invoicing', 'portal'],
    environments: ['pilot'],
    tier: 'standard',
    since: 4,
  },
  {
    name: 'trey-research',
    title: 'Trey Research',
    residency: 'uk',
    region: 'uksouth',
    plan: 'standard',
    modules: ['invoicing', 'credit-notes'],
    environments: ['sandbox'],
    tier: 'standard',
    since: 22,
  },
  {
    name: 'wingtip-toys',
    title: 'Wingtip Toys',
    residency: 'us',
    region: 'westus2',
    plan: 'standard',
    modules: ['invoicing'],
    environments: [],
    tier: 'standard',
    since: 11,
  },
  {
    name: 'blue-yonder',
    title: 'Blue Yonder Airlines',
    residency: 'eu',
    region: 'northeurope',
    plan: 'enterprise',
    modules: ['invoicing', 'payments', 'portal', 'reporting'],
    environments: ['sandbox', 'uat'],
    tier: 'premium',
    since: 40,
  },
  {
    name: 'southridge-video',
    title: 'Southridge Video',
    residency: 'us',
    region: 'centralus',
    plan: 'standard',
    modules: ['invoicing', 'payments'],
    environments: [],
    tier: 'standard',
    since: 17,
  },
  {
    name: 'humongous-insurance',
    title: 'Humongous Insurance',
    residency: 'eu',
    region: 'germanywestcentral',
    plan: 'enterprise',
    modules: ['invoicing', 'dunning', 'reporting'],
    environments: ['uat'],
    tier: 'premium',
    since: 35,
  },
  {
    name: 'vanarsdel',
    title: 'VanArsdel, Ltd.',
    residency: 'uk',
    region: 'ukwest',
    plan: 'standard',
    modules: ['invoicing', 'portal'],
    environments: [],
    tier: 'standard',
    since: 9,
  },
  {
    name: 'graphic-design-institute',
    title: 'Graphic Design Institute',
    residency: 'eu',
    region: 'francecentral',
    plan: 'standard',
    modules: ['invoicing'],
    environments: [],
    tier: 'standard',
    since: 6,
  },
  {
    name: 'school-of-fine-art',
    title: 'School of Fine Art',
    residency: 'eu',
    region: 'westeurope',
    plan: 'standard',
    modules: ['invoicing', 'portal'],
    environments: [],
    tier: 'standard',
    since: 13,
  },
  {
    name: 'consolidated-messenger',
    title: 'Consolidated Messenger',
    residency: 'us',
    region: 'eastus2',
    plan: 'enterprise',
    modules: ['invoicing', 'payments', 'credit-notes', 'reporting'],
    environments: ['sandbox'],
    tier: 'premium',
    since: 28,
  },
  {
    name: 'northridge-fitness',
    title: 'Northridge Fitness',
    residency: 'eu',
    region: 'swedencentral',
    plan: 'standard',
    modules: ['invoicing', 'payments'],
    environments: [],
    tier: 'standard',
    since: 8,
  },
  {
    name: 'liberty-delightful',
    title: 'Liberty’s Delightful Sinful Bakery',
    residency: 'us',
    region: 'southcentralus',
    plan: 'standard',
    modules: ['invoicing'],
    environments: [],
    tier: 'standard',
    since: 3,
  },
  {
    name: 'tailwind-traders',
    title: 'Tailwind Traders',
    residency: 'eu',
    region: 'westeurope',
    plan: 'enterprise',
    modules: ['invoicing', 'payments', 'portal', 'reporting'],
    environments: ['sandbox', 'uat'],
    tier: 'premium',
    since: 26,
  },
  {
    name: 'lamna-healthcare',
    title: 'Lamna Healthcare Company',
    residency: 'eu',
    region: 'northeurope',
    plan: 'enterprise',
    modules: ['invoicing', 'dunning', 'credit-notes'],
    environments: ['uat'],
    tier: 'premium',
    since: 19,
  },
  {
    name: 'fincher-architects',
    title: 'Fincher Architects',
    residency: 'uk',
    region: 'uksouth',
    plan: 'standard',
    modules: ['invoicing'],
    environments: [],
    tier: 'standard',
    since: 7,
  },
  {
    name: 'bellows-college',
    title: 'Bellows College',
    residency: 'us',
    region: 'eastus',
    plan: 'standard',
    modules: ['invoicing', 'portal'],
    environments: ['sandbox'],
    tier: 'standard',
    since: 15,
  },
  {
    name: 'proseware',
    title: 'Proseware, Inc.',
    residency: 'us',
    region: 'westus3',
    plan: 'standard',
    modules: ['invoicing', 'payments', 'credit-notes'],
    environments: [],
    tier: 'standard',
    since: 12,
  },
  {
    name: 'woodgrove-bank',
    title: 'Woodgrove Bank',
    residency: 'uk',
    region: 'ukwest',
    plan: 'enterprise',
    modules: ['invoicing', 'payments', 'dunning', 'reporting'],
    environments: ['sandbox'],
    tier: 'premium',
    since: 33,
  },
  {
    name: 'munson-pickles',
    title: 'Munson’s Pickles and Preserves Farm',
    residency: 'us',
    region: 'eastus',
    plan: 'standard',
    modules: ['invoicing', 'portal'],
    environments: ['sandbox'],
    tier: 'standard',
    since: 0,
  },
]

const modules: { name: string; title: string; labels: string[]; description: string }[] = [
  {
    name: 'invoicing',
    title: 'Invoicing',
    labels: ['core'],
    description: 'Issues invoices, credit notes and e-invoices; exports the ledger every night.',
  },
  {
    name: 'payments',
    title: 'Payments',
    labels: ['core', 'pci'],
    description: 'Collects card and bank payments and settles them against open invoices.',
  },
  {
    name: 'portal',
    title: 'Customer portal',
    labels: ['web'],
    description: 'Where the customer’s own clients download invoices and pay them.',
  },
  { name: 'dunning', title: 'Dunning', labels: ['add-on'], description: 'Reminds and escalates overdue invoices.' },
  {
    name: 'credit-notes',
    title: 'Credit notes',
    labels: ['add-on'],
    description: 'Corrections and refunds of issued invoices.',
  },
  { name: 'reporting', title: 'Reporting', labels: ['add-on'], description: 'Revenue, aging and tax reports.' },
]

function addModules(b: ScopeBuilder) {
  for (const module of modules) {
    b.responsibility(module.name, {
      title: module.title,
      description: module.description,
      labels: module.labels,
      link: `${docs}/modules/${module.name}`,
    })
  }
  b.unit('invoicing', 'nightly-export', {
    unitType: 'time',
    definition: '0 0 * * *',
    title: 'Nightly ledger export',
    description: 'Exports the day’s invoices to the customer’s accounting system.',
  })
  b.unit('dunning', 'reminder-letters', { unitType: 'time', definition: '0 6 * * 1-5', title: 'Reminder letters' })
  b.unit('reporting', 'month-end', { unitType: 'time', definition: '0 3 1 * *', title: 'Month-end reports' })
}

function moduleDocuments(b: ScopeBuilder, next: boolean) {
  // Product defaults of invoicing, tuned over a year and a half: the long history of the project.
  const invoicing = evolve(
    { baseCurrency: 'EUR', retryPolicy: { attempts: 3, backoffSeconds: 30 }, features: ['pdf', 'email'] },
    18,
    (version) =>
      version === 4
        ? { features: ['pdf', 'email', 'credit-notes'] }
        : version === 9
          ? { features: ['pdf', 'email', 'credit-notes', 'e-invoice'] }
          : version === 14
            ? { retryPolicy: { attempts: 5 } }
            : { retryPolicy: { backoffSeconds: 30 + (version % 4) * 15 } },
  )
  if (next) {
    invoicing.push({ ...invoicing.at(-1)!, features: ['pdf', 'email', 'credit-notes', 'e-invoice', 'peppol'] })
  }
  b.configure('rst.invoicing', invoicing, { every: 9, lastDaysAgo: next ? 1 : 3 })
  b.configure(
    'rst.payments',
    [
      { providers: ['adyen'], settlementDay: 'mon', captureDelayHours: 24 },
      { providers: ['adyen', 'stripe'], settlementDay: 'mon', captureDelayHours: 24 },
      { providers: ['adyen', 'stripe'], settlementDay: 'mon', captureDelayHours: 0 },
    ],
    { every: 40 },
  )
  b.configure('rst.portal', { theme: 'light', sessionTimeoutMinutes: 30, languages: ['en', 'nb', 'de', 'fr'] })
  b.configure('rst.dunning', [
    { steps: [{ afterDays: 7, channel: 'email' }] },
    {
      steps: [
        { afterDays: 7, channel: 'email' },
        { afterDays: 21, channel: 'letter' },
      ],
    },
  ])
  b.configure('unt.invoicing.nightly-export', { format: 'csv', target: 'sftp', includeDrafts: false })
  b.configure('unt.dunning.reminder-letters', { printer: 'post-nord', template: 'reminder-v2' })

  // Every job run inherits the retry and notification defaults of its type.
  b.template(
    'uxe',
    evolve({ retries: 2, timeoutMinutes: 30 }, 3, (version) =>
      version === 2 ? { notifyOnFailure: ['ops@fabrikam.example'] } : { timeoutMinutes: 45 },
    ),
  )
}

/** Northwind and Contoso exactly as the documentation describes them. */
function documentedCustomers(b: ScopeBuilder) {
  b.subject('northwind', {
    title: 'Northwind Traders',
    labels: ['enterprise', 'nordics'],
    values: { crmId: 'CRM-1001', accountManager: 'Grace Hopper' },
    timeZone: 'Europe/Oslo',
  })
  b.configure(
    'sbt.northwind',
    evolve({ legalName: 'Northwind Traders AS', residency: 'eu', supportTier: 'standard' }, 4, (version) =>
      version === 3 ? { supportTier: 'premium' } : { legalName: 'Northwind Traders AS' },
    ),
    { every: 45 },
  )
  // A new Northwind instance cannot be saved without a region (b2b-saas.md).
  b.descendantSchema('sbt.northwind', 'cnt', {
    type: 'object',
    properties: { region: { type: 'string', minLength: 1 } },
    required: ['region'],
  })
  b.context('northwind', 'production', { title: 'Northwind production', timeZone: 'Europe/Oslo' })
  b.configure('cnt.northwind.production', [
    { region: 'norwayeast', logLevel: 'info', hostname: host('northwind', 'production') },
    { region: 'norwayeast', logLevel: 'warn', hostname: host('northwind', 'production') },
  ])
  b.context('northwind', 'sandbox', { title: 'Northwind sandbox', labels: ['test'], timeZone: 'Europe/Oslo' })
  b.configure('cnt.northwind.sandbox', {
    region: 'norwayeast',
    logLevel: 'debug',
    hostname: host('northwind', 'sandbox'),
  })

  // The commercial slice: plan, seats and the contracted features.
  for (const module of ['invoicing', 'payments', 'portal']) {
    b.usage('northwind', module, { title: `Northwind ${module} contract` })
    b.configure(`usg.northwind.${module}`, {
      plan: 'enterprise',
      seats: seats.enterprise,
      ...(module === 'invoicing' ? { features: ['e-invoice', 'custom-tax'] } : {}),
    })
  }

  b.execution('northwind', 'invoicing', 'production', { title: 'Northwind production invoicing' })
  // The one-off: a custom tax profile and a limit that exist only in production invoicing.
  b.configure('exe.northwind.invoicing.production', [
    { taxProfile: 'no-mva-standard' },
    { taxProfile: 'no-mva-custom', invoiceLimit: 500000 },
    { taxProfile: 'no-mva-custom', invoiceLimit: 750000 },
  ])
  b.execution('northwind', 'invoicing', 'sandbox', { title: 'Northwind sandbox invoicing' })
  b.configure('exe.northwind.invoicing.sandbox', { taxProfile: 'no-mva-custom', invoiceLimit: 1000 })
  b.execution('northwind', 'payments', 'production')
  b.configure('exe.northwind.payments.production', { merchantId: '@(northwind.adyen-merchant, primaryVault)' })
  b.execution('northwind', 'portal', 'production')
  b.configure('exe.northwind.portal.production', { customDomain: 'billing.northwind.example', branding: 'northwind' })
  b.execution('northwind', 'portal', 'sandbox')

  // Northwind's export runs at 02:00 in Oslo; the sandbox one is off, so a test tenant does not export real files.
  b.unitOfExecution('northwind', 'invoicing', 'production', 'nightly-export', { title: 'Northwind nightly export' })
  b.configure('uxe.northwind.invoicing.production.nightly-export', [
    { cron: '0 1 * * *', timeZone: 'Europe/Oslo' },
    { cron: '0 2 * * *', timeZone: 'Europe/Oslo' },
  ])
  b.unitOfExecution('northwind', 'invoicing', 'sandbox', 'nightly-export', {
    disabled: true,
    description: 'Off, so the sandbox never exports real files.',
  })

  b.subject('contoso', {
    title: 'Contoso Ltd.',
    labels: ['standard'],
    values: { crmId: 'CRM-1002', accountManager: 'Alan Turing' },
  })
  b.configure('sbt.contoso', { legalName: 'Contoso Ltd.', residency: 'eu', supportTier: 'standard' })
  b.context('contoso', 'production', { title: 'Contoso production' })
  b.configure('cnt.contoso.production', {
    region: 'westeurope',
    logLevel: 'warn',
    hostname: host('contoso', 'production'),
  })
  b.usage('contoso', 'invoicing', { title: 'Contoso invoicing contract' })
  b.configure('usg.contoso.invoicing', { plan: 'standard', seats: seats.standard })
  b.execution('contoso', 'invoicing', 'production')
  // Contoso keeps the module's schedule.
  b.unitOfExecution('contoso', 'invoicing', 'production', 'nightly-export')
}

function otherCustomers(b: ScopeBuilder) {
  customers.forEach((customer, index) => {
    const scheduled = customer.since === 0
    const lifecycle = scheduled ? { startsIn: 14, createdDaysAgo: 3, createdBy: 'grace' as const } : {}
    b.subject(customer.name, {
      title: customer.title,
      labels: [customer.plan, customer.residency],
      values: { crmId: `CRM-${1003 + index}`, accountManager: index % 2 ? 'Alan Turing' : 'Grace Hopper' },
      createdDaysAgo: scheduled ? 3 : customer.since * 30,
      ...lifecycle,
    })
    b.configure(`sbt.${customer.name}`, {
      legalName: customer.title,
      residency: customer.residency,
      supportTier: customer.tier,
    })

    for (const environment of ['production', ...customer.environments]) {
      const pilot = environment === 'pilot'
      b.context(customer.name, environment, {
        labels: environment === 'production' ? undefined : ['test'],
        // The pilot ended when Relecloud signed for production.
        ...(pilot ? { expiresIn: -20, description: 'Pilot environment, ended after the production go-live.' } : {}),
        ...lifecycle,
      })
      b.configure(`cnt.${customer.name}.${environment}`, {
        region: customer.region,
        logLevel: environment === 'production' ? 'warn' : 'debug',
        hostname: host(customer.name, environment),
      })
    }

    for (const module of customer.modules) {
      // Humongous Insurance gave notice; their invoicing contract ends in a month.
      const ending = customer.name === 'humongous-insurance' && module === 'invoicing'
      b.usage(customer.name, module, { ...(ending ? { expiresIn: 30, labels: ['notice-given'] } : {}), ...lifecycle })
      b.configure(`usg.${customer.name}.${module}`, { plan: customer.plan, seats: seats[customer.plan] })

      for (const environment of ['production', ...customer.environments]) {
        b.execution(customer.name, module, environment, lifecycle)
        if (module === 'invoicing') {
          b.configure(`exe.${customer.name}.invoicing.${environment}`, {
            taxProfile: `${customer.residency}-standard`,
            invoiceLimit: environment === 'production' ? 100000 * (customer.plan === 'enterprise' ? 5 : 1) : 1000,
          })
          if (environment === 'production') {
            b.unitOfExecution(customer.name, 'invoicing', environment, 'nightly-export', lifecycle)
          }
        }
        if (module === 'dunning' && environment === 'production') {
          b.unitOfExecution(customer.name, 'dunning', environment, 'reminder-letters', lifecycle)
        }
        if (module === 'reporting' && environment === 'production') {
          b.unitOfExecution(customer.name, 'reporting', environment, 'month-end', lifecycle)
        }
      }
    }
  })
  b.configure('uxe.blue-yonder.invoicing.production.nightly-export', { cron: '30 23 * * *', timeZone: 'Europe/Dublin' })
}

function production(b: ScopeBuilder, next: boolean) {
  addModules(b)
  moduleDocuments(b, next)
  // A new customer of invoicing cannot be saved without a plan (b2b-saas.md); the release candidate adds seats.
  b.descendantSchema('rst.invoicing', 'usg', [
    { type: 'object', properties: { plan: { enum: ['standard', 'enterprise'] } }, required: ['plan'] },
    ...(next
      ? [
          {
            type: 'object',
            properties: {
              plan: { enum: ['standard', 'enterprise'] },
              seats: { type: 'integer', minimum: 1 },
            },
            required: ['plan', 'seats'],
          },
        ]
      : []),
  ])
  documentedCustomers(b)
  otherCustomers(b)

  if (next) {
    // The next release brings the e-archive module to Northwind.
    b.responsibility('e-archive', {
      title: 'E-archive',
      labels: ['add-on'],
      description: 'Ten-year archive of issued invoices.',
    })
    b.usage('northwind', 'e-archive')
    b.configure('usg.northwind.e-archive', { plan: 'enterprise', seats: seats.enterprise, retentionYears: 10 })
    b.execution('northwind', 'e-archive', 'production')
    return
  }

  // Trey Research dropped their sandbox with its deployments; a portal deployment of VanArsdel was removed alone.
  b.trash('cnt.trey-research.sandbox', { daysAgo: 18, by: 'grace' })
  b.trash('exe.vanarsdel.portal.production', { daysAgo: 5, by: 'alan' })
}

/** The board's development view: Northwind only, its sandbox and a new QA environment. */
function development(b: ScopeBuilder) {
  addModules(b)
  b.subject('northwind', { title: 'Northwind Traders', timeZone: 'Europe/Oslo' })
  for (const environment of ['sandbox', 'qa']) {
    b.context('northwind', environment, { title: `Northwind ${environment}`, labels: ['test'] })
    b.configure(`cnt.northwind.${environment}`, {
      region: 'norwayeast',
      logLevel: 'debug',
      hostname: `northwind.${environment}.dev.invoicing.fabrikam.example`,
    })
  }
  for (const module of ['invoicing', 'portal']) {
    b.usage('northwind', module)
    b.configure(`usg.northwind.${module}`, { plan: 'enterprise', seats: 5 })
  }
  b.execution('northwind', 'invoicing', 'sandbox')
  b.execution('northwind', 'invoicing', 'qa')
  b.configure('exe.northwind.invoicing.qa', { taxProfile: 'no-mva-custom', invoiceLimit: 100, fakeTaxAuthority: true })
  b.execution('northwind', 'portal', 'qa')
  b.unitOfExecution('northwind', 'invoicing', 'qa', 'nightly-export')
  b.configure('uxe.northwind.invoicing.qa.nightly-export', { cron: '*/30 * * * *', timeZone: 'Europe/Oslo' })
}

/** Staging: the documented customers and two more on staging instances. */
function staging(b: ScopeBuilder) {
  addModules(b)
  moduleDocuments(b, false)
  for (const [customer, title, plan, modulesOfCustomer] of [
    ['northwind', 'Northwind Traders', 'enterprise', ['invoicing', 'payments', 'portal']],
    ['contoso', 'Contoso Ltd.', 'standard', ['invoicing']],
    ['adatum', 'Adatum Corporation', 'enterprise', ['invoicing', 'payments', 'dunning']],
    ['blue-yonder', 'Blue Yonder Airlines', 'enterprise', ['invoicing', 'reporting']],
  ] as const) {
    b.subject(customer, { title })
    b.context(customer, 'staging', { labels: ['test'] })
    b.configure(`cnt.${customer}.staging`, {
      region: 'westeurope',
      logLevel: 'info',
      hostname: host(customer, 'staging'),
    })
    for (const module of modulesOfCustomer) {
      b.usage(customer, module)
      b.configure(`usg.${customer}.${module}`, { plan, seats: seats[plan] })
      b.execution(customer, module, 'staging')
    }
    b.unitOfExecution(customer, 'invoicing', 'staging', 'nightly-export')
  }
}

/** The PEPPOL gateway, delivered to Northwind and Contoso first. */
function featureDelivery(b: ScopeBuilder) {
  b.responsibility('invoicing', { title: 'Invoicing', labels: ['core'] })
  b.responsibility('peppol-gateway', {
    title: 'PEPPOL gateway',
    labels: ['feature'],
    description: 'Sends e-invoices through the PEPPOL network.',
    link: `${docs}/modules/peppol-gateway`,
  })
  b.configure('rst.peppol-gateway', { accessPoint: 'https://ap.peppol.fabrikam.example', profile: 'bis-billing-3' })
  for (const [customer, title] of [
    ['northwind', 'Northwind Traders'],
    ['contoso', 'Contoso Ltd.'],
  ] as const) {
    b.subject(customer, { title })
    b.context(customer, 'production')
    b.configure(`cnt.${customer}.production`, {
      region: 'norwayeast',
      logLevel: 'warn',
      hostname: host(customer, 'production'),
    })
    for (const module of ['invoicing', 'peppol-gateway']) {
      b.usage(customer, module)
      b.configure(`usg.${customer}.${module}`, {
        plan: customer === 'northwind' ? 'enterprise' : 'standard',
        seats: 40,
      })
      b.execution(customer, module, 'production')
    }
    b.configure(`exe.${customer}.peppol-gateway.production`, {
      participantId: `0192:${customer === 'northwind' ? '987654321' : '912345678'}`,
    })
  }
}

export const invoicingSuite: ShowcaseProject = {
  org: 'fabrikam',
  project: 'invoicing-suite',
  summary: 'The B2B SaaS of the documentation: Northwind and Contoso, their environments, modules and contracts.',
  members: { grace: 'Owner', edsger: 'Administrator', ada: 'ContributorWithSecrets', alan: 'Contributor' },
  settings: {
    types: {
      rst: { label: 'Module', color: 'violet', icon: 'i-lucide-puzzle' },
      unt: { label: 'Job', color: 'fuchsia', icon: 'i-lucide-clock' },
      sbt: { label: 'Customer', color: 'blue', icon: 'i-lucide-building-2' },
      usg: { label: 'Contract', color: 'amber', icon: 'i-lucide-file' },
      cnt: { label: 'Environment', color: 'teal', icon: 'i-lucide-server' },
      exe: { label: 'Deployment', color: 'emerald', icon: 'i-lucide-rocket' },
      uxe: { label: 'JobRun', color: 'orange', icon: 'i-lucide-workflow' },
    },
    defaultTimeZone: 'Europe/Oslo',
    dateFormat: 'dd.mm.yyyy',
  },
  views: environmentViews({
    development: 'Development: Northwind sandbox and QA',
    staging: 'Staging instances of four customers',
    'release-candidate': 'Release 2026.11: PEPPOL in invoicing, the e-archive module',
    'feature-delivery1': 'PEPPOL gateway for Northwind and Contoso',
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
