import { getAllAncestors } from '@/utils/annotation-key'
import { seededRng, type Meta, type ScopeBuilder, type UnitMeta } from '../builder'
import { evolve } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// Example 9 of the Miro board: Meridian Health, a hospital network, integrates its clinical systems department by
// department. A hospital licenses a clinical system (usage), a department runs an integration of it (execution), and
// an interface of the system runs as a feed in one department (unit of execution). The board draws St. Anne;
// Riverside and Northgate make it a network. The domain is regulated, so Ada is a Reader here: changes go through
// change control, and every integration names its endpoint and its client certificate in the vault.

const docs = 'https://docs.meridian-health.example/clinical-integrations'
const domain = 'meridian-health.example'

type HospitalName = 'st-anne' | 'riverside' | 'northgate'
type SystemName = 'lab-results' | 'imaging' | 'e-prescription' | 'patient-portal' | 'billing-claims'
type Content = Record<string, unknown>

interface Hospital {
  name: HospitalName
  title: string
  /** The name in the titles of its departments, licenses and integrations. */
  short: string
  /** The HL7 sending facility (MSH-4); the integrations bind to it. */
  facilityCode: string
  timeZone: string
  /** Staffed beds; the license terms scale with them. */
  beds: number
  labels: string[]
  values: Content
  description?: string
  /** The clinical systems the hospital licenses. */
  licenses: SystemName[]
  createdDaysAgo: number
}

const hospitals: Hospital[] = [
  {
    name: 'st-anne',
    title: 'St. Anne’s Medical Center',
    short: 'St. Anne',
    facilityCode: 'STANNE',
    timeZone: 'America/Chicago',
    beds: 486,
    labels: ['teaching', 'chicago-metro'],
    values: { npi: '1497758544', cmsCertificationNumber: '140088', city: 'Chicago, IL' },
    licenses: ['lab-results', 'imaging', 'e-prescription', 'patient-portal', 'billing-claims'],
    createdDaysAgo: 900,
  },
  {
    name: 'riverside',
    title: 'Riverside General Hospital',
    short: 'Riverside',
    facilityCode: 'RIVGEN',
    timeZone: 'America/Indiana/Indianapolis',
    beds: 212,
    labels: ['community', 'indiana'],
    values: { npi: '1316024789', cmsCertificationNumber: '150034', city: 'Indianapolis, IN' },
    // Riverside has no patient portal of its own yet; its patients use the network's call center.
    licenses: ['lab-results', 'imaging', 'e-prescription', 'billing-claims'],
    createdDaysAgo: 760,
  },
  {
    name: 'northgate',
    title: 'Northgate Regional Hospital',
    short: 'Northgate',
    facilityCode: 'NGATE',
    timeZone: 'America/Detroit',
    beds: 318,
    labels: ['regional', 'michigan'],
    values: { npi: '1720395569', cmsCertificationNumber: '230097', city: 'Grand Rapids, MI', joinedNetwork: '2025-08' },
    description:
      'Joined the Meridian network in August 2025. A year later, with the MPI migration of summer 2026, its patient identifiers and DICOM AE titles moved to the network’s naming.',
    licenses: ['lab-results', 'imaging', 'e-prescription', 'patient-portal', 'billing-claims'],
    createdDaysAgo: 430,
  },
]

const hospitalOf = (name: HospitalName) => hospitals.find((hospital) => hospital.name === name)!

/** The same audit dates and authors for a key in every view: production's copies show the same history. */
const rngOf = (key: string) => seededRng(`meridian-health/clinical-integrations/${key}`)

const roundTo = (value: number, step: number) => Math.round(value / step) * step

interface ClinicalSystem {
  name: SystemName
  title: string
  /** The short name in certificate names and interface ids. */
  short: string
  vendor: string
  labels: string[]
  description: string
  /** The hospital-wide connection; the license holds it and the integrations inherit it. */
  endpoint: (hospital: HospitalName) => string
  /** What the vendor licenses, sized by the hospital's beds. */
  terms: (beds: number) => Content
}

const systems: ClinicalSystem[] = [
  {
    name: 'lab-results',
    title: 'Lab results',
    short: 'lab',
    vendor: 'Cobalt Diagnostics',
    labels: ['hl7v2', 'phi'],
    description:
      'The laboratory information system (**Cobalt LIS**).\n\n- Orders come in as `OML^O21`.\n- Results go out as `ORU^R01`, point-of-care results as `ORU^R30`.\n- Critical values are called back to the ordering department.',
    endpoint: (hospital) => `mllp://lis.${hospital}.${domain}:2575`,
    terms: (beds) => ({ concurrentUsers: roundTo(beds * 0.85, 10) }),
  },
  {
    name: 'imaging',
    title: 'Imaging',
    short: 'imaging',
    vendor: 'Clearview Imaging',
    labels: ['dicom', 'phi'],
    description: 'PACS and the modality worklist (**Clearview PACS 9**); studies are routed over DICOM.',
    endpoint: (hospital) => `dicom://pacs.${hospital}.${domain}:11112`,
    terms: (beds) => ({ readingStations: Math.round(beds / 20), studiesPerYear: roundTo(beds * 520, 1000) }),
  },
  {
    name: 'e-prescription',
    title: 'E-prescription',
    short: 'erx',
    vendor: 'RxLink Networks',
    labels: ['ncpdp', 'phi'],
    description:
      'Electronic prescriptions to retail and specialty pharmacies over NCPDP SCRIPT 2017071, controlled substances included (EPCS).',
    endpoint: () => 'https://hub.rxlink.example/ncpdp/script/2017071',
    terms: (beds) => ({ prescribers: roundTo(beds * 1.1, 5) }),
  },
  {
    name: 'patient-portal',
    title: 'Patient portal',
    short: 'portal',
    vendor: 'Meridian Digital',
    labels: ['fhir', 'phi'],
    description: 'Results, medications and messages for patients and their proxies, over FHIR R4.',
    endpoint: (hospital) => `https://fhir.${hospital}.${domain}/r4`,
    terms: (beds) => ({ activePatients: roundTo(beds * 310, 1000) }),
  },
  {
    name: 'billing-claims',
    title: 'Billing and claims',
    short: 'claims',
    vendor: 'ClaimBridge',
    labels: ['x12'],
    description: 'Charge capture and X12 837 claims to the payers through the ClaimBridge clearinghouse.',
    endpoint: (hospital) => `sftp://edi.claimbridge.example/inbound/${hospital}`,
    terms: (beds) => ({ claimsPerMonth: roundTo(beds * 95, 100) }),
  },
]

const systemOf = (name: SystemName) => systems.find((system) => system.name === name)!

interface Interface extends Pick<UnitMeta, 'unitType' | 'definition'> {
  system: SystemName
  name: string
  title: string
  description: string
}

/** One interface per clinical system; its feeds run it in a department. */
const interfaces: Interface[] = [
  {
    system: 'lab-results',
    name: 'hl7-feed',
    title: 'HL7 results feed',
    unitType: 'message',
    definition: 'hl7.lab-results.oru-r01',
    description: 'Results from the LIS to the departments through the interface engine, as HL7 v2.5.1.',
  },
  {
    system: 'imaging',
    name: 'dicom-router',
    title: 'DICOM router',
    unitType: 'event',
    definition: 'dicom.study.received',
    description: 'Routes each received study to the PACS archive and the reading worklists.',
  },
  {
    system: 'patient-portal',
    name: 'fhir-export',
    title: 'FHIR bulk export',
    unitType: 'time',
    definition: '0 2 * * *',
    description: 'Nightly FHIR `$export` of the new results and medications to the portal.',
  },
  {
    system: 'billing-claims',
    name: 'claims-batch',
    title: 'Claims batch',
    unitType: 'file',
    definition: 'edi/outbound/837-claims.x12',
    description: 'The X12 837 batch file the department’s charges go out in.',
  },
  {
    system: 'e-prescription',
    name: 'renewal-requests',
    title: 'Renewal requests',
    unitType: 'message',
    definition: 'ncpdp.script.rx-renewal-request',
    description: 'Pharmacies’ `RxRenewalRequest` messages, routed to the prescriber pool of the department.',
  },
]

const interfaceOf = (system: SystemName) => interfaces.find((entry) => entry.system === system)!

interface Department {
  hospital: HospitalName
  name: string
  title: string
  /** The HL7 point of care (PV1-3); the feeds bind to it. */
  locationCode: string
  labels: string[]
  /** The nurse manager, who signs off the department's changes. */
  manager: string
  /** The clinical systems integrated in the department. */
  systems: SystemName[]
  /** The systems whose interface has a feed in the department. */
  feeds: SystemName[]
  /** Department settings: the label printer and the phone critical results are called to. */
  config?: Content
}

const departments: Department[] = [
  // St. Anne: the three departments of the board, then the rest of the hospital.
  {
    hospital: 'st-anne',
    name: 'emergency',
    title: 'Emergency department',
    locationCode: 'ED',
    labels: ['acute', 'level-1-trauma'],
    manager: 'r.alvarez',
    systems: ['lab-results', 'imaging'],
    feeds: ['lab-results'],
    config: {
      labelPrinter: `zebra-ed-01.st-anne.${domain}`,
      criticalResultsPhone: '+1 312 555 0142',
      resultInbox: 'ED-ATTENDING',
    },
  },
  {
    hospital: 'st-anne',
    name: 'cardiology',
    title: 'Cardiology',
    locationCode: 'CARD',
    labels: ['inpatient'],
    manager: 'k.nguyen',
    systems: ['lab-results', 'e-prescription'],
    feeds: ['lab-results'],
  },
  {
    hospital: 'st-anne',
    name: 'oncology',
    title: 'Oncology',
    locationCode: 'ONC',
    labels: ['outpatient'],
    manager: 'm.okafor',
    systems: ['imaging', 'e-prescription'],
    feeds: [],
  },
  {
    hospital: 'st-anne',
    name: 'radiology',
    title: 'Radiology',
    locationCode: 'RAD',
    labels: ['diagnostic'],
    manager: 'j.kowalski',
    systems: ['imaging', 'billing-claims'],
    feeds: ['imaging', 'billing-claims'],
  },
  {
    hospital: 'st-anne',
    name: 'pediatrics',
    title: 'Pediatrics',
    locationCode: 'PEDS',
    labels: ['inpatient'],
    manager: 's.patel',
    systems: ['lab-results', 'e-prescription', 'patient-portal'],
    feeds: ['lab-results', 'patient-portal'],
  },
  {
    hospital: 'st-anne',
    name: 'intensive-care',
    title: 'Intensive care unit',
    locationCode: 'ICU',
    labels: ['acute'],
    manager: 'd.fischer',
    systems: ['lab-results', 'e-prescription'],
    feeds: ['lab-results'],
    config: {
      labelPrinter: `zebra-icu-02.st-anne.${domain}`,
      criticalResultsPhone: '+1 312 555 0177',
      resultInbox: 'ICU-INTENSIVIST',
    },
  },
  {
    hospital: 'riverside',
    name: 'emergency',
    title: 'Emergency department',
    locationCode: 'ED',
    labels: ['acute'],
    manager: 'l.brooks',
    systems: ['lab-results', 'imaging', 'e-prescription'],
    feeds: ['lab-results'],
  },
  {
    hospital: 'riverside',
    name: 'cardiology',
    title: 'Cardiology',
    locationCode: 'CARD',
    labels: ['inpatient'],
    manager: 'a.haddad',
    systems: ['lab-results', 'imaging', 'e-prescription'],
    feeds: ['lab-results'],
  },
  {
    hospital: 'riverside',
    name: 'maternity',
    title: 'Maternity',
    locationCode: 'MAT',
    labels: ['inpatient'],
    manager: 'e.moreno',
    systems: ['lab-results', 'e-prescription'],
    feeds: ['lab-results', 'e-prescription'],
    config: {
      labelPrinter: `zebra-mat-01.riverside.${domain}`,
      criticalResultsPhone: '+1 317 555 0118',
      resultInbox: 'OB-ON-CALL',
    },
  },
  {
    hospital: 'riverside',
    name: 'radiology',
    title: 'Radiology',
    locationCode: 'RAD',
    labels: ['diagnostic'],
    manager: 't.lindqvist',
    systems: ['imaging', 'billing-claims'],
    feeds: ['imaging', 'billing-claims'],
  },
  {
    hospital: 'riverside',
    name: 'orthopedics',
    title: 'Orthopedics',
    locationCode: 'ORTH',
    labels: ['outpatient'],
    manager: 'p.osei',
    systems: ['lab-results', 'imaging', 'billing-claims'],
    feeds: ['lab-results'],
  },
  {
    hospital: 'northgate',
    name: 'emergency',
    title: 'Emergency department',
    locationCode: 'ED',
    labels: ['acute'],
    manager: 'c.yamamoto',
    systems: ['lab-results', 'imaging', 'e-prescription'],
    feeds: ['lab-results'],
  },
  {
    hospital: 'northgate',
    name: 'oncology',
    title: 'Oncology',
    locationCode: 'ONC',
    labels: ['outpatient', 'cancer-center'],
    manager: 'b.ferreira',
    systems: ['lab-results', 'imaging', 'e-prescription', 'patient-portal'],
    feeds: ['lab-results', 'patient-portal'],
    config: {
      labelPrinter: `zebra-onc-01.northgate.${domain}`,
      criticalResultsPhone: '+1 616 555 0163',
      resultInbox: 'ONC-INFUSION',
    },
  },
  {
    hospital: 'northgate',
    name: 'radiology',
    title: 'Radiology',
    locationCode: 'RAD',
    labels: ['diagnostic'],
    manager: 'h.novak',
    systems: ['imaging', 'billing-claims'],
    feeds: ['imaging', 'billing-claims'],
  },
  {
    hospital: 'northgate',
    name: 'intensive-care',
    title: 'Intensive care unit',
    locationCode: 'ICU',
    labels: ['acute'],
    manager: 'g.mensah',
    systems: ['lab-results', 'e-prescription'],
    feeds: ['lab-results'],
  },
  {
    hospital: 'northgate',
    name: 'outpatient-clinic',
    title: 'Outpatient clinic',
    locationCode: 'OPC',
    labels: ['outpatient'],
    manager: 'n.ivanova',
    systems: ['lab-results', 'e-prescription', 'patient-portal', 'billing-claims'],
    feeds: ['e-prescription', 'patient-portal'],
    config: { labelPrinter: `zebra-opc-03.northgate.${domain}`, resultInbox: 'OPC-PROVIDERS' },
  },
  {
    hospital: 'northgate',
    name: 'urgent-care-annex',
    title: 'Urgent care annex',
    locationCode: 'UCA',
    labels: ['outpatient'],
    manager: 'f.duarte',
    systems: ['lab-results', 'e-prescription'],
    feeds: ['lab-results'],
    config: { labelPrinter: `zebra-uca-01.northgate.${domain}`, criticalResultsPhone: '+1 616 555 0129' },
  },
]

const departmentOf = (hospital: HospitalName, name: string) =>
  departments.find((department) => department.hospital === hospital && department.name === name)!

const certificate = (hospital: string, system: string) => `@(certificates.${hospital}-${system}, primaryVault)`
const downtimeProcedure = (system: string) => `${docs}/downtime/${system}`

/** The hospital-wide connection of a system: the license stores it, an integration repeats or overrides it. */
const connectionOf = (hospital: HospitalName, system: SystemName): Content => ({
  endpoint: systemOf(system).endpoint(hospital),
  certificateRef: certificate(hospital, systemOf(system).short),
})

// The titles: "St. Anne lab results license", "Lab results in the St. Anne emergency department", "HL7 results feed for
// St. Anne cardiology". A department named as a place (department, unit, clinic, annex, ward) takes the article.
const placeOf = (where: string) => (/ (department|unit|clinic|annex|ward)$/.test(where) ? `the ${where}` : where)
const departmentTitle = (hospital: Hospital, title: string) => `${hospital.short} ${title.toLowerCase()}`
const licenseTitle = (hospital: Hospital, system: string) => `${hospital.short} ${system.toLowerCase()} license`
const integrationTitle = (system: string, where: string) => `${system} in ${placeOf(where)}`
const feedTitle = (unit: string, where: string) => `${unit} for ${placeOf(where)}`

/**
 * Integrations whose department differs from the hospital's connection, by `hospital.system.department`, one content
 * per version. Only these store a document of their own; the others inherit the license's connection.
 */
const setups: Record<string, Content | Content[]> = {
  // The ED gets its own STAT channel on the LIS; the callback window was tightened twice after audits.
  'st-anne.lab-results.emergency': evolve(
    { endpoint: `mllp://lis.st-anne.${domain}:2577`, priority: 'routine', criticalCallbackMinutes: 30 },
    5,
    (version) =>
      [
        { priority: 'stat' },
        { criticalCallbackMinutes: 20 },
        { reflexTesting: ['lactate-repeat-2h'] },
        { criticalCallbackMinutes: 15 },
      ][version - 2]!,
  ),
  'st-anne.lab-results.cardiology': { reflexTesting: ['hs-troponin-1h', 'hs-troponin-3h'] },
  'st-anne.lab-results.intensive-care': {
    endpoint: `mllp://lis.st-anne.${domain}:2577`,
    priority: 'stat',
    criticalCallbackMinutes: 15,
  },
  'st-anne.imaging.emergency': { callingAeTitle: 'STANNE_ED', modalities: ['CT', 'CR', 'US'], worklist: 'ED-TRAUMA' },
  'st-anne.imaging.oncology': { callingAeTitle: 'STANNE_ONC', modalities: ['CT', 'PT', 'MR'] },
  'st-anne.imaging.radiology': {
    callingAeTitle: 'STANNE_RAD',
    modalities: ['CT', 'MR', 'CR', 'US', 'MG', 'NM'],
    worklist: 'RAD-ALL',
  },
  // Chemotherapy orders need the sign-off of a second pharmacist before they leave the hospital.
  'st-anne.e-prescription.oncology': { formulary: 'oncology-chemo', dualSignOff: true },
  // Parents keep proxy access until 18 in pediatrics (the network ends it at 12); from 12 on, the confidential results
  // go to the adolescent only.
  'st-anne.patient-portal.pediatrics': {
    proxyAccess: true,
    proxyAgeLimit: 18,
    adolescentConfidentiality: { fromAge: 12, categories: ['reproductive-health', 'behavioral-health'] },
  },
  'st-anne.billing-claims.radiology': { claimType: '837P', chargeLagDays: 2, billingProvider: 'Lakeshore Radiology' },
  'riverside.lab-results.emergency': { priority: 'stat', criticalCallbackMinutes: 15 },
  // The orthopedic center's lab moved to the main campus; since the move its results come back over a channel of
  // their own (port 2580), which replaced the interim feed.
  'riverside.lab-results.orthopedics': { endpoint: `mllp://lis.riverside.${domain}:2580` },
  'riverside.imaging.radiology': { callingAeTitle: 'RIVGEN_RAD', modalities: ['CT', 'MR', 'CR', 'US'] },
  'riverside.imaging.orthopedics': { callingAeTitle: 'RIVGEN_ORTH', modalities: ['CR', 'MR'] },
  'riverside.billing-claims.radiology': { claimType: '837P', chargeLagDays: 3 },
  'riverside.billing-claims.orthopedics': { claimType: '837P', chargeLagDays: 5 },
  'northgate.lab-results.emergency': { priority: 'stat', criticalCallbackMinutes: 15 },
  'northgate.lab-results.urgent-care-annex': { endpoint: `mllp://lis.northgate.${domain}:2581` },
  // A year after Northgate joined, the MPI migration of summer 2026 moved its AE titles to the network's naming.
  'northgate.imaging.radiology': [
    { callingAeTitle: 'NGRH_CT1', modalities: ['CT', 'MR', 'CR'] },
    { callingAeTitle: 'NGATE_RAD', modalities: ['CT', 'MR', 'CR'] },
    { callingAeTitle: 'NGATE_RAD', modalities: ['CT', 'MR', 'CR', 'US', 'MG'] },
  ],
  'northgate.imaging.oncology': { callingAeTitle: 'NGATE_ONC', modalities: ['CT', 'PT'] },
  // The cancer center shows the care plans too, and routes the patients' messages to its nurse navigators.
  'northgate.patient-portal.oncology': {
    resources: ['Observation', 'CarePlan'],
    messagePool: 'oncology-nurse-navigators',
    messageResponseHours: 24,
  },
  'northgate.e-prescription.outpatient-clinic': { pharmacyDirectory: 'retail', epcs: { twoFactor: 'push' } },
  'northgate.billing-claims.outpatient-clinic': { claimType: '837I', chargeLagDays: 3 },
  'northgate.billing-claims.radiology': { claimType: '837P', chargeLagDays: 2 },
}

/** Feeds of the other interfaces that differ from the interface's defaults, by `hospital.system.department`. */
const feedSetups: Record<string, Content | Content[]> = {
  'st-anne.imaging.radiology': [
    { routes: [{ modality: 'CT', destination: 'STANNE_PACS' }], compression: 'jpeg2000-lossless' },
    {
      routes: [
        { modality: 'CT', destination: 'STANNE_PACS' },
        { modality: 'MG', destination: 'STANNE_BREAST' },
      ],
      compression: 'jpeg2000-lossless',
    },
  ],
  'st-anne.billing-claims.radiology': { cutoff: '22:00', claimType: '837P' },
  'riverside.e-prescription.maternity': { responseSlaHours: 24, routeTo: 'ob-pharmacists' },
  'riverside.billing-claims.radiology': { cutoff: '21:00', claimType: '837P' },
  'northgate.patient-portal.oncology': {
    cron: '30 3 * * *',
    resources: ['Observation', 'DiagnosticReport', 'CarePlan'],
  },
}

/**
 * Enhanced acknowledgment: valuing MSH-15 and MSH-16 asks the receiver for an accept acknowledgment always (AL) and an
 * application acknowledgment only on an error (ER). Original mode leaves both empty.
 */
const enhanced: Content = { ackMode: 'enhanced', acknowledgment: { accept: 'AL', application: 'ER' } }

/** The HL7 feeds whose message types or acknowledgment differ, by `hospital.department`. */
const hl7Setups: Record<string, Content> = {
  'st-anne.emergency': { messageTypes: ['ORU^R01', 'ORU^R30'], ...enhanced },
  'st-anne.intensive-care': { messageTypes: ['ORU^R01', 'ORU^R30'], ...enhanced },
  'riverside.emergency': enhanced,
  'northgate.emergency': { messageTypes: ['ORU^R01', 'ORU^R30'] },
}

/** The lifecycle examples of production, by key. */
const lifecycle: Record<string, Meta> = {
  'exe.riverside.e-prescription.cardiology': {
    startsIn: 14,
    createdDaysAgo: 6,
    createdBy: 'barbara',
    labels: ['go-live'],
    description: 'Goes live with the cardiology e-prescribing rollout (change **CHG-24310**).',
  },
  'uxe.riverside.lab-results.orthopedics.hl7-feed': {
    title: 'Interim HL7 results feed for Riverside orthopedics',
    createdDaysAgo: 110,
    expiresIn: -9,
    labels: ['interim'],
    description:
      'Carried the results to the orthopedic center while its lab moved to the main campus (change CHG-23562). Since the move the results come straight back over the integration’s own channel on port 2580.',
  },
  'usg.riverside.imaging': {
    expiresIn: 75,
    labels: ['renewal-pending'],
    description: 'The Clearview contract ends in about ten weeks; the renewal is with procurement.',
  },
}

/** Deleted in production: the closed annex with its integrations, and a claims batch the radiology group took over. */
const deletions: { key: string; daysAgo: number; by: PersonaId }[] = [
  { key: 'cnt.northgate.urgent-care-annex', daysAgo: 26, by: 'barbara' },
  { key: 'uxe.riverside.billing-claims.radiology.claims-batch', daysAgo: 8, by: 'donald' },
]

const deletionOf = (key: string) =>
  deletions.find((deletion) => key === deletion.key || getAllAncestors(key).includes(deletion.key))

const isDeleted = (key: string) => deletionOf(key) !== undefined

/** The age of the newest version: what production deleted was last edited a few days before the deletion. */
const lastDaysAgoOf = (key: string, fallback: number) => {
  const deletion = deletionOf(key)
  return deletion ? deletion.daysAgo + rngOf(`${key}/deleted`).int(1, 3) : fallback
}

/** Only the members who may write secrets author the documents with a vault binding. */
const secretAuthors: PersonaId[] = ['donald', 'barbara']

interface Options {
  /** The release candidate: the next release's changes. */
  next: boolean
  /** Whether to add an annotation: the release candidate leaves out what production deleted. */
  keep: (key: string) => boolean
}

function addSystems(b: ScopeBuilder, names: SystemName[], interfaceNames: string[], next: boolean) {
  for (const system of systems.filter((entry) => names.includes(entry.name))) {
    b.responsibility(system.name, {
      title: system.title,
      description: system.description,
      labels: system.labels,
      link: `${docs}/systems/${system.name}`,
      values: { vendor: system.vendor, systemOwner: `${system.short}-it@${domain}` },
      createdDaysAgo: 1100,
    })
  }
  for (const entry of interfaces.filter((candidate) => interfaceNames.includes(candidate.name))) {
    // The router is off while it is upgraded to 5.2; the studies queue at the modalities meanwhile.
    const paused = entry.name === 'dicom-router' && !next
    b.unit(entry.system, entry.name, {
      unitType: entry.unitType,
      definition: entry.definition,
      title: entry.title,
      description: paused
        ? `${entry.description}\n\n**Paused for the upgrade to router 5.2** (change CHG-24117).`
        : entry.description,
      link: `${docs}/interfaces/${entry.name}`,
      disabled: paused,
      createdDaysAgo: 1000,
      ...(paused ? { modifiedDaysAgo: 2, modifiedBy: 'barbara' as const } : {}),
    })
  }
}

/** The lab defaults of the network, tuned for half a year: the long history of the project. */
const labChanges: Record<number, Content> = {
  2: { criticalValues: { glucoseLow: 40 } },
  3: { resultStatuses: ['P', 'F', 'C'] },
  4: { criticalCallbackMinutes: 20 },
  5: { autoVerification: { enabled: true, panels: ['bmp'] } },
  6: { criticalValues: { sodiumLow: 120, sodiumHigh: 160 } },
  7: { autoVerification: { panels: ['bmp', 'cbc'] } },
  8: { criticalValues: { hsTroponinHigh: 52 } },
  9: { loincRelease: '2.81' },
  10: { criticalCallbackMinutes: 15 },
  11: { criticalValues: { potassiumHigh: 6.2 } },
  12: { autoVerification: { panels: ['bmp', 'cbc', 'cmp'] } },
  13: { criticalValues: { inrHigh: 5 } },
  14: { resultCopies: ['primary-care-provider'] },
  15: { criticalValues: { lactateHigh: 4 } },
  16: { autoVerification: { deltaCheckPercent: 20 } },
  17: { criticalValues: { plateletsLow: 20 } },
  18: { resultCopies: ['primary-care-provider', 'patient-portal'] },
  // The next release brings LOINC 2.82 and the ionized calcium critical value.
  19: { loincRelease: '2.82', criticalValues: { ionizedCalciumLow: 0.8 } },
}

/** The documents of the clinical systems and their interfaces that the scope has. */
function systemDocuments(b: ScopeBuilder, next: boolean) {
  const configure: ScopeBuilder['configure'] = (key, versions, options) => {
    if (b.has(key)) {
      b.configure(key, versions, options)
    }
  }
  configure(
    'rst.lab-results',
    evolve(
      {
        loincRelease: '2.80',
        resultStatuses: ['F', 'C'],
        criticalValues: { potassiumHigh: 6.0, potassiumLow: 2.8, glucoseLow: 45, glucoseHigh: 450 },
        criticalCallbackMinutes: 30,
        autoVerification: { enabled: false },
      },
      next ? 19 : 18,
      (version) => labChanges[version]!,
    ),
    { every: 11, lastDaysAgo: next ? 1 : 3 },
  )
  configure(
    'rst.imaging',
    evolve({ compression: 'jpeg2000-lossless', retentionYears: 7, priorsLookbackMonths: 24 }, 3, (version) =>
      version === 2 ? { priorsLookbackMonths: 36 } : { aiTriage: ['intracranial-hemorrhage', 'pulmonary-embolism'] },
    ),
    { every: 40, lastDaysAgo: 12 },
  )
  configure(
    'rst.e-prescription',
    [
      { scriptVersion: '2017071', epcs: { enabled: true, twoFactor: 'hard-token' }, pdmpCheck: 'optional' },
      { scriptVersion: '2017071', epcs: { enabled: true, twoFactor: 'hard-token' }, pdmpCheck: 'required' },
    ],
    { every: 60, lastDaysAgo: 20 },
  )
  configure(
    'rst.patient-portal',
    { resultReleaseDelayHours: 0, proxyAgeLimit: 12, languages: ['en', 'es', 'pl'], messageResponseHours: 48 },
    { lastDaysAgo: 17 },
  )
  configure(
    'rst.billing-claims',
    evolve(
      { clearinghouse: 'ClaimBridge', x12Version: '005010', timelyFilingDays: 90, scrubber: 'basic' },
      4,
      (version) =>
        version === 2
          ? { scrubber: 'ncci-edits' }
          : version === 3
            ? { attachments: 'x12-275' }
            : { scrubber: 'ncci-edits-2026q4' },
    ),
    { every: 30, lastDaysAgo: 6 },
  )
  configure(
    'unt.lab-results.hl7-feed',
    evolve(
      {
        hl7Version: '2.5.1',
        queue: 'hl7.lab-results.oru-r01',
        deadLetter: 'hl7.lab-results.dead-letter',
        maxMessageKb: 512,
      },
      4,
      (version) =>
        version === 2
          ? { maxMessageKb: 1024 }
          : version === 3
            ? { characterSet: 'UNICODE UTF-8' }
            : { replayWindowHours: 72 },
    ),
    { every: 21, lastDaysAgo: 9 },
  )
  configure(
    'unt.imaging.dicom-router',
    evolve(
      {
        routerVersion: '4.8.2',
        maxAssociations: 32,
        transferSyntaxes: ['1.2.840.10008.1.2.1', '1.2.840.10008.1.2.4.90'],
      },
      next ? 3 : 2,
      (version) =>
        version === 2
          ? { maxAssociations: 48 }
          : {
              routerVersion: '5.2.0',
              transferSyntaxes: ['1.2.840.10008.1.2.1', '1.2.840.10008.1.2.4.90', '1.2.840.10008.1.2.4.201'],
            },
    ),
    { every: 14, lastDaysAgo: next ? 1 : 4 },
  )
  configure(
    'unt.patient-portal.fhir-export',
    {
      fhirVersion: 'R4',
      format: 'ndjson',
      since: 'last-successful-run',
      resources: ['Patient', 'Observation', 'DiagnosticReport', 'MedicationRequest', 'Immunization'],
    },
    { lastDaysAgo: 22 },
  )
  configure(
    'unt.billing-claims.claims-batch',
    { implementationGuide: '005010X222A1', maxClaimsPerFile: 5000, cutoff: '21:00' },
    { lastDaysAgo: 15 },
  )
  configure(
    'unt.e-prescription.renewal-requests',
    { messageType: 'RxRenewalRequest', responseSlaHours: 72, autoDenyAfterDays: 7 },
    { lastDaysAgo: 11 },
  )
}

/**
 * The network's rules. Every integration names its endpoint and its client certificate in its own document, so a
 * change can be reviewed without reading the license (the type schema of exe); the release candidate also requires
 * the downtime procedure. A feed of the HL7 interface names its endpoint and message types. The feeds inherit the
 * HL7 acknowledgment and retry defaults of the template.
 */
function rules(b: ScopeBuilder, next: boolean) {
  const endpoint = { type: 'string', pattern: '^(mllp|dicom|https|sftp)://' }
  // The certificate rule came in October; the release candidate adds the downtime procedure on top of it.
  b.typeSchema(
    'exe',
    [
      { type: 'object', properties: { endpoint }, required: ['endpoint'] },
      {
        type: 'object',
        properties: { endpoint, certificateRef: { type: 'string', minLength: 1 } },
        required: ['endpoint', 'certificateRef'],
      },
      ...(next
        ? [
            {
              type: 'object',
              properties: {
                endpoint,
                certificateRef: { type: 'string', minLength: 1 },
                downtimeProcedure: { type: 'string', pattern: '^https://' },
              },
              required: ['endpoint', 'certificateRef', 'downtimeProcedure'],
            },
          ]
        : []),
    ],
    next ? { every: 19, lastDaysAgo: 1 } : { every: 60, lastDaysAgo: 20 },
  )
  const ackCode = { enum: ['AL', 'NE', 'ER', 'SU'] }
  b.descendantSchema(
    'unt.lab-results.hl7-feed',
    'uxe',
    {
      type: 'object',
      properties: {
        endpoint: { type: 'string', pattern: '^mllp://' },
        messageTypes: { type: 'array', minItems: 1, items: { type: 'string', pattern: '^[A-Z]{3}\\^[A-Z][0-9]{2}$' } },
        ackMode: { enum: ['original', 'enhanced'] },
        acknowledgment: { type: 'object', properties: { accept: ackCode, application: ackCode } },
        processingId: { enum: ['P', 'T', 'D'] },
      },
      required: ['endpoint', 'messageTypes'],
    },
    { lastDaysAgo: 25 },
  )
  // The HL7 acknowledgment and retry defaults of every feed: original mode (MSH-15 and MSH-16 empty) with the timeout
  // of the commit acknowledgment; after the last attempt a message goes to the dead letter queue.
  b.template(
    'uxe',
    evolve(
      {
        ackMode: 'original',
        acknowledgment: { timeoutSeconds: 30 },
        retry: { attempts: 3, backoffSeconds: 60 },
        processingId: 'P',
      },
      3,
      (version) => (version === 2 ? { retry: { attempts: 5 } } : { retry: { deadLetter: true } }),
    ),
    { every: 45, lastDaysAgo: 10 },
  )
}

/**
 * The content of an integration: the connection the schema requires (the hospital's, or a sandbox's), then the
 * department's own settings.
 */
function integrationContents(
  hospital: HospitalName,
  system: SystemName,
  setup: Content | Content[],
  next: boolean,
  connection = connectionOf(hospital, system),
) {
  const contents = (Array.isArray(setup) ? setup : [setup]).map((version) => ({
    ...connection,
    ...(system === 'lab-results' ? { sendingFacility: '@annotation("/facilityCode", "Subject")' } : {}),
    ...version,
  }))
  // The release candidate writes the downtime procedure into every integration, as a new version.
  return next ? [...contents, { ...contents.at(-1)!, downtimeProcedure: downtimeProcedure(system) }] : contents
}

/** An HL7 feed: the engine channel, the message types, the acknowledgment and the department it reports for. */
const hl7Feed = (endpoint: string, setup: Content = {}): Content => ({
  endpoint,
  messageTypes: ['ORU^R01'],
  ackMode: 'original',
  assignedLocation: '@annotation("/locationCode", "Context")',
  heartbeat: { target: '@config("/endpoint")', everySeconds: 60 },
  ...setup,
})

function addHospital(b: ScopeBuilder, hospital: Hospital) {
  b.subject(hospital.name, {
    title: hospital.title,
    description: hospital.description,
    labels: hospital.labels,
    values: { facilityCode: hospital.facilityCode, beds: hospital.beds, ...hospital.values },
    timeZone: hospital.timeZone,
    link: `${docs}/hospitals/${hospital.name}`,
    createdDaysAgo: hospital.createdDaysAgo,
    createdBy: 'annie',
  })
  const engine = `engine.${hospital.name}.${domain}`
  b.configure(
    `sbt.${hospital.name}`,
    hospital.name === 'northgate'
      ? // A year after Northgate joined, the MPI migration of summer 2026 moved its patient identifiers into the
        // network's master patient index; the read-only replica for downtime followed.
        [
          { assigningAuthority: 'NGRH', interfaceEngine: engine, downtime: { mode: 'paper' } },
          { assigningAuthority: 'NGATE', interfaceEngine: engine, downtime: { mode: 'paper' } },
          { assigningAuthority: 'NGATE', interfaceEngine: engine, downtime: { mode: 'read-only-replica' } },
        ]
      : {
          assigningAuthority: hospital.facilityCode,
          interfaceEngine: engine,
          downtime: { mode: 'read-only-replica', reportPrinter: `bca-01.${hospital.name}.${domain}` },
        },
    { every: 60, lastDaysAgo: 14 },
  )
}

/** The license of a system at a hospital: the commercial terms and the hospital-wide connection. */
function addLicense(b: ScopeBuilder, hospital: Hospital, name: SystemName, { next, keep }: Options) {
  const key = `usg.${hospital.name}.${name}`
  if (!keep(key)) {
    return
  }
  const system = systemOf(name)
  b.usage(hospital.name, name, {
    title: licenseTitle(hospital, system.title),
    timeZone: hospital.timeZone,
    values: { contractNumber: `MH-${system.short.toUpperCase()}-${hospital.facilityCode}`, vendor: system.vendor },
    createdDaysAgo: hospital.createdDaysAgo - 20,
    createdBy: 'annie',
    ...lifecycle[key],
  })
  const current: Content = {
    tier: hospital.name === 'riverside' ? 'standard' : 'enterprise',
    ...system.terms(hospital.beds),
    ...connectionOf(hospital.name, name),
  }
  // St. Anne's lab license grew twice with the teaching wards before today's terms.
  const contents: Content[] =
    hospital.name === 'st-anne' && name === 'lab-results'
      ? [{ ...current, concurrentUsers: 300 }, { ...current, concurrentUsers: 360 }, current]
      : [current]
  // The integrations without a document of their own inherit the downtime procedure of the release candidate here.
  if (next) {
    contents.push({ ...contents.at(-1)!, downtimeProcedure: downtimeProcedure(name) })
  }
  b.configure(key, contents, { every: 90, lastDaysAgo: next ? 1 : 18, authors: secretAuthors })
}

function addDepartment(
  b: ScopeBuilder,
  hospital: Hospital,
  department: Department,
  { next, keep }: Options,
  only: readonly SystemName[] = department.systems,
) {
  const context = `cnt.${hospital.name}.${department.name}`
  if (!keep(context)) {
    return
  }
  const where = departmentTitle(hospital, department.title)
  const contextRng = rngOf(context)
  b.context(hospital.name, department.name, {
    title: where,
    labels: department.labels,
    values: {
      locationCode: department.locationCode,
      costCenter: `CC-${hospital.facilityCode}-${department.locationCode}`,
      nurseManager: `${department.manager}@${hospital.name}.${domain}`,
    },
    timeZone: hospital.timeZone,
    createdDaysAgo: hospital.createdDaysAgo - contextRng.int(10, 60),
    createdBy: contextRng.pick(['annie', 'barbara'] as const),
  })
  if (department.config) {
    b.configure(context, department.config, { lastDaysAgo: lastDaysAgoOf(context, contextRng.int(5, 25)) })
  }

  // A feed runs in an integration, so it is created after it.
  const integrationCreated = new Map<SystemName, number>()
  for (const name of department.systems.filter((system) => only.includes(system))) {
    const system = systemOf(name)
    const key = `exe.${hospital.name}.${name}.${department.name}`
    if (!keep(key)) {
      continue
    }
    const rng = rngOf(key)
    const meta: Meta = {
      title: integrationTitle(system.title, where),
      timeZone: hospital.timeZone,
      values: {
        interfaceId: `INT-${hospital.facilityCode}-${department.locationCode}-${system.short.toUpperCase()}`,
        owner: `${system.short}-it@${domain}`,
      },
      createdDaysAgo: rng.int(120, hospital.createdDaysAgo - 60),
      createdBy: rng.pick(['donald', 'barbara'] as const),
      ...lifecycle[key],
    }
    b.execution(hospital.name, name, department.name, meta)
    integrationCreated.set(name, meta.createdDaysAgo!)
    const lastDaysAgo = lastDaysAgoOf(key, rng.int(2, 24))
    const setup = setups[`${hospital.name}.${name}.${department.name}`]
    if (setup) {
      b.configure(key, integrationContents(hospital.name, name, setup, next), {
        every: 20,
        lastDaysAgo: next ? 1 : lastDaysAgo,
        authors: secretAuthors,
      })
    }
  }

  const port = 6601 + departments.indexOf(department)
  for (const name of department.feeds.filter((system) => only.includes(system))) {
    const unit = interfaceOf(name)
    const key = `uxe.${hospital.name}.${name}.${department.name}.${unit.name}`
    if (!keep(key)) {
      continue
    }
    const rng = rngOf(key)
    // Production pauses the router's feeds with the router; the release candidate runs router 5.2.
    const paused = unit.name === 'dicom-router' && !next
    b.unitOfExecution(hospital.name, name, department.name, unit.name, {
      title: feedTitle(unit.title, where),
      timeZone: hospital.timeZone,
      createdDaysAgo: rng.int(45, integrationCreated.get(name)! - 7),
      createdBy: 'donald',
      ...(paused
        ? {
            disabled: true,
            description: 'Paused with the router for its upgrade to 5.2 (change CHG-24117).',
            modifiedDaysAgo: 2,
            modifiedBy: 'barbara' as const,
          }
        : {}),
      ...lifecycle[key],
    })
    const setup =
      name === 'lab-results'
        ? hl7Feed(`mllp://engine.${hospital.name}.${domain}:${port}`, hl7Setups[`${hospital.name}.${department.name}`])
        : feedSetups[`${hospital.name}.${name}.${department.name}`]
    const lastDaysAgo = lastDaysAgoOf(key, rng.int(2, 20))
    if (setup) {
      b.configure(key, setup, { every: 12, lastDaysAgo })
    }
  }
}

/** The release candidate's new clinical system: telehealth visits in St. Anne cardiology and the Northgate clinic. */
function addTelehealth(b: ScopeBuilder) {
  b.responsibility('telehealth', {
    title: 'Telehealth',
    labels: ['fhir', 'video'],
    description: 'Video visits (**VisitBridge**) booked from the scheduling system and documented as FHIR encounters.',
    link: `${docs}/systems/telehealth`,
    values: { vendor: 'VisitBridge', systemOwner: `telehealth-it@${domain}` },
    createdDaysAgo: 12,
    createdBy: 'barbara',
  })
  b.configure('rst.telehealth', { maxParticipants: 4, recording: false, waitingRoom: true, interpreterDialIn: true })
  for (const [name, department] of [
    ['st-anne', 'cardiology'],
    ['northgate', 'outpatient-clinic'],
  ] as const) {
    const hospital = hospitalOf(name)
    const endpoint = `https://visits.${name}.${domain}/api/v1`
    const connection = { endpoint, certificateRef: certificate(name, 'telehealth') }
    b.usage(name, 'telehealth', {
      title: licenseTitle(hospital, 'Telehealth'),
      timeZone: hospital.timeZone,
      values: { contractNumber: `MH-TELEHEALTH-${hospital.facilityCode}`, vendor: 'VisitBridge' },
      createdDaysAgo: 10,
      createdBy: 'annie',
    })
    b.configure(
      `usg.${name}.telehealth`,
      { tier: 'pilot', concurrentVisits: 25, ...connection },
      { authors: secretAuthors },
    )
    b.execution(name, 'telehealth', department, {
      title: integrationTitle('Telehealth', departmentTitle(hospital, departmentOf(name, department).title)),
      timeZone: hospital.timeZone,
      createdDaysAgo: 8,
      createdBy: 'donald',
    })
    b.configure(
      `exe.${name}.telehealth.${department}`,
      {
        ...connection,
        downtimeProcedure: downtimeProcedure('telehealth'),
        visitTypes: ['follow-up', 'medication-review'],
      },
      { authors: secretAuthors },
    )
  }
}

/** Production, and the release candidate as its close copy with the next release's changes. */
function production(b: ScopeBuilder, next: boolean) {
  addSystems(
    b,
    systems.map((system) => system.name),
    interfaces.map((entry) => entry.name),
    next,
  )
  systemDocuments(b, next)
  rules(b, next)
  // The release candidate starts from production as it is today: without what production deleted.
  const options: Options = { next, keep: (key) => !next || !isDeleted(key) }
  for (const hospital of hospitals) {
    addHospital(b, hospital)
    hospital.licenses.forEach((system) => addLicense(b, hospital, system, options))
    departments
      .filter((department) => department.hospital === hospital.name)
      .forEach((department) => addDepartment(b, hospital, department, options))
  }
  if (next) {
    addTelehealth(b)
    return
  }
  deletions.forEach(({ key, daysAgo, by }) => b.trash(key, { daysAgo, by }))
}

/** A test environment of St. Anne: the vendors' test endpoints and its client certificates. */
interface TestEnvironment {
  tier: 'sandbox' | 'test'
  endpoints: [SystemName, string][]
  certificateRef: (system: SystemName) => string
  /** When Annie set up the test hospital; its licenses follow two days later. */
  createdDaysAgo: number
}

const sandbox: TestEnvironment = {
  tier: 'sandbox',
  endpoints: [
    ['lab-results', `mllp://lis-sandbox.dev.${domain}:2575`],
    ['e-prescription', 'https://sandbox.rxlink.example/ncpdp/script/2017071'],
    ['patient-portal', `https://fhir-sandbox.dev.${domain}/r4`],
  ],
  certificateRef: () => '@(certificates.dev-sandbox, primaryVault)',
  createdDaysAgo: 120,
}

const testEnvironment: TestEnvironment = {
  tier: 'test',
  endpoints: [
    ['lab-results', `mllp://lis.staging.${domain}:2575`],
    // RxLink's certification environment.
    ['e-prescription', 'https://cert.rxlink.example/ncpdp/script/2017071'],
  ],
  certificateRef: (system) => certificate('st-anne', `${systemOf(system).short}-staging`),
  createdDaysAgo: 210,
}

const connectionIn = (environment: TestEnvironment, system: SystemName): Content => ({
  endpoint: environment.endpoints.find(([name]) => name === system)![1],
  certificateRef: environment.certificateRef(system),
})

/** The test hospital and its licenses on the vendors' test endpoints. */
function addTestHospital(b: ScopeBuilder, environment: TestEnvironment, meta: Meta, config: Content) {
  b.subject('st-anne', { ...meta, createdDaysAgo: environment.createdDaysAgo, createdBy: 'annie' })
  b.configure('sbt.st-anne', config)
  for (const [system] of environment.endpoints) {
    b.usage('st-anne', system, {
      title: licenseTitle(hospitalOf('st-anne'), systemOf(system).title),
      labels: [environment.tier],
      createdDaysAgo: environment.createdDaysAgo - 2,
      createdBy: 'annie',
    })
    b.configure(
      `usg.st-anne.${system}`,
      { tier: environment.tier, ...connectionIn(environment, system) },
      { authors: secretAuthors },
    )
  }
}

/** A St. Anne test ward of development or staging. */
interface Ward {
  name: string
  title: string
  locationCode: string
  environment: TestEnvironment
  /** When Barbara set it up; Donald connects its integrations two days later and its feeds the day after. */
  createdDaysAgo: number
}

function addWard(b: ScopeBuilder, ward: Ward, values: Content = {}) {
  b.context('st-anne', ward.name, {
    title: ward.title,
    labels: ['test'],
    values: { locationCode: ward.locationCode, tester: `integration-qa@${domain}`, ...values },
    createdDaysAgo: ward.createdDaysAgo,
    createdBy: 'barbara',
  })
}

/** An integration of a test ward; with a setup it stores the environment's connection and the setup, like production. */
function addWardIntegration(b: ScopeBuilder, ward: Ward, system: SystemName, setup?: Content) {
  b.execution('st-anne', system, ward.name, {
    title: integrationTitle(systemOf(system).title, ward.title),
    createdDaysAgo: ward.createdDaysAgo - 2,
    createdBy: 'donald',
  })
  if (setup) {
    b.configure(
      `exe.st-anne.${system}.${ward.name}`,
      integrationContents('st-anne', system, setup, false, connectionIn(ward.environment, system)),
      { authors: secretAuthors },
    )
  }
}

function addWardFeed(b: ScopeBuilder, ward: Ward, system: SystemName, content: Content) {
  const unit = interfaceOf(system)
  b.unitOfExecution('st-anne', system, ward.name, unit.name, {
    title: feedTitle(unit.title, ward.title),
    createdDaysAgo: ward.createdDaysAgo - 3,
    createdBy: 'donald',
  })
  b.configure(`uxe.st-anne.${system}.${ward.name}.${unit.name}`, content)
}

/** Development: St. Anne's dev and QA wards on the vendors' sandboxes, with synthetic patients and debug logging. */
function development(b: ScopeBuilder) {
  addSystems(b, ['lab-results', 'e-prescription', 'patient-portal'], ['hl7-feed', 'fhir-export'], false)
  rules(b, false)
  addTestHospital(
    b,
    sandbox,
    {
      title: 'St. Anne’s Medical Center (sandbox)',
      labels: ['sandbox'],
      values: { facilityCode: 'STANNE', testPatients: 'synthea-500' },
    },
    { assigningAuthority: 'STANNE-DEV', interfaceEngine: `engine.dev.${domain}`, logLevel: 'debug' },
  )
  const devWard: Ward = {
    name: 'dev-ward',
    title: 'St. Anne dev ward',
    locationCode: 'DEVW',
    environment: sandbox,
    createdDaysAgo: 45,
  }
  const qaWard: Ward = {
    name: 'qa-ward',
    title: 'St. Anne QA ward',
    locationCode: 'QAW',
    environment: sandbox,
    createdDaysAgo: 31,
  }
  // The dev ward sends debugging messages (processing id D), the QA ward training messages (T), each on its own port.
  for (const [ward, processingId, port] of [
    [devWard, 'D', 6601],
    [qaWard, 'T', 6602],
  ] as const) {
    addWard(b, ward)
    addWardIntegration(b, ward, 'lab-results', { logLevel: 'debug', capturePayloads: true })
    addWardFeed(
      b,
      ward,
      'lab-results',
      hl7Feed(`mllp://engine.dev.${domain}:${port}`, { messageTypes: ['ORU^R01', 'ORU^R30'], processingId }),
    )
  }
  addWardIntegration(b, qaWard, 'e-prescription', { testPharmacy: 'NCPDP 0000001', logLevel: 'debug' })
  addWardIntegration(b, devWard, 'patient-portal')
  addWardFeed(b, devWard, 'patient-portal', { cron: '*/15 * * * *', resources: ['Observation'] })
}

/** Staging, as the board draws it: the St. Anne test ward with lab results, e-prescription and the test feed. */
function staging(b: ScopeBuilder) {
  addSystems(b, ['lab-results', 'e-prescription'], ['hl7-feed'], false)
  systemDocuments(b, false)
  rules(b, false)
  addTestHospital(
    b,
    testEnvironment,
    { title: 'St. Anne’s Medical Center', labels: ['test'], values: { facilityCode: 'STANNE' } },
    { assigningAuthority: 'STANNE', interfaceEngine: `engine.staging.${domain}` },
  )
  const ward: Ward = {
    name: 'test-ward',
    title: 'St. Anne test ward',
    locationCode: 'TEST',
    environment: testEnvironment,
    createdDaysAgo: 64,
  }
  addWard(b, ward)
  b.configure('cnt.st-anne.test-ward', { labelPrinter: `zebra-test-01.staging.${domain}`, testPatients: 'synthea-5k' })
  addWardIntegration(b, ward, 'lab-results', { priority: 'stat' })
  addWardIntegration(b, ward, 'e-prescription')
  addWardFeed(
    b,
    ward,
    'lab-results',
    hl7Feed(`mllp://engine.staging.${domain}:6601`, { messageTypes: ['ORU^R01', 'ORU^R30'], processingId: 'T' }),
  )
}

/** Critical lab results pushed to the care team's secure chat, first in the St. Anne ED and ICU. */
function featureDelivery(b: ScopeBuilder) {
  addSystems(b, ['lab-results'], ['hl7-feed'], false)
  b.unit('lab-results', 'critical-alerts', {
    unitType: 'event',
    definition: 'lab.result.critical',
    title: 'Critical result alerts',
    labels: ['feature'],
    description: 'Pages the responsible nurse in secure chat when a critical value is verified; escalates if unread.',
    link: `${docs}/interfaces/critical-alerts`,
    createdDaysAgo: 9,
    createdBy: 'donald',
  })
  b.configure('unt.lab-results.critical-alerts', { channel: 'secure-chat', escalateAfterMinutes: 5, readBack: true })
  systemDocuments(b, false)
  rules(b, false)
  const stAnne = hospitalOf('st-anne')
  addHospital(b, stAnne)
  const options: Options = { next: false, keep: () => true }
  addLicense(b, stAnne, 'lab-results', options)
  for (const name of ['emergency', 'intensive-care']) {
    const department = departmentOf('st-anne', name)
    addDepartment(b, stAnne, department, options, ['lab-results'])
    b.unitOfExecution('st-anne', 'lab-results', name, 'critical-alerts', {
      title: feedTitle('Critical result alerts', departmentTitle(stAnne, department.title)),
      labels: ['feature'],
      createdDaysAgo: 7,
      createdBy: 'donald',
    })
    b.configure(`uxe.st-anne.lab-results.${name}.critical-alerts`, {
      recipients: name === 'emergency' ? 'ed-charge-nurse' : 'icu-charge-nurse',
      escalateTo: '@annotation("/nurseManager", "Context")',
    })
  }
}

export const clinicalIntegrations: ShowcaseProject = {
  org: 'meridian-health',
  project: 'clinical-integrations',
  description:
    'Clinical integrations of a hospital network: lab, imaging, e-prescription and portal systems connected per department, under change control.',
  members: {
    annie: 'Owner',
    barbara: 'Administrator',
    donald: 'ContributorWithSecrets',
    grace: 'Contributor',
    ada: 'Reader',
  },
  settings: {
    types: {
      rst: { label: 'Clinical system', color: 'indigo', icon: 'i-lucide-database' },
      unt: { label: 'Interface', color: 'fuchsia', icon: 'i-lucide-network' },
      sbt: { label: 'Hospital', color: 'red', icon: 'i-lucide-building-2' },
      usg: { label: 'License', color: 'amber', icon: 'i-lucide-key' },
      cnt: { label: 'Department', color: 'teal', icon: 'i-lucide-users' },
      exe: { label: 'Integration', color: 'emerald', icon: 'i-lucide-link' },
      uxe: { label: 'Feed', color: 'orange', icon: 'i-lucide-activity' },
    },
    defaultTimeZone: 'America/Chicago',
    dateFormat: 'mm/dd/yyyy',
  },
  views: environmentViews({
    development: 'Development: St. Anne dev and QA wards on the vendor sandboxes',
    staging: 'Staging: the St. Anne test ward with lab results and e-prescription',
    'release-candidate': 'Release 2026.12: telehealth, DICOM router 5.2 and downtime procedures',
    'feature-delivery1': 'Critical lab result alerts in the St. Anne ED and ICU',
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
