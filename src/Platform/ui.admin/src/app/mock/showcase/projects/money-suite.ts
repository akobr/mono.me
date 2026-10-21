import { seededRng, type Meta, type ScopeBuilder } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// The consumer finance suite of the documentation (docs/42for.net/platform/examples/b2c-finance.md), example 4 of the
// Miro board. The person is the subject; each legal entity the person acts as, their "hat", is a context; each product
// of the suite is a responsibility; the execution is the product as used by this person wearing that hat. Alex acts
// as themselves and as the director of Bright Forge and Harbor Studio. Nordic customers of the same kind, each with a
// private hat and some with one or two companies, make it a catalog of hundreds.

const docs = 'https://docs.bluefin.example/money-suite'

type ProductName = 'accounts' | 'payments' | 'cards' | 'investments' | 'savings' | 'insurance' | 'lending'
type Country = 'NO' | 'SE' | 'DK' | 'FI'
type Segment = 'everyday' | 'premium' | 'private-banking' | 'youth' | 'onboarding'
type LegalForm = 'AS' | 'ENK' | 'AB' | 'ApS' | 'Oy'
type Content = Record<string, unknown>

interface CountryInfo {
  currency: string
  language: string
  timeZone: string
  label: string
  /** A mobile number: the country code, the first digits of the mobile ranges and the grouping of the digits. */
  mobile: { code: string; ranges: string[]; groups: number[] }
  /** The tax-favoured share account of the country. */
  wrapper: string
  /** The payments limit of a new private customer, in the country's currency. */
  limit: number
  /** The monthly limit of a youth card. */
  youthLimit: number
}

const countries: Record<Country, CountryInfo> = {
  NO: {
    currency: 'NOK',
    language: 'nb',
    timeZone: 'Europe/Oslo',
    label: 'norway',
    mobile: { code: '+47', ranges: ['4', '9'], groups: [3, 2, 3] },
    wrapper: 'ask',
    limit: 50000,
    youthLimit: 3000,
  },
  SE: {
    currency: 'SEK',
    language: 'sv',
    timeZone: 'Europe/Stockholm',
    label: 'sweden',
    mobile: { code: '+46', ranges: ['70', '72', '73', '76'], groups: [2, 3, 2, 2] },
    wrapper: 'isk',
    limit: 50000,
    youthLimit: 3000,
  },
  DK: {
    currency: 'DKK',
    language: 'da',
    timeZone: 'Europe/Copenhagen',
    label: 'denmark',
    mobile: {
      code: '+45',
      ranges: ['2', '30', '31', '40', '42', '50', '51', '53', '60', '61', '71', '81', '91'],
      groups: [2, 2, 2, 2],
    },
    wrapper: 'aktiesparekonto',
    limit: 35000,
    youthLimit: 2000,
  },
  FI: {
    currency: 'EUR',
    language: 'fi',
    timeZone: 'Europe/Helsinki',
    label: 'finland',
    mobile: { code: '+358', ranges: ['40', '50'], groups: [2, 3, 4] },
    wrapper: 'osakesaastotili',
    limit: 5000,
    youthLimit: 300,
  },
}

interface Product {
  title: string
  /** The product use in a private hat ("Ingrid’s debit card") and in a company ("Fjordlys Design company card"). */
  personal: string
  business: string
  labels: string[]
  description: string
  /** Days since launch, for the Created dates. */
  days: number
}

const productNames: ProductName[] = ['accounts', 'payments', 'cards', 'investments', 'savings', 'insurance', 'lending']

const products: Record<ProductName, Product> = {
  accounts: {
    title: 'Accounts',
    personal: 'everyday account',
    business: 'business account',
    labels: ['core'],
    description: 'Everyday, youth and business accounts on the core banking system.',
    days: 3650,
  },
  payments: {
    title: 'Payments',
    personal: 'payments',
    business: 'payments',
    labels: ['core', 'psd2'],
    description: [
      'Domestic, instant, SEPA and international payments.',
      '',
      '- The **limit** and the **currency** follow the hat: a private person or one of their companies.',
      '- A product use without a document of its own keeps the default limit of a new private customer.',
    ].join('\n'),
    days: 3650,
  },
  cards: {
    title: 'Cards',
    personal: 'debit card',
    business: 'company card',
    labels: ['core', 'pci'],
    description: 'Debit, youth, credit and company cards, issued through Bluefin Card Services.',
    days: 3300,
  },
  investments: {
    title: 'Investments',
    personal: 'portfolio',
    business: 'portfolio',
    labels: ['mifid'],
    description: 'Funds and Nordic equities in the tax-favoured share account of the country. Private hats only.',
    days: 2200,
  },
  savings: {
    title: 'Savings',
    personal: 'savings account',
    business: 'savings account',
    labels: ['deposit'],
    description: 'High-yield and fixed-term savings, and BSU home savings for young people in Norway.',
    days: 1500,
  },
  insurance: {
    title: 'Insurance',
    personal: 'travel insurance',
    business: 'business insurance',
    labels: ['partner'],
    description: 'Travel and contents insurance, sold for the partner **Fjordtrygg**.',
    days: 600,
  },
  lending: {
    title: 'Lending',
    personal: 'loan',
    business: 'credit line',
    labels: ['credit'],
    description: 'Credit lines and business loans for the companies of our customers.',
    days: 20,
  },
}

/** The batches of the products, run by the scheduler of the core banking system. */
const batches = [
  {
    product: 'accounts',
    name: 'monthly-statement',
    title: 'Monthly statement',
    definition: '0 6 1 * *',
    description: 'PDF and camt.053 statement of a business account, for the companies whose accountant wants one.',
    days: 3000,
  },
  {
    product: 'cards',
    name: 'annual-fee',
    title: 'Annual card fee',
    definition: '30 3 * * *',
    description: 'Charges the annual fee of the company cards whose anniversary is today.',
    days: 2000,
  },
  {
    product: 'investments',
    name: 'rebalance',
    title: 'Quarterly rebalancing',
    definition: '0 10 1 1,4,7,10 *',
    description:
      'Brings the managed portfolios back to the weights of their model portfolio. On an exchange holiday, ' +
      '1 January among them, the orders roll to the next trading day.',
    days: 1200,
  },
] as const

interface Company {
  name: string
  /** The short name of the titles; the legal name carries the legal form. */
  title: string
  legalName: string
  legalForm: LegalForm
  organizationNumber: string
  settlementAccount: string
  /** The industry in the business register, a fact rather than configuration. */
  naceCode: string
  products: ProductName[]
  /** The payments limit and the approval rule of the company. */
  limit: number
  approval: string
  /** A company that names its accountant gets the monthly statement, delivered to them. */
  accountantEmail?: string
  /** Company cards with an annual fee. */
  annualFee?: boolean
}

interface Person {
  name: string
  title: string
  country: Country
  segment: Segment
  customerNumber: string
  /** Years as a customer, for the Created dates; 0 for a person still in onboarding. */
  since: number
  /** The risk class of the KYC check; low by default. */
  kycRiskClass?: 'medium' | 'high'
  /** The products used in the private hat. */
  products: ProductName[]
  companies?: Company[]
  /** The language when it is not the country's. */
  language?: string
  /** The private payments limit when it is not the default of the country. */
  limit?: number
  riskProfile?: 'cautious' | 'balanced' | 'growth'
  /** A managed portfolio, rebalanced every quarter. */
  managed?: boolean
  savingsProduct?: 'high-yield' | 'fixed-12m' | 'bsu'
  /** More facts for the Values of the subject. */
  values?: Content
  /** The lifecycle of single entitlements ("savings") and product uses ("cards.self"). */
  lifecycle?: Record<string, Meta>
}

const brightForge: Company = {
  name: 'bright-forge',
  title: 'Bright Forge',
  legalName: 'Bright Forge AS',
  legalForm: 'AS',
  organizationNumber: '921 456 783',
  settlementAccount: '9235.42.17866',
  naceCode: '25.620',
  products: ['accounts', 'payments', 'cards'],
  limit: 250000,
  approval: 'two-eyes-above-50000',
  accountantEmail: 'regnskap@brightforge.example',
}

const harborStudio: Company = {
  name: 'harbor-studio',
  title: 'Harbor Studio',
  legalName: 'Harbor Studio AS',
  legalForm: 'AS',
  organizationNumber: '918 334 210',
  settlementAccount: '9235.58.20930',
  naceCode: '74.201',
  products: ['accounts', 'payments'],
  limit: 150000,
  approval: 'two-eyes-on-new-supplier',
}

/** The round-up pilot of feature-delivery1 opened a savings account for its customers less than two weeks ago. */
const pilot: Meta = { createdDaysAgo: 11, createdBy: 'hedy' }

/** Alex and the documentation's table: the products of the private hat, and each company's in the company. */
const alex: Person = {
  name: 'alex',
  title: 'Alex Haugen',
  country: 'NO',
  segment: 'premium',
  customerNumber: '100042',
  since: 8,
  products: ['accounts', 'payments', 'cards', 'investments'],
  companies: [brightForge, harborStudio],
  lifecycle: { savings: pilot },
}

/** Days since Alex wore each hat: a customer for eight years, director of Bright Forge and later of Harbor Studio. */
const alexHatDays: Record<string, number> = {
  self: alex.since * 365,
  [brightForge.name]: 1900,
  [harborStudio.name]: 700,
}

/** The customers besides Alex: mostly Nordic, each with a private hat, some with one or two companies. */
const customers: Person[] = [
  {
    name: 'ingrid',
    title: 'Ingrid Johansen',
    country: 'NO',
    segment: 'premium',
    customerNumber: '100117',
    since: 6,
    products: ['accounts', 'payments', 'cards', 'investments'],
    limit: 100000,
    riskProfile: 'growth',
    managed: true,
    companies: [
      {
        name: 'fjordlys-design',
        title: 'Fjordlys Design',
        legalName: 'Fjordlys Design AS',
        legalForm: 'AS',
        organizationNumber: '923 118 640',
        settlementAccount: '9235.61.44122',
        naceCode: '74.102',
        products: ['accounts', 'payments', 'cards'],
        limit: 200000,
        approval: 'two-eyes-above-50000',
        accountantEmail: 'regnskap@fjordlys.example',
        annualFee: true,
      },
    ],
  },
  {
    name: 'eirik',
    title: 'Eirik Lunde',
    country: 'NO',
    segment: 'premium',
    customerNumber: '100168',
    since: 9,
    kycRiskClass: 'medium',
    products: ['accounts', 'payments', 'cards'],
    companies: [
      {
        name: 'nordkapp-sjomat',
        title: 'Nordkapp Sjømat',
        legalName: 'Nordkapp Sjømat AS',
        legalForm: 'AS',
        organizationNumber: '915 772 904',
        settlementAccount: '9235.18.30575',
        naceCode: '03.211',
        products: ['accounts', 'payments', 'cards'],
        limit: 1000000,
        approval: 'two-eyes-above-100000',
        accountantEmail: 'okonomi@nordkapp-sjomat.example',
      },
      {
        name: 'lunde-eiendom',
        title: 'Lunde Eiendom',
        legalName: 'Lunde Eiendom AS',
        legalForm: 'AS',
        organizationNumber: '927 640 318',
        settlementAccount: '9235.27.91840',
        naceCode: '68.209',
        products: ['accounts', 'payments'],
        limit: 300000,
        approval: 'single',
        accountantEmail: 'post@lunde-eiendom.example',
      },
    ],
    lifecycle: {
      'payments.nordkapp-sjomat': {
        disabled: true,
        description:
          'Paused during the **KYC review** of Nordkapp Sjømat AS: the register lists a new beneficial owner.',
        modifiedDaysAgo: 12,
        modifiedBy: 'hedy',
      },
    },
  },
  {
    name: 'henrik',
    title: 'Henrik Bakke',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100203',
    since: 4,
    products: ['accounts', 'payments', 'insurance'],
    companies: [
      {
        name: 'bakke-snekkeri',
        title: 'Bakke Snekkeri',
        legalName: 'Bakke Snekkeri Henrik Bakke',
        legalForm: 'ENK',
        organizationNumber: '998 210 457',
        settlementAccount: '9235.40.11877',
        naceCode: '43.320',
        products: ['accounts', 'payments'],
        limit: 75000,
        approval: 'single',
        accountantEmail: 'post@fjellregnskap.example',
      },
    ],
    // The travel insurance runs for a year from the policy date.
    lifecycle: { insurance: { expiresIn: 140 } },
  },
  {
    name: 'magnus',
    title: 'Magnus Aasen',
    country: 'NO',
    segment: 'private-banking',
    customerNumber: '100031',
    since: 14,
    products: ['accounts', 'payments', 'cards', 'investments', 'savings'],
    limit: 500000,
    riskProfile: 'balanced',
    managed: true,
    savingsProduct: 'fixed-12m',
    companies: [
      {
        name: 'aasen-holding',
        title: 'Aasen Holding',
        legalName: 'Aasen Holding AS',
        legalForm: 'AS',
        organizationNumber: '912 905 336',
        settlementAccount: '9235.05.67013',
        naceCode: '64.200',
        products: ['accounts', 'payments'],
        limit: 2000000,
        approval: 'two-eyes-above-250000',
        accountantEmail: 'regnskap@aasen-holding.example',
      },
    ],
  },
  {
    // Closed every account; seeded in Deleted with every hat.
    name: 'anders',
    title: 'Anders Vik',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100412',
    since: 3,
    products: ['accounts', 'payments', 'cards'],
    companies: [
      {
        name: 'vik-media',
        title: 'Vik Media',
        legalName: 'Vik Media AS',
        legalForm: 'AS',
        organizationNumber: '924 551 089',
        settlementAccount: '9235.33.20981',
        naceCode: '59.112',
        products: ['accounts', 'payments'],
        limit: 100000,
        approval: 'single',
        accountantEmail: 'regnskap@vikmedia.example',
      },
    ],
  },
  {
    name: 'jonas',
    title: 'Jonas Strand',
    country: 'NO',
    segment: 'premium',
    customerNumber: '100286',
    since: 7,
    products: ['accounts', 'payments', 'cards', 'investments'],
    riskProfile: 'growth',
    managed: true,
  },
  {
    name: 'lars',
    title: 'Lars Nilsen',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100334',
    since: 11,
    products: ['accounts', 'payments', 'cards'],
  },
  {
    name: 'sigrid',
    title: 'Sigrid Haugland',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100355',
    since: 5,
    products: ['accounts', 'cards'],
    lifecycle: {
      'cards.self': {
        expiresIn: -9,
        description: 'The card expired and Sigrid did not order a new one.',
      },
    },
  },
  {
    name: 'nora',
    title: 'Nora Dahl',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100479',
    since: 2,
    products: ['accounts', 'payments', 'savings'],
    savingsProduct: 'bsu',
    lifecycle: {
      savings: {
        startsIn: 9,
        description: 'BSU home savings; opens with the first monthly deposit.',
        createdDaysAgo: 3,
        createdBy: 'ada',
      },
    },
  },
  {
    name: 'thea',
    title: 'Thea Moen',
    country: 'NO',
    segment: 'youth',
    customerNumber: '100501',
    since: 1,
    products: ['accounts', 'cards'],
  },
  {
    name: 'sam',
    title: 'Sam Okafor',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100522',
    since: 2,
    kycRiskClass: 'medium',
    language: 'en',
    products: ['accounts', 'payments'],
  },
  {
    name: 'hanna',
    title: 'Hanna Rønning',
    country: 'NO',
    segment: 'onboarding',
    customerNumber: '100538',
    since: 0,
    products: [],
    values: { kycStatus: 'pending-documents' },
  },
  {
    name: 'astrid',
    title: 'Astrid Lindqvist',
    country: 'SE',
    segment: 'premium',
    customerNumber: '200044',
    since: 8,
    products: ['accounts', 'payments', 'cards', 'investments'],
    limit: 150000,
    riskProfile: 'balanced',
    managed: true,
    companies: [
      {
        name: 'lindqvist-konsult',
        title: 'Lindqvist Konsult',
        legalName: 'Lindqvist Konsult AB',
        legalForm: 'AB',
        organizationNumber: '559214-0873',
        settlementAccount: 'SE26 9230 0000 0123 4567 8901',
        naceCode: '70.220',
        products: ['accounts', 'payments', 'cards'],
        limit: 250000,
        approval: 'two-eyes-above-50000',
        accountantEmail: 'ekonomi@lindqvistkonsult.example',
      },
    ],
  },
  {
    name: 'viktor',
    title: 'Viktor Engström',
    country: 'SE',
    segment: 'premium',
    customerNumber: '200061',
    since: 5,
    kycRiskClass: 'medium',
    products: ['accounts', 'payments', 'investments'],
    riskProfile: 'growth',
    managed: true,
    companies: [
      {
        name: 'polar-pixel',
        title: 'Polar Pixel',
        legalName: 'Polar Pixel AB',
        legalForm: 'AB',
        organizationNumber: '559087-3361',
        settlementAccount: 'SE54 9230 0000 0987 6543 2109',
        naceCode: '62.010',
        products: ['accounts', 'payments', 'cards'],
        limit: 400000,
        approval: 'two-eyes-above-100000',
        annualFee: true,
      },
    ],
  },
  {
    name: 'linnea',
    title: 'Linnéa Ek',
    country: 'SE',
    segment: 'premium',
    customerNumber: '200092',
    since: 6,
    products: ['accounts', 'payments', 'cards', 'insurance'],
  },
  {
    name: 'erik',
    title: 'Erik Sandberg',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '200118',
    since: 10,
    products: ['accounts', 'payments', 'cards'],
  },
  {
    name: 'ebba',
    title: 'Ebba Holm',
    country: 'SE',
    segment: 'youth',
    customerNumber: '200135',
    since: 1,
    products: ['accounts', 'cards'],
  },
  {
    name: 'oskar',
    title: 'Oskar Berglund',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '200147',
    since: 3,
    products: ['accounts', 'payments'],
  },
  {
    name: 'maja',
    title: 'Maja Nyström',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '200163',
    since: 4,
    products: ['accounts', 'cards', 'savings'],
  },
  {
    name: 'mette',
    title: 'Mette Kjær',
    country: 'DK',
    segment: 'premium',
    customerNumber: '300027',
    since: 7,
    products: ['accounts', 'payments', 'cards'],
    companies: [
      {
        name: 'kjaer-arkitekter',
        title: 'Kjær Arkitekter',
        legalName: 'Kjær Arkitekter ApS',
        legalForm: 'ApS',
        organizationNumber: '38472196',
        settlementAccount: 'DK86 9230 0004 4011 62',
        naceCode: '71.111',
        products: ['accounts', 'payments', 'cards'],
        limit: 300000,
        approval: 'two-eyes-above-50000',
        accountantEmail: 'bogholderi@kjaer-arkitekter.example',
      },
    ],
  },
  {
    name: 'mikkel',
    title: 'Mikkel Lauridsen',
    country: 'DK',
    segment: 'everyday',
    customerNumber: '300058',
    since: 3,
    kycRiskClass: 'medium',
    products: ['accounts', 'payments'],
    companies: [
      {
        name: 'havtorn-bryghus',
        title: 'Havtorn Bryghus',
        legalName: 'Havtorn Bryghus ApS',
        legalForm: 'ApS',
        organizationNumber: '41205578',
        settlementAccount: 'DK31 9230 0007 1180 55',
        naceCode: '11.050',
        products: ['accounts', 'payments'],
        limit: 150000,
        approval: 'single',
      },
    ],
  },
  {
    name: 'ida',
    title: 'Ida Mortensen',
    country: 'DK',
    segment: 'premium',
    customerNumber: '300074',
    since: 9,
    products: ['accounts', 'payments', 'cards', 'investments'],
    riskProfile: 'cautious',
  },
  {
    name: 'sofie',
    title: 'Sofie Thomsen',
    country: 'DK',
    segment: 'everyday',
    customerNumber: '300091',
    since: 2,
    products: ['accounts', 'savings'],
  },
  {
    name: 'oliver',
    title: 'Oliver Brandt',
    country: 'DK',
    segment: 'onboarding',
    customerNumber: '300112',
    since: 0,
    kycRiskClass: 'medium',
    language: 'en',
    products: [],
    values: { kycStatus: 'awaiting-mitid' },
  },
  {
    name: 'aino',
    title: 'Aino Virtanen',
    country: 'FI',
    segment: 'premium',
    customerNumber: '400019',
    since: 5,
    products: ['accounts', 'cards'],
    companies: [
      {
        name: 'revontuli-games',
        title: 'Revontuli Games',
        legalName: 'Revontuli Games Oy',
        legalForm: 'Oy',
        organizationNumber: '3187452-6',
        settlementAccount: 'FI36 9230 5600 0007 85',
        naceCode: '58.210',
        products: ['accounts', 'payments', 'cards'],
        limit: 50000,
        approval: 'two-eyes-above-10000',
        accountantEmail: 'kirjanpito@revontuli.example',
      },
    ],
  },
  {
    name: 'helmi',
    title: 'Helmi Mäkinen',
    country: 'FI',
    segment: 'youth',
    customerNumber: '400046',
    since: 1,
    products: ['accounts', 'cards'],
  },
  {
    name: 'venla',
    title: 'Venla Heikkinen',
    country: 'FI',
    segment: 'everyday',
    customerNumber: '400058',
    since: 4,
    products: ['accounts', 'payments', 'insurance'],
    lifecycle: { insurance: { expiresIn: 75 } },
  },
  {
    name: 'juho',
    title: 'Juho Nieminen',
    country: 'FI',
    segment: 'everyday',
    customerNumber: '400073',
    since: 6,
    products: ['accounts', 'payments'],
  },
  // The everyday customers who keep an account, and maybe their bills, with us and nothing more.
  {
    name: 'kristian',
    title: 'Kristian Solberg',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100547',
    since: 12,
    products: ['accounts', 'payments'],
  },
  {
    name: 'marte',
    title: 'Marte Berg',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100563',
    since: 3,
    products: ['accounts'],
  },
  {
    name: 'tuva',
    title: 'Tuva Lie',
    country: 'NO',
    segment: 'youth',
    customerNumber: '100588',
    since: 1,
    products: ['accounts', 'cards'],
  },
  {
    name: 'priya',
    title: 'Priya Raman',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '100602',
    since: 1,
    language: 'en',
    products: ['accounts', 'payments'],
  },
  {
    name: 'elin',
    title: 'Elin Lundgren',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '200179',
    since: 7,
    products: ['accounts', 'payments'],
  },
  {
    name: 'gustav',
    title: 'Gustav Wikström',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '200194',
    since: 2,
    products: ['accounts'],
  },
  {
    name: 'saga',
    title: 'Saga Persson',
    country: 'SE',
    segment: 'onboarding',
    customerNumber: '200211',
    since: 0,
    products: [],
    values: { kycStatus: 'awaiting-bankid' },
  },
  {
    name: 'freja',
    title: 'Freja Nielsen',
    country: 'DK',
    segment: 'everyday',
    customerNumber: '300126',
    since: 5,
    products: ['accounts', 'payments'],
  },
  {
    name: 'rasmus',
    title: 'Rasmus Poulsen',
    country: 'DK',
    segment: 'everyday',
    customerNumber: '300139',
    since: 8,
    products: ['accounts', 'cards'],
  },
  {
    name: 'karoline',
    title: 'Karoline Juhl',
    country: 'DK',
    segment: 'everyday',
    customerNumber: '300151',
    since: 1,
    products: ['accounts'],
  },
  {
    name: 'eero',
    title: 'Eero Laine',
    country: 'FI',
    segment: 'everyday',
    customerNumber: '400088',
    since: 9,
    products: ['accounts', 'payments'],
  },
  {
    name: 'aada',
    title: 'Aada Korhonen',
    country: 'FI',
    segment: 'everyday',
    customerNumber: '400097',
    since: 2,
    products: ['accounts'],
  },
  {
    name: 'ville',
    title: 'Ville Lehtonen',
    country: 'FI',
    segment: 'onboarding',
    customerNumber: '400104',
    since: 0,
    products: [],
    values: { kycStatus: 'pending-documents' },
  },
]

/** The synthetic people of the test environments: the "John Does" of the four countries, and their test companies. */
const testCustomers: Person[] = [
  {
    name: 'ola-nordmann',
    title: 'Ola Nordmann',
    country: 'NO',
    segment: 'premium',
    customerNumber: '900001',
    since: 2,
    products: ['accounts', 'payments', 'cards', 'investments'],
    riskProfile: 'growth',
    managed: true,
    companies: [
      {
        name: 'qa-testselskap',
        title: 'QA Testselskap',
        legalName: 'QA Testselskap AS',
        legalForm: 'AS',
        organizationNumber: '999 100 001',
        settlementAccount: '9235.99.00009',
        naceCode: '62.010',
        products: ['accounts', 'payments', 'cards'],
        limit: 10000,
        approval: 'two-eyes-above-1000',
        accountantEmail: 'regnskap@qa-testselskap.example',
      },
    ],
  },
  {
    name: 'sven-svensson',
    title: 'Sven Svensson',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '900002',
    since: 2,
    kycRiskClass: 'medium',
    products: ['accounts', 'payments', 'cards'],
    companies: [
      {
        name: 'qa-testbolaget',
        title: 'QA Testbolaget',
        legalName: 'QA Testbolaget AB',
        legalForm: 'AB',
        organizationNumber: '559999-0002',
        settlementAccount: 'SE69 9230 0000 0999 0000 0002',
        naceCode: '62.010',
        products: ['accounts', 'payments'],
        limit: 10000,
        approval: 'single',
      },
    ],
  },
  {
    name: 'kari-nordmann',
    title: 'Kari Nordmann',
    country: 'NO',
    segment: 'everyday',
    customerNumber: '900003',
    since: 2,
    kycRiskClass: 'high',
    products: ['accounts', 'payments', 'savings'],
  },
  {
    name: 'anna-andersson',
    title: 'Anna Andersson',
    country: 'SE',
    segment: 'everyday',
    customerNumber: '900004',
    since: 1,
    products: ['accounts', 'cards', 'insurance'],
  },
  {
    name: 'jens-hansen',
    title: 'Jens Hansen',
    country: 'DK',
    segment: 'everyday',
    customerNumber: '900005',
    since: 1,
    products: ['accounts', 'payments'],
    companies: [
      {
        name: 'qa-testfirma',
        title: 'QA Testfirma',
        legalName: 'QA Testfirma ApS',
        legalForm: 'ApS',
        organizationNumber: '99990005',
        settlementAccount: 'DK54 9230 0009 9900 05',
        naceCode: '62.010',
        products: ['accounts', 'payments', 'cards'],
        limit: 10000,
        approval: 'two-eyes-above-1000',
        accountantEmail: 'bogholderi@qa-testfirma.example',
        annualFee: true,
      },
    ],
  },
  {
    name: 'matti-meikalainen',
    title: 'Matti Meikäläinen',
    country: 'FI',
    segment: 'premium',
    customerNumber: '900006',
    since: 1,
    products: ['accounts', 'payments', 'cards', 'investments'],
    riskProfile: 'cautious',
    managed: true,
  },
  {
    name: 'maija-meikalainen',
    title: 'Maija Meikäläinen',
    country: 'FI',
    segment: 'youth',
    customerNumber: '900007',
    since: 1,
    products: ['accounts', 'cards', 'savings'],
  },
]

/** The companies the next release gives a credit line, with its amount. */
const creditLines: Record<string, number> = {
  'bright-forge': 500000,
  'fjordlys-design': 300000,
  'nordkapp-sjomat': 2000000,
  'revontuli-games': 150000,
}

interface Environment {
  name: 'production' | 'release-candidate' | 'staging' | 'development' | 'feature-delivery1'
  /** The domain of the product endpoints. */
  domain: string
  logLevel: 'debug' | 'info' | 'warn'
}

const production: Environment = { name: 'production', domain: 'bluefin.example', logLevel: 'warn' }
const releaseCandidate: Environment = { name: 'release-candidate', domain: 'rc.bluefin.example', logLevel: 'info' }
const staging: Environment = { name: 'staging', domain: 'staging.bluefin.example', logLevel: 'info' }
const development: Environment = { name: 'development', domain: 'sandbox.bluefin.example', logLevel: 'debug' }
const featureDelivery: Environment = { name: 'feature-delivery1', domain: 'fd1.bluefin.example', logLevel: 'info' }

/** Production and the release candidate keep the histories; the test environments start from the latest version. */
const versionsIn = (env: Environment, versions: Content[]) =>
  env.name === 'production' || env.name === 'release-candidate' ? versions : versions.slice(-1)

const legalForms = ['person', 'AS', 'ENK', 'AB', 'ApS', 'Oy']
const currencies = ['NOK', 'SEK', 'DKK', 'EUR']

/** The monthly statement of a business account goes to the accountant the company names in its Values. */
const statementRun: Content = { formats: ['pdf', 'camt.053'], deliverTo: '@annotation("/accountantEmail", "Context")' }

const firstName = (person: Person) => person.title.split(' ')[0]!
const ascii = (text: string) =>
  text
    .toLowerCase()
    .replaceAll('æ', 'ae')
    .replaceAll('ø', 'o')
    .normalize('NFD')
    .replaceAll(/\p{M}/gu, '')
    .replaceAll(' ', '.')
/** The name of a private product use: a youth card and the metal card of private banking are not debit cards. */
const personalTitle = (person: Person, product: ProductName) =>
  product === 'cards' && person.segment === 'youth'
    ? 'youth card'
    : product === 'cards' && person.segment === 'private-banking'
      ? 'credit card'
      : products[product].personal
const emailOf = (person: Person) => `${ascii(person.title)}@mail.example`
/** A mobile number in the format of the country, seeded by the person, so it is the same in every view. */
function phoneOf(person: Person) {
  const { code, ranges, groups } = countries[person.country].mobile
  const rng = seededRng(`money-suite phone ${person.name}`)
  let digits = rng.pick(ranges)
  while (digits.length < groups.reduce((sum, group) => sum + group, 0)) {
    digits += rng.int(0, 9)
  }
  const parts = [code]
  let at = 0
  for (const group of groups) {
    parts.push(digits.slice(at, at + group))
    at += group
  }
  return parts.join(' ')
}

/** Only the defined fields, so a spread does not reset a default with undefined. */
const defined = (meta: Meta): Meta =>
  Object.fromEntries(Object.entries(meta).filter(([, value]) => value !== undefined)) as Meta

/** What a product use takes over from its entitlement, and a batch run from its product use. */
const inherited = (meta: Meta | undefined): Meta =>
  meta
    ? defined({
        startsIn: meta.startsIn,
        expiresIn: meta.expiresIn,
        disabled: meta.disabled,
        createdDaysAgo: meta.createdDaysAgo,
        createdBy: meta.createdBy,
      })
    : {}

/** A single version written some days ago by one of the editors. */
const recently = (b: ScopeBuilder): HistoryOptions => ({
  lastDaysAgo: b.rng.int(2, 25),
  authors: [b.rng.pick(b.editors)],
})

function addProducts(b: ScopeBuilder, names: readonly ProductName[], withBatches: boolean, env: Environment) {
  for (const name of names) {
    const product = products[name]
    b.responsibility(name, {
      title: product.title,
      description: product.description,
      labels: product.labels,
      link: `${docs}/products/${name}`,
      createdDaysAgo: product.days,
    })
  }
  if (!withBatches) {
    return
  }
  for (const batch of batches) {
    if (b.has(`rst.${batch.product}`)) {
      b.unit(batch.product, batch.name, {
        unitType: 'time',
        // Development runs the statements every fifteen minutes, so a developer sees one without waiting a month.
        definition:
          env.name === 'development' && batch.name === 'monthly-statement' ? '*/15 * * * *' : batch.definition,
        title: batch.title,
        description: batch.description,
        createdDaysAgo: batch.days,
      })
    }
  }
}

function productDocuments(b: ScopeBuilder, env: Environment) {
  const url = (service: string) => `https://${service}.${env.domain}`
  if (b.has('rst.accounts')) {
    // The move to the core banking API v3 brought the camt.053 statement next to the PDF.
    const accounts = { accountTypes: ['everyday', 'youth', 'business'], logLevel: env.logLevel }
    b.configure(
      'rst.accounts',
      versionsIn(env, [
        { coreBanking: `${url('core')}/accounts/v2`, ...accounts, statementFormats: ['pdf'] },
        { coreBanking: `${url('core')}/accounts/v3`, ...accounts, statementFormats: ['pdf', 'camt.053'] },
      ]),
      { every: 70, lastDaysAgo: 18 },
    )
  }
  if (b.has('rst.payments')) {
    // The rails, and the limit and currency of a new private customer, which a private product use keeps unless it
    // stores its own (b2c-finance.md). Tuned every week for four months: the long history of the project. In a week
    // without a named change the fraud team lowers the alert threshold a notch while it watches the false positives.
    const changes: Record<number, Content> = {
      3: { rails: ['domestic', 'instant', 'sepa'] },
      6: { limit: 50000 },
      9: { rails: ['domestic', 'instant', 'sepa', 'international'] },
      12: { instantLimit: 25000 },
      14: { rails: ['domestic', 'instant', 'sepa', 'international', 'request-to-pay'] },
      16: { cutOff: '16:00' },
    }
    const versions = evolve(
      {
        rails: ['domestic', 'instant'],
        limit: 25000,
        currency: 'NOK',
        instantLimit: 10000,
        cutOff: '15:30',
        endpoint: url('payments'),
        fraudScreening: { scoreThreshold: 900 },
        logLevel: env.logLevel,
        ...(env.name === 'development' ? { fakeClearing: true } : {}),
      },
      17,
      (version) => changes[version] ?? { fraudScreening: { scoreThreshold: 900 - version * 5 } },
    )
    b.configure('rst.payments', versionsIn(env, versions), { every: 7, lastDaysAgo: 2 })
  }
  if (b.has('rst.cards')) {
    // Personal cards without a document of their own are the product default: a Visa debit card.
    const cards = evolve(
      {
        issuer: 'Bluefin Card Services',
        cardType: 'debit',
        network: 'visa',
        networks: ['visa'],
        threeDSecure: true,
        processor: { endpoint: url('issuing'), apiKey: '@(cards.processor-api-key, primaryVault)' },
      },
      env.name === 'release-candidate' ? 4 : 3,
      (version) =>
        version === 2
          ? { networks: ['visa', 'mastercard'] }
          : version === 3
            ? { wallets: ['apple-pay', 'google-pay'] }
            : { virtualCards: true, companyCardsPerCompany: 25 },
    )
    // The release candidate adds a version three days ago and keeps production's latest one, 11 days ago.
    b.configure(
      'rst.cards',
      versionsIn(env, cards),
      env.name === 'release-candidate' ? { every: 8, lastDaysAgo: 3 } : { every: 30, lastDaysAgo: 11 },
    )
  }
  if (b.has('rst.investments')) {
    b.configure('rst.investments', {
      broker: { endpoint: url('broker'), apiKey: '@(investments.broker-api-key, primaryVault)' },
      markets: ['XOSL', 'XSTO', 'XCSE', 'XHEL'],
      fundCatalog: 'nordic-ucits',
    })
  }
  if (b.has('rst.savings')) {
    // The rate follows the central bank; the round-up pilot adds its cap.
    const roundUps = b.has('unt.savings.round-up-sweep') ? { roundUps: { roundTo: 10, maxPerMonth: 2000 } } : {}
    b.configure(
      'rst.savings',
      versionsIn(
        env,
        [3.25, 3.5, 3.75, 3.6].map((interestRate) => ({
          interestRate,
          compounding: 'monthly',
          freeWithdrawalsPerYear: 4,
          ...roundUps,
        })),
      ),
      { every: 45, lastDaysAgo: 8 },
    )
  }
  if (b.has('rst.insurance')) {
    b.configure(
      'rst.insurance',
      versionsIn(env, [
        { partner: 'Fjordtrygg', endpoint: 'https://api.fjordtrygg.example/v2', covers: ['travel'] },
        {
          partner: 'Fjordtrygg',
          endpoint: 'https://api.fjordtrygg.example/v2',
          covers: ['travel', 'contents'],
          token: '@(insurance.fjordtrygg-token, primaryVault)',
        },
      ]),
      { every: 60, lastDaysAgo: 21 },
    )
  }
  if (b.has('rst.lending')) {
    b.configure('rst.lending', {
      products: ['credit-line', 'business-loan'],
      maxAmount: 2000000,
      currency: 'NOK',
      scoring: { endpoint: url('scoring'), model: 'sme-2026-10' },
    })
  }

  if (b.has('unt.accounts.monthly-statement')) {
    b.configure('unt.accounts.monthly-statement', { formats: ['pdf'], archiveYears: 5 })
  }
  if (b.has('unt.cards.annual-fee')) {
    // The fee of a company card in each currency; a run charges NOK unless its company settles in another.
    b.configure('unt.cards.annual-fee', [
      { amounts: { NOK: 395, SEK: 395, DKK: 279, EUR: 35 }, currency: 'NOK' },
      { amounts: { NOK: 495, SEK: 495, DKK: 349, EUR: 45 }, currency: 'NOK', retryFailedChargeDays: 3 },
    ])
  }
  if (b.has('unt.investments.rebalance')) {
    b.configure('unt.investments.rebalance', {
      driftThresholdPercent: 5,
      minimumTrade: { NOK: 500, SEK: 500, DKK: 350, EUR: 50 },
      cutOff: '14:00',
      holidayCalendars: ['XOSL', 'XSTO', 'XCSE', 'XHEL'],
      rollTo: 'next-business-day',
    })
  }
}

function schemas(b: ScopeBuilder, env: Environment) {
  // Every hat of everyone names the jurisdiction whose rules apply: KYC, tax and the products on offer.
  b.typeSchema(
    'cnt',
    {
      type: 'object',
      properties: { jurisdiction: { type: 'string', pattern: '^[A-Z]{2}$' }, legalForm: { enum: legalForms } },
      required: ['jurisdiction'],
    },
    { lastDaysAgo: 16 },
  )
  if (b.has('rst.payments')) {
    // A product use of payments cannot be saved without its limit and currency (b2c-finance.md).
    b.descendantSchema(
      'rst.payments',
      'exe',
      versionsIn(env, [
        { type: 'object', properties: { limit: { type: 'integer', minimum: 0 } }, required: ['limit'] },
        {
          type: 'object',
          properties: {
            limit: { type: 'integer', minimum: 0 },
            currency: { enum: currencies },
            vatInvoice: { type: 'boolean' },
            approval: { type: 'string' },
          },
          required: ['limit', 'currency'],
        },
      ]),
      { every: 120, lastDaysAgo: 20 },
    )
  }
  if (b.has('rst.cards')) {
    const card = {
      type: 'object',
      properties: {
        cardType: { enum: ['debit', 'youth', 'credit', 'company'] },
        network: { enum: ['visa', 'mastercard'] },
        monthlyLimit: { type: 'integer', minimum: 0 },
      },
      required: ['cardType'],
    }
    // The next release lets a company hold up to 25 cards, so every card needs its own monthly limit.
    b.descendantSchema(
      'rst.cards',
      'exe',
      versionsIn(
        env,
        env.name === 'release-candidate' ? [card, { ...card, required: ['cardType', 'monthlyLimit'] }] : [card],
      ),
      // Production's version keeps its date, 24 days ago, in the release candidate too.
      env.name === 'release-candidate' ? { every: 21, lastDaysAgo: 3 } : { lastDaysAgo: 24 },
    )
  }
  if (b.has('rst.investments')) {
    // MiFID II: no investment entitlement without a suitability assessment.
    b.descendantSchema('rst.investments', 'usg', {
      type: 'object',
      properties: { riskProfile: { enum: ['cautious', 'balanced', 'growth'] } },
      required: ['riskProfile'],
    })
  }
  if (b.has('rst.lending')) {
    b.descendantSchema('rst.lending', 'exe', {
      type: 'object',
      properties: {
        creditLimit: { type: 'integer', minimum: 10000, maximum: 2000000 },
        currency: { enum: currencies },
        interestMargin: { type: 'number', minimum: 0, maximum: 15 },
      },
      required: ['creditLimit', 'currency'],
    })
  }
}

function templates(b: ScopeBuilder) {
  // Every product use screens its transactions at the KYC risk class of its person and notifies the person; the app's
  // push notifications came later, and they keep quiet at night.
  const screening = '@annotation("/kycRiskClass", "Subject")'
  b.template(
    'exe',
    [
      { screening, notifications: { channels: ['email'] } },
      { screening, notifications: { channels: ['push', 'email'], quietHours: '22:00-07:00' } },
    ],
    { every: 40, lastDaysAgo: 9 },
  )
  if (b.keys('unt').length) {
    // A failed batch run is retried, then reported to the batch operations team.
    b.template('uxe', { retries: 3, retryDelayMinutes: 20, notifyOnFailure: 'batch-ops@bluefin.example' })
  }
}

/** The facts of a person that are not configuration: the segment, the country, the CRM id and the KYC risk class. */
const personMeta = (person: Person, values?: Content): Meta => ({
  title: person.title,
  labels: [person.segment, countries[person.country].label],
  values: {
    crmId: `CRM-${person.customerNumber}`,
    kycRiskClass: person.kycRiskClass ?? 'low',
    ...person.values,
    ...values,
  },
  timeZone: countries[person.country].timeZone,
})

/** The identity on the subject: the customer number, the language and the contact. */
const identityOf = (person: Person, contact = { email: emailOf(person), phone: phoneOf(person) }): Content => ({
  customerNumber: person.customerNumber,
  language: person.language ?? countries[person.country].language,
  contact,
})

/** Alex's subject: the identity every product use inherits, and the rule for the hats (b2c-finance.md). */
function alexSubject(b: ScopeBuilder) {
  b.subject(alex.name, {
    ...personMeta(alex),
    description:
      'Logs in as one person and acts as themselves and as the director of **Bright Forge** and **Harbor Studio**. ' +
      'Limits, KYC, tax and the available products follow the hat.',
    link: `${docs}/hats`,
    createdDaysAgo: alexHatDays.self,
  })
  // The language and the customer number are written once, on the subject, and every product use inherits them.
  const phone = '+47 912 34 567'
  b.configure(
    'sbt.alex',
    [
      identityOf(alex, { email: 'alex.haugen@mail.example', phone }),
      identityOf(alex, { email: 'alex@haugen.example', phone }),
    ],
    { every: 90, lastDaysAgo: 15 },
  )
  // Every hat of Alex needs a jurisdiction; the private one records the legal form person.
  b.descendantSchema(
    'sbt.alex',
    'cnt',
    {
      type: 'object',
      properties: { jurisdiction: { enum: ['NO', 'SE', 'DK', 'FI'] }, legalForm: { enum: legalForms } },
      required: ['jurisdiction'],
    },
    { lastDaysAgo: 25, authors: ['frances'] },
  )
}

function alexHats(b: ScopeBuilder, hats: readonly string[]) {
  if (hats.includes('self')) {
    addPrivateHat(b, alex, alexHatDays.self!)
  }
  for (const company of alex.companies ?? []) {
    if (hats.includes(company.name)) {
      addCompanyHat(b, alex, company, alexHatDays[company.name]!)
    }
  }
}

interface AlexUse {
  title: string
  description?: string
  versions?: Content[]
  options?: HistoryOptions
}

/** Alex's product uses: the table of the documentation, where an absent cell has no execution, and later changes. */
const alexTable: Record<string, AlexUse> = {
  'accounts.self': { title: 'Everyday account' },
  'accounts.bright-forge': {
    title: 'Bright Forge business account',
    versions: [{ accountType: 'business', camt053: true }],
  },
  'accounts.harbor-studio': {
    title: 'Harbor Studio business account',
    versions: [{ accountType: 'business', camt053: false }],
  },
  'payments.self': {
    title: 'Consumer payments',
    description: 'Consumer rails, low limit: keeps the product default limit and leaves `vatInvoice` unset.',
  },
  'payments.bright-forge': {
    title: 'Bright Forge payments',
    description: 'VAT invoice, higher limit. This document is the whole of Bright Forge’s difference.',
    // The version history says who set the two-eyes rule.
    versions: [
      { limit: 100000, currency: 'NOK', vatInvoice: true },
      { limit: 250000, currency: 'NOK', vatInvoice: true },
      { limit: 250000, currency: 'NOK', vatInvoice: true, approval: 'two-eyes-above-50000' },
    ],
    options: { every: 21, lastDaysAgo: 4, authors: ['ada', 'frances', 'hedy'] },
  },
  'payments.harbor-studio': {
    title: 'Harbor Studio supplier payments',
    description: 'Supplier payments, with an approval rule of their own.',
    versions: [
      { limit: 150000, currency: 'NOK', profile: 'supplier-payments', batchFormat: 'pain.001' },
      {
        limit: 150000,
        currency: 'NOK',
        profile: 'supplier-payments',
        batchFormat: 'pain.001',
        approval: 'two-eyes-on-new-supplier',
      },
    ],
    options: { every: 14, lastDaysAgo: 6 },
  },
  'cards.self': { title: 'Debit card', versions: [{ cardType: 'debit', network: 'visa', contactless: true }] },
  'cards.bright-forge': {
    title: 'Bright Forge company card',
    versions: [{ cardType: 'company', network: 'mastercard', monthlyLimit: 50000, embossing: '@config("/legalName")' }],
  },
  'investments.self': { title: 'Personal portfolio', versions: [{ wrapper: 'ask', managed: false }] },
  // The next release: Harbor Studio gains cards, and Bright Forge a credit line.
  'cards.harbor-studio': {
    title: 'Harbor Studio company card',
    description: 'New in the release: the person, the company and the card product were already in the catalog.',
    versions: [{ cardType: 'company', network: 'visa', monthlyLimit: 30000, embossing: '@config("/legalName")' }],
  },
  'lending.bright-forge': {
    title: 'Bright Forge credit line',
    versions: [
      { product: 'credit-line', creditLimit: creditLines['bright-forge'], currency: 'NOK', interestMargin: 2.9 },
    ],
  },
  // The round-up pilot of feature-delivery1.
  'savings.self': { title: 'Savings account', versions: [{ monthlyDeposit: 2500, currency: 'NOK' }] },
}

const alexEntitlements: Partial<Record<ProductName, Content>> = {
  // The fact that Alex may use payments at all, and the services inside payments Alex may turn on.
  payments: { services: ['domestic', 'instant', 'sepa', 'international', 'vat-invoice', 'supplier-batch'] },
  // The card entitlement is personal, so it exists whichever hat holds the card.
  cards: { entitlement: 'personal', cardTypes: ['debit', 'company'] },
  investments: { riskProfile: 'growth', services: ['funds', 'equities'] },
  lending: { creditCheck: 'approved', products: ['credit-line'] },
  savings: { product: 'high-yield' },
}

/** Alex's product uses in production: the documentation's table, one cell for each product of each hat. */
const alexDefaultUses = [
  ...alex.products.map((product) => `${product}.self`),
  ...(alex.companies ?? []).flatMap((company) => company.products.map((product) => `${product}.${company.name}`)),
]

function alexUses(b: ScopeBuilder, uses: readonly string[]) {
  const used = productNames.filter((product) => uses.some((use) => use.startsWith(`${product}.`)))
  for (const product of used) {
    b.usage(alex.name, product, {
      title: `${products[product].title} for ${firstName(alex)}`,
      createdDaysAgo: Math.min(alexHatDays.self!, products[product].days),
      ...alex.lifecycle?.[product],
    })
    const content = alexEntitlements[product]
    if (content) {
      b.configure(`usg.alex.${product}`, content)
    }
  }
  for (const use of uses) {
    const [product, context] = use.split('.') as [ProductName, string]
    const { title, description, versions, options } = alexTable[use]!
    const key = b.execution(alex.name, product, context, {
      title,
      description,
      labels: context === 'self' ? undefined : ['business'],
      createdDaysAgo: Math.min(products[product].days, alexHatDays[context]!),
      ...inherited(alex.lifecycle?.[product]),
      ...alex.lifecycle?.[use],
    })
    if (versions) {
      b.configure(key, versions, options)
    }
  }
  // The board's one batch run: Bright Forge's accountant gets the monthly statement.
  if (b.has('exe.alex.accounts.bright-forge') && b.has('unt.accounts.monthly-statement')) {
    const key = b.unitOfExecution(alex.name, 'accounts', 'bright-forge', 'monthly-statement', {
      title: 'Bright Forge monthly statement',
      createdDaysAgo: Math.min(alexHatDays['bright-forge']!, 365),
    })
    b.configure(key, statementRun)
  }
}

/** The private hat: everyone acts as themselves, under the rules of their home country. */
function addPrivateHat(b: ScopeBuilder, person: Person, days: number) {
  b.context(person.name, 'self', {
    title: `${person.title}, private`,
    labels: ['personal'],
    timeZone: countries[person.country].timeZone,
    createdDaysAgo: days,
  })
  b.configure(`cnt.${person.name}.self`, { jurisdiction: person.country, legalForm: 'person' }, recently(b))
}

function addCompanyHat(b: ScopeBuilder, person: Person, company: Company, days: number) {
  b.context(person.name, company.name, {
    title: company.legalName,
    labels: ['business', ...(company.legalForm === 'ENK' ? ['sole-proprietor'] : [])],
    values: {
      naceCode: company.naceCode,
      ...(company.accountantEmail ? { accountantEmail: company.accountantEmail } : {}),
    },
    timeZone: countries[person.country].timeZone,
    createdDaysAgo: days,
  })
  // The company: legal name, organization number, home jurisdiction and settlement account (b2c-finance.md).
  b.configure(
    `cnt.${person.name}.${company.name}`,
    {
      legalName: company.legalName,
      legalForm: company.legalForm,
      organizationNumber: company.organizationNumber,
      jurisdiction: person.country,
      settlementAccount: company.settlementAccount,
    },
    recently(b),
  )
}

interface Hat {
  context: string
  company?: Company
  products: ProductName[]
  days: number
}

function entitlementContent(b: ScopeBuilder, person: Person, product: ProductName, hats: Hat[]): Content | undefined {
  switch (product) {
    case 'payments':
      // A director may turn on the business services, the VAT invoice of their company's payments among them.
      return hats.some((hat) => hat.company)
        ? { services: ['domestic', 'instant', 'sepa', 'international', 'vat-invoice', 'supplier-batch'] }
        : undefined
    case 'cards': {
      const types = hats
        .filter((hat) => hat.products.includes('cards'))
        .map((hat) => (hat.company ? 'company' : person.segment === 'youth' ? 'youth' : 'debit'))
      return types.includes('company') ? { entitlement: 'personal', cardTypes: [...new Set(types)] } : undefined
    }
    case 'investments':
      return { riskProfile: person.riskProfile ?? b.rng.pick(['cautious', 'balanced', 'growth']), services: ['funds'] }
    case 'savings':
      return { product: person.savingsProduct ?? 'high-yield' }
    case 'insurance':
      return { cover: 'travel', partner: 'Fjordtrygg' }
    case 'lending':
      return { creditCheck: 'approved', products: ['credit-line'] }
    default:
      return undefined
  }
}

/** The document of a product use: only what differs from the product, the person and the hat. */
function useContent(b: ScopeBuilder, person: Person, product: ProductName, hat: Hat): Content | undefined {
  const { currency, limit, wrapper, youthLimit } = countries[person.country]
  const company = hat.company
  switch (product) {
    case 'accounts':
      if (company) {
        return { accountType: 'business', camt053: Boolean(company.accountantEmail) }
      }
      return person.segment === 'youth' ? { accountType: 'youth', overdraft: 0, guardianConsent: true } : undefined
    case 'payments':
      if (company) {
        return { limit: company.limit, currency, vatInvoice: true, approval: company.approval }
      }
      // A private hat in Norway keeps the product default; elsewhere it stores the limit in its own currency.
      return person.country === 'NO' && !person.limit ? undefined : { limit: person.limit ?? limit, currency }
    case 'cards':
      if (company) {
        return {
          cardType: 'company',
          network: 'mastercard',
          monthlyLimit: Math.round(company.limit / 5),
          embossing: '@config("/legalName")',
        }
      }
      if (person.segment === 'youth') {
        // No gambling (MCC 7995) and no liquor stores (MCC 5921) on a youth card.
        return { cardType: 'youth', monthlyLimit: youthLimit, blockedMerchantCategories: ['7995', '5921'] }
      }
      return person.segment === 'private-banking'
        ? { cardType: 'credit', network: 'mastercard', metal: true, monthlyLimit: 200000 }
        : undefined
    case 'investments':
      return { wrapper, managed: Boolean(person.managed) }
    case 'savings':
      // BSU takes at most 27,500 NOK a year, so the standing order stays below 2,292 a month.
      return person.savingsProduct === 'bsu'
        ? { monthlyDeposit: 2250, currency, annualCap: 27500 }
        : { monthlyDeposit: b.rng.pick([1000, 1500, 2500, 5000]), currency }
    case 'insurance':
      return {
        area: 'worldwide',
        familyCover: b.rng.chance(0.5),
        deductible: currency === 'EUR' ? 150 : 1500,
        currency,
      }
    case 'lending':
      return { product: 'credit-line', creditLimit: creditLines[hat.context] ?? 100000, currency, interestMargin: 3.4 }
  }
}

function batchRuns(b: ScopeBuilder, person: Person, product: ProductName, hat: Hat, lifecycle: Meta) {
  const run = (unit: string, title: string, content?: Content) => {
    if (!b.has(`unt.${product}.${unit}`)) {
      return
    }
    const key = b.unitOfExecution(person.name, product, hat.context, unit, {
      title,
      timeZone: countries[person.country].timeZone,
      createdDaysAgo: Math.min(hat.days, 365),
      ...lifecycle,
    })
    if (content) {
      b.configure(key, content, recently(b))
    }
  }
  const owner = hat.company?.title ?? `${firstName(person)}’s`
  if (product === 'accounts' && hat.company?.accountantEmail) {
    run('monthly-statement', `${owner} monthly statement`, statementRun)
  }
  if (product === 'investments' && person.managed) {
    // The model portfolio is the risk profile of the entitlement, read through the run's own configuration.
    run('rebalance', `${owner} quarterly rebalancing`, {
      modelPortfolio: '@config("/riskProfile")',
      ...(b.rng.chance(0.5) ? { driftThresholdPercent: 3 } : {}),
    })
  }
  if (product === 'cards' && hat.company?.annualFee) {
    // The batch holds the fee of each currency; a run names the account to debit, and its currency outside Norway.
    const { currency } = countries[person.country]
    run('annual-fee', `${owner} annual card fee`, {
      debitAccount: '@config("/settlementAccount")',
      ...(currency === 'NOK' ? {} : { currency }),
    })
  }
}

/** A person with their hats, entitlements, product uses and batch runs, for the products the view offers. */
function addPerson(b: ScopeBuilder, person: Person, options: { self?: boolean; values?: Content } = {}) {
  const timeZone = countries[person.country].timeZone
  const offered = (names: readonly ProductName[]) => names.filter((product) => b.has(`rst.${product}`))
  const companies = (person.companies ?? []).filter((company) => offered(company.products).length)
  if (options.self === false && !companies.length) {
    return
  }

  const days = person.since ? person.since * 365 - b.rng.int(0, 180) : b.rng.int(2, 6)
  b.subject(person.name, {
    ...personMeta(person, options.values),
    createdDaysAgo: days,
    ...(person.segment === 'onboarding' ? { description: 'Signed up in the app; the KYC check is not finished.' } : {}),
  })
  b.configure(`sbt.${person.name}`, identityOf(person), recently(b))

  const hats: Hat[] = []
  if (options.self !== false) {
    addPrivateHat(b, person, days)
    hats.push({ context: 'self', products: offered(person.products), days })
  }
  for (const company of companies) {
    const since = Math.max(20, days - b.rng.int(60, 600))
    addCompanyHat(b, person, company, since)
    hats.push({ context: company.name, company, products: offered(company.products), days: since })
  }

  // One entitlement per product, whichever hats use it.
  const entitlements = new Map<ProductName, Meta | undefined>()
  for (const product of productNames.filter((name) => hats.some((hat) => hat.products.includes(name)))) {
    const meta = person.lifecycle?.[product]
    entitlements.set(product, meta)
    b.usage(person.name, product, {
      title: `${products[product].title} for ${firstName(person)}`,
      timeZone,
      createdDaysAgo: Math.min(days, products[product].days),
      ...(product === 'insurance' ? { values: { policyNumber: `FT-${person.customerNumber}` } } : {}),
      ...meta,
    })
    const content = entitlementContent(b, person, product, hats)
    if (content) {
      b.configure(`usg.${person.name}.${product}`, content, recently(b))
    }
  }

  for (const hat of hats) {
    for (const product of hat.products) {
      const meta = person.lifecycle?.[`${product}.${hat.context}`]
      const key = b.execution(person.name, product, hat.context, {
        title: hat.company
          ? `${hat.company.title} ${products[product].business}`
          : `${firstName(person)}’s ${personalTitle(person, product)}`,
        labels: hat.company ? ['business'] : undefined,
        timeZone,
        createdDaysAgo: Math.min(hat.days, products[product].days),
        ...inherited(entitlements.get(product)),
        ...meta,
      })
      const content = useContent(b, person, product, hat)
      if (content) {
        b.configure(key, content, recently(b))
      }
      batchRuns(b, person, product, hat, { ...inherited(entitlements.get(product)), ...inherited(meta) })
    }
  }
}

function defaultView(b: ScopeBuilder) {
  addProducts(b, ['accounts', 'payments', 'cards', 'investments', 'savings', 'insurance'], true, production)
  productDocuments(b, production)
  schemas(b, production)
  templates(b)
  alexSubject(b)
  alexHats(b, Object.keys(alexHatDays))
  alexUses(b, alexDefaultUses)
  customers.forEach((person) => addPerson(b, person))

  // Anders closed every account: the person is in Deleted with both hats. Linnéa cancelled her travel insurance.
  b.trash('sbt.anders', { daysAgo: 24, by: 'frances' })
  b.trash('exe.linnea.insurance.self', { daysAgo: 6, by: 'hedy' })
}

/** The board's release candidate: company cards for Harbor Studio and the new lending product, business hats only. */
function releaseCandidateView(b: ScopeBuilder) {
  addProducts(b, ['cards', 'lending'], false, releaseCandidate)
  productDocuments(b, releaseCandidate)
  schemas(b, releaseCandidate)
  templates(b)
  alexSubject(b)
  alexHats(b, ['bright-forge', 'harbor-studio'])
  alexUses(b, ['cards.harbor-studio', 'lending.bright-forge'])
  // The business customers whose company cards and credit lines the release carries.
  for (const person of customers) {
    const companies = person.companies?.map((company) => ({
      ...company,
      products: [...company.products, ...(creditLines[company.name] ? (['lending'] as const) : [])],
    }))
    addPerson(b, { ...person, companies }, { self: false })
  }
}

/** Development: three test people on the sandbox rails, with debug logging and fixtures. */
function developmentView(b: ScopeBuilder) {
  addProducts(b, ['accounts', 'payments', 'cards'], true, development)
  productDocuments(b, development)
  schemas(b, development)
  templates(b)
  for (const person of testCustomers.slice(0, 3)) {
    addPerson(b, person, { values: { fixture: `fixtures/people/${person.name}.json`, testIdentity: 'bankid-test' } })
  }
}

/** Staging: the test people of the four countries and their test companies, on the staging core banking. */
function stagingView(b: ScopeBuilder) {
  addProducts(b, ['accounts', 'payments', 'cards', 'investments', 'savings', 'insurance'], true, staging)
  productDocuments(b, staging)
  schemas(b, staging)
  templates(b)
  testCustomers.forEach((person) => addPerson(b, person))
}

/** Round-up savings: card purchases are rounded up, and every night the differences move into savings. */
function featureDeliveryView(b: ScopeBuilder) {
  addProducts(b, ['cards', 'savings'], false, featureDelivery)
  b.unit('savings', 'round-up-sweep', {
    unitType: 'time',
    definition: '0 1 * * *',
    title: 'Round-up sweep',
    description: 'Moves the round-ups of yesterday’s card purchases into the savings account.',
    link: `${docs}/features/round-ups`,
    labels: ['feature'],
    createdDaysAgo: 12,
    createdBy: 'hedy',
  })
  productDocuments(b, featureDelivery)
  schemas(b, featureDelivery)
  templates(b)
  alexSubject(b)
  alexHats(b, ['self'])
  alexUses(b, ['cards.self', 'savings.self'])
  // Two more card holders join the pilot with a new savings account; the multiplier rounds up two or three times.
  const multipliers: Record<string, number> = { alex: 2, ingrid: 1, jonas: 3 }
  const pilots = customers.filter((person) => person.name in multipliers)
  for (const person of pilots) {
    addPerson(b, {
      ...person,
      products: ['cards', 'savings'],
      companies: [],
      lifecycle: { ...person.lifecycle, savings: pilot },
    })
  }
  for (const person of [alex, ...pilots]) {
    const key = b.unitOfExecution(person.name, 'savings', 'self', 'round-up-sweep', {
      title: `${firstName(person)}’s round-up sweep`,
      labels: ['pilot'],
      createdDaysAgo: b.rng.int(6, 10),
      createdBy: 'hedy',
    })
    b.configure(key, { multiplier: multipliers[person.name], sourceCard: `exe.${person.name}.cards.self` })
  }
}

export const moneySuite: ShowcaseProject = {
  org: 'bluefin',
  project: 'money-suite',
  summary:
    'The consumer finance suite of the documentation: people, the legal entities they act as, and the products they use.',
  members: { frances: 'Owner', hedy: 'ContributorWithSecrets', ada: 'Contributor', radia: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Product', color: 'indigo', icon: 'i-lucide-package' },
      unt: { label: 'Batch', color: 'purple', icon: 'i-lucide-layers' },
      sbt: { label: 'Person', plural: 'People', color: 'sky', icon: 'i-lucide-user' },
      usg: { label: 'Entitlement', color: 'amber', icon: 'i-lucide-key' },
      cnt: { label: 'Legal entity', color: 'teal', icon: 'i-lucide-landmark' },
      exe: { label: 'Product use', color: 'green', icon: 'i-lucide-briefcase' },
      uxe: { label: 'BatchRun', color: 'orange', icon: 'i-lucide-workflow' },
    },
    defaultTimeZone: 'Europe/Oslo',
    dateFormat: 'dd.mm.yyyy',
  },
  views: environmentViews({
    development: 'Development: test people on the sandbox rails',
    staging: 'Staging: Nordic test customers on the staging core banking',
    'release-candidate': 'Release 2026.11: company cards for Harbor Studio, the lending product',
    'feature-delivery1': 'Round-up savings for three pilot customers',
  }),
  build: (b) => {
    switch (b.view) {
      case 'default':
        return defaultView(b)
      case 'release-candidate':
        return releaseCandidateView(b)
      case 'development':
        return developmentView(b)
      case 'staging':
        return stagingView(b)
      case 'feature-delivery1':
        return featureDeliveryView(b)
    }
  },
}
