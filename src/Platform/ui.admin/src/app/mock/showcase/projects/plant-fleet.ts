import { seededRng, type Meta, type ScopeBuilder, type UnitMeta } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { PersonaId } from '../people'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// The device fleet of the documentation (docs/42for.net/platform/examples/device-fleet.md), example 8 of the Miro
// board. The site is the subject, the device the context, a capability (telemetry, a control loop, a maintenance
// window, video) the responsibility and a sampling job the unit; the resolved assignment is what the device is told
// to do. Harbor Plant has two compressors and a gateway, West Quay runs the same capabilities on other hardware, and
// three more sites of 9-12 devices make it a fleet of hundreds of annotations.

const docs = 'https://docs.harbor-industrial.example/plant-fleet'
const domain = 'harbor-industrial.example'

/** Where the devices of a view run: production (and its copies), the developers' sandbox or the staging lab. */
type Stage = 'production' | 'development' | 'staging'
/** The fleet services of a stage: the upstream broker, the historian, the CMMS and the energy inbox. */
const host = (stage: Stage, service: string) =>
  stage === 'production'
    ? `${service}.${domain}`
    : `${service}.${stage === 'development' ? 'dev' : 'staging'}.${domain}`
/** Production keeps 13 months of readings; the sandbox and the lab only what their test runs need. */
const retentionDays: Record<Stage, number> = { production: 395, development: 7, staging: 30 }

/** The alert channel lives in the site's Values, so every assignment of the site mentions it once. */
const alertChannel = '@annotation("$.alertChannel", "Subject")'
/** A firmware update reads the family of its device from the device's Values. */
const firmwareFamily = '@annotation("$.firmwareFamily", "Context")'

type Capability = 'telemetry' | 'control-loop' | 'maintenance' | 'vibration-monitoring' | 'energy-metering' | 'video'
type Job = 'sample' | 'calibrate' | 'firmware-update' | 'spectrum-capture' | 'meter-export'
type Kind = 'gateway' | 'compressor' | 'pump' | 'chiller' | 'conveyor' | 'boiler' | 'camera'
type Model =
  | 'norvik-ng400'
  | 'kestrel-kx37'
  | 'kestrel-kx55'
  | 'brenner-sc45'
  | 'halden-hp80'
  | 'polar-ch220'
  | 'ferro-bl600'
  | 'thermon-bx2'
  | 'optic-vc4'
/** The views that copy production: the release candidate adds the next release, the campaign a faster sample. */
type Variant = 'default' | 'release-candidate' | 'campaign-march'
type Content = Record<string, unknown>

const protocols = ['modbus-tcp', 'opc-ua', 'profinet', 'bacnet-ip', 'mqtt', 'rtsp'] as const

const capabilities: { name: Capability; title: string; labels: string[]; description: string }[] = [
  {
    name: 'telemetry',
    title: 'Telemetry',
    labels: ['core'],
    description: 'The gateway of a site collects the readings of its devices and publishes them to the historian.',
  },
  {
    name: 'control-loop',
    title: 'Control loop',
    labels: ['core', 'safety'],
    description: [
      'Closed-loop pressure control of compressors, pumps, chillers and boilers.',
      '',
      '- The capability ships the **safe defaults**: sample period, maximum pressure and PID tuning.',
      '- Each assignment tunes `pressureMaxBar` for its machine; a schema requires it and caps it at the rating of',
      '  the pressure vessels, **10 bar** in the fleet and **9.5 bar** at West Quay.',
    ].join('\n'),
  },
  {
    name: 'maintenance',
    title: 'Maintenance window',
    labels: ['service'],
    description: 'The weekly window in which a machine may be locked out, serviced and calibrated.',
  },
  {
    name: 'vibration-monitoring',
    title: 'Vibration monitoring',
    labels: ['condition'],
    description:
      'Velocity RMS against the ISO 20816-3 zone limits of the machine group; an alarm captures a full spectrum.',
  },
  {
    name: 'energy-metering',
    title: 'Energy metering',
    labels: ['energy'],
    description: 'Fifteen-minute consumption of the machines with a sub-meter, exported for the energy report.',
  },
  {
    name: 'video',
    title: 'Video',
    labels: ['security'],
    description: 'Camera streams recorded on the site’s NVR, with privacy masking of public areas.',
  },
]

const capabilityNames = capabilities.map((capability) => capability.name)
const capabilityTitle = (name: Capability) => capabilities.find((capability) => capability.name === name)!.title

/** The jobs of the capabilities: one of each unit type. Their defaults point at the services of the stage. */
const jobs: Record<Job, { capability: Capability; meta: UnitMeta; content: (stage: Stage) => Content }> = {
  sample: {
    capability: 'control-loop',
    meta: {
      title: 'Pressure sample',
      unitType: 'time',
      definition: '*/5 * * * *',
      description: 'Every five minutes, a burst of pressure, temperature and motor-current readings.',
    },
    content: () => ({ burstSeconds: 60, channels: ['pressure', 'temperature', 'motorCurrent'] }),
  },
  calibrate: {
    capability: 'maintenance',
    meta: {
      title: 'Sensor calibration',
      unitType: 'time',
      definition: '0 7 2 1,4,7,10 *',
      description:
        'Opens the quarterly work order: the pressure transmitters are checked against a reference gauge in the next maintenance window.',
    },
    content: () => ({ procedure: 'CAL-PT-07', toleranceBar: 0.05 }),
  },
  'firmware-update': {
    capability: 'telemetry',
    meta: {
      title: 'Firmware update',
      unitType: 'file',
      definition: 'firmware/norvik-edge-4/stable.fwpkg',
      description: 'Installs a new gateway firmware when the stable package is published.',
    },
    content: () => ({ channel: 'stable', rollbackOnFailure: true, maxParallel: 2 }),
  },
  'spectrum-capture': {
    capability: 'vibration-monitoring',
    meta: {
      title: 'Spectrum capture',
      unitType: 'event',
      definition: 'vibration.rms-alarm',
      description: 'Captures a full spectrum when the velocity RMS crosses its alarm limit.',
    },
    // Finer than the routine spectrum of the capability.
    content: () => ({ durationSeconds: 10, spectrumLines: 6400 }),
  },
  'meter-export': {
    capability: 'energy-metering',
    meta: {
      title: 'Meter export',
      unitType: 'message',
      definition: 'energy-readings',
      description: 'Exports the readings that arrive on the energy queue to the energy report.',
    },
    content: (stage) => ({ format: 'csv', target: `sftp://${host(stage, 'energy')}/inbox` }),
  },
}

const jobNames = Object.keys(jobs) as Job[]

interface Hardware {
  kind: Kind
  model: string
  firmwareFamily: string
  protocol: (typeof protocols)[number]
  /** The manufacturer's serial prefix. */
  serial: string
  /** Who services it in the maintenance window. */
  service: string
  ratedPressureBar?: number
}

const hardware: Record<Model, Hardware> = {
  'norvik-ng400': {
    kind: 'gateway',
    model: 'Norvik NG-400',
    firmwareFamily: 'norvik-edge-4',
    protocol: 'mqtt',
    serial: 'NG4',
    service: 'Norvik Systems',
  },
  'kestrel-kx37': {
    kind: 'compressor',
    model: 'Kestrel KX-37 VSD',
    firmwareFamily: 'kestrel-ctrl-5',
    protocol: 'modbus-tcp',
    serial: 'KX37',
    service: 'Kestrel Air Service',
    ratedPressureBar: 10,
  },
  'kestrel-kx55': {
    kind: 'compressor',
    model: 'Kestrel KX-55 VSD',
    firmwareFamily: 'kestrel-ctrl-5',
    protocol: 'modbus-tcp',
    serial: 'KX55',
    service: 'Kestrel Air Service',
    ratedPressureBar: 10,
  },
  'brenner-sc45': {
    kind: 'compressor',
    model: 'Brenner SC-45',
    firmwareFamily: 'brenner-s7',
    protocol: 'profinet',
    serial: 'SC45',
    service: 'Brenner Kompressoren',
    ratedPressureBar: 9.5,
  },
  'halden-hp80': {
    kind: 'pump',
    model: 'Halden HP-80',
    firmwareFamily: 'halden-drive-2',
    protocol: 'modbus-tcp',
    serial: 'HP80',
    service: 'Halden Pumps',
    ratedPressureBar: 6,
  },
  'polar-ch220': {
    kind: 'chiller',
    model: 'Polar CH-220',
    firmwareFamily: 'polar-ctl-3',
    protocol: 'bacnet-ip',
    serial: 'CH220',
    service: 'Polar Cooling',
    ratedPressureBar: 6,
  },
  'ferro-bl600': {
    kind: 'conveyor',
    model: 'Ferro BeltLine 600',
    firmwareFamily: 'ferro-plc-9',
    protocol: 'profinet',
    serial: 'BL600',
    service: 'Ferro Conveyors',
  },
  'thermon-bx2': {
    kind: 'boiler',
    model: 'Thermon BX-2',
    firmwareFamily: 'thermon-burner-2',
    protocol: 'opc-ua',
    serial: 'BX2',
    service: 'Thermon Heat',
    ratedPressureBar: 10,
  },
  'optic-vc4': {
    kind: 'camera',
    model: 'Optic VC-4K',
    firmwareFamily: 'optic-cam-7',
    protocol: 'rtsp',
    serial: 'VC4',
    service: 'Optic Security',
  },
}

/** What a device of a kind runs by default, its sample period and the range its pressure limits are tuned in. */
const kinds: Record<
  Kind,
  { capabilities: Capability[]; jobs: Job[]; group: number; samplePeriodMs?: number; pressure?: [number, number] }
> = {
  gateway: { capabilities: ['telemetry'], jobs: ['firmware-update'], group: 0 },
  compressor: {
    capabilities: ['control-loop', 'vibration-monitoring', 'energy-metering'],
    jobs: ['sample', 'calibrate'],
    group: 1,
    samplePeriodMs: 500,
    pressure: [7.6, 8.8],
  },
  pump: {
    capabilities: ['control-loop', 'energy-metering'],
    jobs: ['sample'],
    group: 2,
    samplePeriodMs: 1000,
    pressure: [4.2, 5.4],
  },
  chiller: {
    capabilities: ['control-loop', 'energy-metering'],
    jobs: ['meter-export'],
    group: 3,
    samplePeriodMs: 2000,
    pressure: [3.2, 4.4],
  },
  conveyor: { capabilities: ['vibration-monitoring', 'energy-metering'], jobs: ['spectrum-capture'], group: 4 },
  boiler: {
    capabilities: ['control-loop', 'maintenance', 'energy-metering'],
    jobs: ['sample', 'calibrate'],
    group: 5,
    samplePeriodMs: 250,
    pressure: [8.2, 9.2],
  },
  camera: { capabilities: ['video'], jobs: [], group: 8 },
}

type Lifecycle = Pick<Meta, 'startsIn' | 'expiresIn' | 'createdDaysAgo' | 'createdBy'>

interface Device {
  name: string
  model: Model
  title?: string
  description?: string
  labels?: string[]
  /** The capabilities it runs; those of its kind by default. */
  capabilities?: Capability[]
  /** Under a maintenance contract: adds the maintenance window and its calibration. */
  maintained?: boolean
  /** Its job runs; those of its kind under the capabilities it runs by default. */
  jobs?: Job[]
  /** Facts the documentation gives; generated from the device otherwise. */
  serial?: string
  installDate?: string
  /** The tuned pressure limits of its control loop, oldest first. */
  pressures?: number[]
  /** Watched closely, so it samples faster than its assignment says (device-fleet.md). */
  observed?: boolean
  /** The capability whose assignment is off while the device is stripped for service. */
  service?: Capability
  /** More Values and device settings, for the developer views. */
  values?: Content
  settings?: Content
  lifecycle?: Lifecycle
}

interface Site {
  name: string
  title: string
  description: string
  timeZone: string
  code: string
  owner: string
  alertChannel: string
  labels: string[]
  /** The second octet of the site's network. */
  octet: number
  currency: 'EUR' | 'GBP' | 'PLN' | 'SEK'
  window: string
  /** Production by default; the sandbox and the staging lab reach the services of their stage. */
  stage?: Stage
  /** The site's MQTT broker, oldest first; the TLS broker of the site (or of its stage) by default. */
  brokers?: string[]
  devices: Device[]
  lifecycle?: Lifecycle
  /** Deleted with everything under it; nothing of the site changed after. */
  deleted?: { daysAgo: number; by: PersonaId }
}

/** Two compressors and one gateway, exactly as the documentation and the board draw them. */
const harborPlant: Site = {
  name: 'harbor-plant',
  title: 'Harbor Plant',
  description: 'Compressed-air plant on the Rotterdam waterfront: two **Kestrel** compressors and the gateway.',
  timeZone: 'Europe/Amsterdam',
  code: 'NL-RTM',
  owner: 'Pieter de Vries',
  alertChannel: 'pagerduty:harbor-plant-shift',
  labels: ['port', 'benelux'],
  octet: 20,
  currency: 'EUR',
  window: 'sat 06:00-10:00',
  // The site broker moved to TLS in the summer.
  brokers: ['mqtt://10.20.0.5:1883', `mqtts://mqtt.harbor-plant.${domain}:8883`],
  devices: [
    {
      name: 'gateway',
      model: 'norvik-ng400',
      capabilities: ['telemetry'],
      jobs: [],
      serial: 'NG4-22-10931',
      installDate: '2022-05-11',
    },
    {
      name: 'compressor-1',
      model: 'kestrel-kx37',
      capabilities: ['control-loop', 'maintenance'],
      jobs: ['sample'],
      serial: 'KX37-19-04412',
      installDate: '2019-03-18',
      // Raised twice after the new receiver tank; the third version is the 8.4 bar it alerts at.
      pressures: [8.0, 8.2, 8.4],
      observed: true,
    },
    {
      name: 'compressor-2',
      model: 'kestrel-kx37',
      description: 'Five years older than compressor 1, so its control loop trips at a lower pressure.',
      capabilities: ['control-loop'],
      jobs: ['sample'],
      serial: 'KX37-14-01187',
      installDate: '2014-09-02',
      pressures: [7.8],
      service: 'control-loop',
    },
  ],
}

/** The second site of the documentation: the same capabilities on other hardware, with thresholds of its own. */
const westQuay: Site = {
  name: 'west-quay',
  title: 'West Quay',
  description: 'Container quay in Aberdeen. Runs the capabilities of Harbor Plant on **Brenner** screw compressors.',
  timeZone: 'Europe/London',
  code: 'GB-ABZ',
  owner: 'Fiona MacLeod',
  alertChannel: 'pagerduty:west-quay-shift',
  labels: ['port', 'uk'],
  octet: 21,
  currency: 'GBP',
  window: 'sun 07:00-11:00',
  lifecycle: { createdDaysAgo: 380, createdBy: 'radia' },
  devices: [
    { name: 'gateway', model: 'norvik-ng400' },
    { name: 'compressor-a', model: 'brenner-sc45', maintained: true, observed: true },
    { name: 'compressor-b', model: 'brenner-sc45', observed: true },
    { name: 'cold-store-chiller', model: 'polar-ch220' },
    { name: 'dewatering-pump', model: 'halden-hp80' },
    { name: 'quay-camera', model: 'optic-vc4' },
    {
      name: 'rental-chiller',
      model: 'polar-ch220',
      labels: ['rental'],
      description: 'Rented for the cold store; goes back when the second Polar unit is commissioned.',
      installDate: '2026-06-02',
      lifecycle: { expiresIn: 75, createdDaysAgo: 140, createdBy: 'ken' },
    },
  ],
}

const northDock: Site = {
  name: 'north-dock',
  title: 'North Dock',
  description: 'Bulk terminal in Gdańsk with the largest compressed-air network of the fleet.',
  timeZone: 'Europe/Warsaw',
  code: 'PL-GDN',
  owner: 'Katarzyna Nowak',
  alertChannel: 'teams:north-dock-control-room',
  labels: ['port', 'baltic'],
  octet: 22,
  currency: 'PLN',
  window: 'sat 22:00-02:00',
  lifecycle: { createdDaysAgo: 310, createdBy: 'radia' },
  devices: [
    { name: 'gateway', model: 'norvik-ng400' },
    { name: 'compressor-a', model: 'kestrel-kx55', maintained: true },
    { name: 'compressor-b', model: 'kestrel-kx55', maintained: true },
    { name: 'compressor-c', model: 'kestrel-kx37' },
    { name: 'cooling-pump', model: 'halden-hp80' },
    {
      name: 'fire-pump',
      model: 'halden-hp80',
      labels: ['safety'],
      description:
        'A fire pump runs until it is stopped by hand, so no control loop may trip it; only its energy is metered.',
      capabilities: ['energy-metering'],
    },
    { name: 'hall-chiller', model: 'polar-ch220' },
    { name: 'intake-conveyor', model: 'ferro-bl600' },
    { name: 'outfeed-conveyor', model: 'ferro-bl600' },
    { name: 'gate-camera', model: 'optic-vc4' },
    {
      name: 'yard-camera',
      model: 'optic-vc4',
      description: 'A spare since the gate camera covers the yard.',
      capabilities: [],
    },
  ],
}

const riverMill: Site = {
  name: 'river-mill',
  title: 'River Mill',
  description:
    'Grain mill on the Rhine in Duisburg; three **Thermon** boilers raise the steam of the conditioning line.',
  timeZone: 'Europe/Berlin',
  code: 'DE-DUI',
  owner: 'Jürgen Becker',
  alertChannel: 'teams:river-mill-control-room',
  labels: ['mill', 'rhine'],
  octet: 23,
  currency: 'EUR',
  window: 'sun 05:00-09:00',
  lifecycle: { createdDaysAgo: 260, createdBy: 'radia' },
  devices: [
    { name: 'gateway', model: 'norvik-ng400' },
    { name: 'steam-boiler-a', model: 'thermon-bx2' },
    {
      name: 'steam-boiler-b',
      model: 'thermon-bx2',
      description: 'Decommissioned after the boiler inspection; steam boiler C replaced it.',
      installDate: '2009-11-03',
      lifecycle: { expiresIn: -18 },
    },
    {
      name: 'steam-boiler-c',
      model: 'thermon-bx2',
      installDate: '2026-08-24',
      lifecycle: { createdDaysAgo: 55, createdBy: 'radia' },
    },
    { name: 'feed-pump', model: 'halden-hp80' },
    { name: 'condensate-pump', model: 'halden-hp80' },
    { name: 'compressor-a', model: 'kestrel-kx37', maintained: true },
    { name: 'compressor-b', model: 'kestrel-kx37' },
    { name: 'mill-conveyor', model: 'ferro-bl600' },
    { name: 'bagging-conveyor', model: 'ferro-bl600' },
    { name: 'process-chiller', model: 'polar-ch220' },
    { name: 'mill-camera', model: 'optic-vc4' },
  ],
}

/** The bottling line is installed and goes live in a few weeks. */
const bottlingLine: Lifecycle = { startsIn: 24, createdDaysAgo: 9, createdBy: 'ken' }

const eastYard: Site = {
  name: 'east-yard',
  title: 'East Yard',
  description: 'Beverage yard in Gothenburg. The new bottling line is installed and goes live next month.',
  timeZone: 'Europe/Stockholm',
  code: 'SE-GOT',
  owner: 'Elin Lindqvist',
  alertChannel: 'pagerduty:east-yard-shift',
  labels: ['yard', 'nordics'],
  octet: 24,
  currency: 'SEK',
  window: 'sat 06:00-10:00',
  lifecycle: { createdDaysAgo: 200, createdBy: 'ada' },
  devices: [
    { name: 'gateway', model: 'norvik-ng400' },
    { name: 'compressor-a', model: 'kestrel-kx55', maintained: true },
    { name: 'compressor-b', model: 'kestrel-kx55' },
    { name: 'cold-room-chiller', model: 'polar-ch220' },
    { name: 'transfer-pump', model: 'halden-hp80' },
    { name: 'gate-camera', model: 'optic-vc4' },
    ...(
      [
        ['bottling-compressor', 'kestrel-kx55'],
        ['bottling-chiller', 'polar-ch220'],
        ['bottling-conveyor', 'ferro-bl600'],
      ] as const
    ).map(([name, model]): Device => ({
      name,
      model,
      labels: ['bottling-line'],
      installDate: '2026-10-02',
      lifecycle: bottlingLine,
    })),
  ],
}

const sites = [harborPlant, westQuay, northDock, riverMill, eastYard]

/** The temporary extension of Harbor Plant during the refit of hall 2; deleted with its devices when it closed. */
const annex: Site = {
  name: 'harbor-plant-annex',
  title: 'Harbor Plant annex',
  description: 'Temporary extension of Harbor Plant for the refit of hall 2, with rented machines.',
  timeZone: 'Europe/Amsterdam',
  code: 'NL-RTM-ANX',
  owner: 'Pieter de Vries',
  alertChannel: 'pagerduty:harbor-plant-shift',
  labels: ['port', 'benelux', 'temporary'],
  octet: 25,
  currency: 'EUR',
  window: 'sat 06:00-10:00',
  lifecycle: { createdDaysAgo: 210, createdBy: 'radia' },
  deleted: { daysAgo: 24, by: 'radia' },
  devices: [
    { name: 'gateway', model: 'norvik-ng400', jobs: [] },
    {
      name: 'rental-compressor',
      model: 'kestrel-kx37',
      labels: ['rental'],
      capabilities: ['control-loop', 'energy-metering'],
      jobs: ['sample'],
    },
    { name: 'temporary-chiller', model: 'polar-ch220', labels: ['rental'], capabilities: ['energy-metering'] },
  ],
}

/** The development view of the board: one test rig at Harbor Plant replaces the devices, on sandbox endpoints. */
const developmentSite: Site = {
  ...harborPlant,
  alertChannel: 'teams:plant-fleet-dev',
  stage: 'development',
  // The broker on the bench, next to the rig.
  brokers: ['mqtt://localhost:1883'],
  devices: [
    {
      name: 'test-rig',
      model: 'kestrel-kx37',
      title: 'Test rig',
      description: 'A Kestrel controller on the bench of the controls lab, with a pressure simulator for a compressor.',
      labels: ['test'],
      capabilities: ['telemetry', 'control-loop'],
      jobs: ['sample'],
      serial: 'KX37-24-90007',
      installDate: '2024-02-12',
      pressures: [6.0],
      observed: true,
      values: { bench: 'controls-lab-2', simulator: 'pressure-sim 1.4' },
      settings: { address: '127.0.0.1:5020' },
    },
  ],
}

/** Staging: the hardware-in-the-loop lab where every release runs before it reaches a site. */
const integrationLab: Site = {
  name: 'integration-lab',
  title: 'Integration lab',
  description: 'Hardware-in-the-loop benches in Rotterdam; simulators stand in for the machines a bench cannot hold.',
  timeZone: 'Europe/Amsterdam',
  code: 'NL-RTM-LAB',
  owner: 'Ken Thompson',
  alertChannel: 'teams:plant-fleet-staging',
  labels: ['lab'],
  octet: 90,
  currency: 'EUR',
  window: 'mon 08:00-12:00',
  stage: 'staging',
  lifecycle: { createdDaysAgo: 150, createdBy: 'ken' },
  devices: [
    { name: 'gateway', model: 'norvik-ng400' },
    {
      name: 'hil-compressor',
      model: 'kestrel-kx37',
      title: 'HIL compressor',
      labels: ['hil'],
      maintained: true,
      observed: true,
    },
    { name: 'sim-compressor', model: 'brenner-sc45', title: 'Simulated compressor', labels: ['simulator'] },
    { name: 'sim-pump', model: 'halden-hp80', title: 'Simulated pump', labels: ['simulator'] },
    { name: 'sim-boiler', model: 'thermon-bx2', title: 'Simulated boiler', labels: ['simulator'] },
    { name: 'sim-conveyor', model: 'ferro-bl600', title: 'Simulated conveyor', labels: ['simulator'] },
    { name: 'lab-chiller', model: 'polar-ch220' },
    { name: 'lab-camera', model: 'optic-vc4' },
  ],
}

const pad = (value: number, length = 2) => String(value).padStart(length, '0')
const round = (value: number, digits = 2) => Math.round(value * 10 ** digits) / 10 ** digits
/** "compressor-a" → "Compressor A", "steam-boiler-b" → "Steam boiler B". */
const titleOf = (name: string) =>
  name
    .split('-')
    .map((word, index) => (word.length === 1 || index === 0 ? word[0]!.toUpperCase() + word.slice(1) : word))
    .join(' ')
/** "Compressor 1" and "Control loop" → "Compressor 1 control loop": the grid lists a device's annotations together. */
const titled = (device: string, what: string) => `${device} ${what[0]!.toLowerCase()}${what.slice(1)}`
/** A host on the site's own network in production; the shared instance of its stage otherwise. */
const siteHost = ({ name, stage = 'production' }: Site, service: string) =>
  stage === 'production' ? `${service}.${name}.${domain}` : host(stage, service)

/** Nothing is edited after it is deleted or expires: the fewest days since a document of the site or device changed. */
function settledDaysAgo(site: Site, lifecycle: Lifecycle) {
  const expired = lifecycle.expiresIn !== undefined && lifecycle.expiresIn < 0 ? 1 - lifecycle.expiresIn : 1
  return Math.max(site.deleted ? site.deleted.daysAgo + 1 : 1, expired)
}

interface Planned {
  device: Device
  hardware: Hardware
  index: number
  title: string
  address: string
  capabilities: Capability[]
  jobs: Job[]
  lifecycle: Lifecycle
  serial: string
  installDate: string
  pressures: number[]
  /** Days since the device's documents last changed. */
  age: number
  subMeter: boolean
  ctRatio: string
  gauge: string
}

/**
 * What one device runs in this view, and its facts. Capabilities and jobs outside the view's catalog are left out,
 * so the small views reuse the site tables. The facts are seeded by the device, not by the view, so a device keeps
 * its serial number and its limits in every view.
 */
function plan(b: ScopeBuilder, site: Site, device: Device, index: number): Planned {
  const model = hardware[device.model]
  const kind = kinds[model.kind]
  const runs = [...(device.capabilities ?? kind.capabilities), ...(device.maintained ? ['maintenance' as const] : [])]
  const capabilitiesOfView = runs.filter((capability) => b.has(`rst.${capability}`))
  const rng = seededRng(`plant-fleet/${site.name}/${device.name}`)
  const year = device.installDate ? Number(device.installDate.slice(0, 4)) : rng.int(2012, 2025)
  const [low, high] = kind.pressure ?? [0, 0]
  const lifecycle = { ...site.lifecycle, ...device.lifecycle }
  return {
    device,
    hardware: model,
    index,
    title: device.title ?? titleOf(device.name),
    address: `10.${site.octet}.${kind.group}.${11 + index}`,
    capabilities: capabilitiesOfView,
    jobs: (device.jobs ?? kind.jobs).filter(
      (job) => capabilitiesOfView.includes(jobs[job].capability) && b.has(`unt.${jobs[job].capability}.${job}`),
    ),
    lifecycle,
    installDate: device.installDate ?? `${year}-${pad(rng.int(1, 12))}-${pad(rng.int(1, 28))}`,
    serial: device.serial ?? `${model.serial}-${pad(year % 100)}-${pad(rng.int(1, 99999), 5)}`,
    pressures: device.pressures ?? [round(low + rng.next() * (high - low), 1)],
    age: Math.max(Math.min(rng.int(2, 25), (lifecycle.createdDaysAgo ?? 400) - 1), settledDaysAgo(site, lifecycle)),
    subMeter: rng.chance(0.45),
    ctRatio: rng.pick(['200/5', '400/5', '800/5']),
    gauge: `REF-PG-${rng.int(1000, 9999)}`,
  }
}

/** The capabilities and jobs of the view: the responsibilities and units every site enables and assigns. */
function addCatalog(b: ScopeBuilder, names: readonly Capability[], units: readonly Job[]) {
  for (const capability of capabilities.filter((candidate) => names.includes(candidate.name))) {
    b.responsibility(capability.name, {
      title: capability.title,
      description: capability.description,
      labels: capability.labels,
      link: `${docs}/capabilities/${capability.name}`,
    })
  }
  for (const job of units) {
    b.unit(jobs[job].capability, job, jobs[job].meta)
  }
}

/** The safe defaults shipped with each capability and job, and the defaults every assignment inherits. */
function capabilityDocuments(b: ScopeBuilder, variant: Variant, stage: Stage = 'production') {
  const next = variant === 'release-candidate'
  const configure = (key: string, versions: Content | Content[], options?: HistoryOptions) => {
    if (b.has(key)) {
      b.configure(key, versions, options)
    }
  }

  configure(
    'rst.telemetry',
    evolve(
      {
        upstream: `mqtts://${host(stage, 'mqtt')}:8883`,
        topicPrefix: 'harbor-industrial/sites',
        publishIntervalSeconds: 30,
      },
      4,
      (version) =>
        version === 2 ? { publishIntervalSeconds: 15 } : version === 3 ? { qos: 1 } : { publishIntervalSeconds: 10 },
    ),
    { every: 30 },
  )
  // The control loop's safe defaults, tuned on the plant floor over a quarter: the long history of the project.
  const controlLoop = evolve(
    {
      samplePeriodMs: 1000,
      pressureMaxBar: 7.0,
      deadbandBar: 0.3,
      pid: { kp: 1.0, ki: 0.25, kd: 0 },
      onTrip: 'unload',
    },
    16,
    (version) =>
      version === 6
        ? { deadbandBar: 0.2 }
        : version === 10
          ? { pid: { kd: 0.05 } }
          : version === 14
            ? { onTrip: 'unload-and-alert' }
            : { pid: { kp: round(1 + version * 0.04) } },
  )
  if (next) {
    controlLoop.push({ ...controlLoop.at(-1)!, antiWindup: true })
  }
  configure('rst.control-loop', controlLoop, { every: 6, lastDaysAgo: next ? 1 : 2 })
  configure('rst.maintenance', {
    windowHours: 4,
    notifyBeforeHours: 48,
    lockoutTagout: true,
    cmms: `https://${host(stage, 'cmms')}`,
  })
  // The defaults are the zone limits of the compressors: machine group 2 on flexible (isolated) mounts. The second
  // version moved to the successor standard and records a routine spectrum.
  configure(
    'rst.vibration-monitoring',
    [
      {
        standard: 'ISO 10816-3',
        machineGroup: 2,
        foundation: 'flexible',
        rmsAlarmMmS: 4.5,
        rmsTripMmS: 7.1,
        sampleRateHz: 12800,
      },
      {
        standard: 'ISO 20816-3',
        machineGroup: 2,
        foundation: 'flexible',
        rmsAlarmMmS: 4.5,
        rmsTripMmS: 7.1,
        sampleRateHz: 25600,
        spectrumLines: 3200,
      },
    ],
    { every: 45 },
  )
  configure('rst.energy-metering', {
    intervalMinutes: 15,
    unit: 'kWh',
    historian: `https://${host(stage, 'historian')}/energy`,
  })
  configure('rst.video', { codec: 'h265', bitrateKbps: 4096, retentionDays: 30, privacyMasking: true })

  for (const job of jobNames) {
    const { capability, content } = jobs[job]
    // The release lets twice as many gateways update at once.
    configure(
      `unt.${capability}.${job}`,
      next && job === 'firmware-update' ? [content(stage), { ...content(stage), maxParallel: 4 }] : content(stage),
    )
  }

  // The safe defaults of every assignment: what a device does when it loses its controller.
  const template = evolve(
    { heartbeatSeconds: 30, alarmDelaySeconds: 5, onCommunicationLoss: 'hold-safe-state', logLevel: 'info' },
    next ? 4 : 3,
    (version) =>
      version === 2 ? { alarmDelaySeconds: 3 } : version === 3 ? { logLevel: 'warn' } : { watchdogSeconds: 10 },
  )
  if (stage === 'development') {
    // The developers read every message of the test rig.
    template.push({ ...template.at(-1)!, logLevel: 'debug' })
  }
  b.template('exe', template, { every: 40 })
}

/**
 * A control-loop assignment cannot be saved without its pressure limit, nor with one the pressure vessels are not
 * rated for (device-fleet.md); a device needs an address and a protocol the gateways speak.
 */
function schemaDocuments(b: ScopeBuilder, variant: Variant) {
  const pressureMaxBar = {
    type: 'number',
    exclusiveMinimum: 0,
    maximum: 10,
    description: 'The trip pressure of this machine; never above the 10 bar of the highest-rated vessels in the fleet.',
  }
  const samplePeriodMs = { type: 'integer', minimum: 50, maximum: 60000 }
  b.descendantSchema('rst.control-loop', 'exe', [
    { type: 'object', properties: { pressureMaxBar, samplePeriodMs }, required: ['pressureMaxBar'] },
    // The next release keeps a margin below the rating and makes every control loop name its alert channel.
    ...(variant === 'release-candidate'
      ? [
          {
            type: 'object',
            properties: {
              pressureMaxBar: {
                ...pressureMaxBar,
                maximum: 9.5,
                description:
                  'The trip pressure of this machine; never above 9.5 bar, a margin below the 10 bar rating.',
              },
              samplePeriodMs,
              alertChannel: { type: 'string', minLength: 1 },
            },
            required: ['pressureMaxBar', 'alertChannel'],
          },
        ]
      : []),
  ])
  // The Brenner compressors of West Quay are rated lower than the Kestrels, so the site lowers the cap of its loops.
  // It requires nothing, so the required list of the capability's schema stays in the merge.
  if (b.has('sbt.west-quay')) {
    b.descendantSchema('sbt.west-quay', 'exe', {
      type: 'object',
      properties: {
        pressureMaxBar: {
          maximum: 9.5,
          description: 'The trip pressure of this machine; never above the 9.5 bar of the Brenner SC-45 vessels.',
        },
      },
    })
  }
  const address = { type: 'string', minLength: 1 }
  b.typeSchema(
    'cnt',
    [
      { type: 'object', properties: { address }, required: ['address'] },
      {
        type: 'object',
        properties: {
          address,
          protocol: { enum: [...protocols] },
          ratedPressureBar: { type: 'number', exclusiveMinimum: 0, maximum: 16 },
        },
        required: ['address', 'protocol'],
      },
    ],
    { every: 90 },
  )
}

/** The contract-level facts of a capability at one site; the control loop and vibration monitoring need none. */
function enablement(site: Site, capability: Capability): Content | undefined {
  const stage = site.stage ?? 'production'
  switch (capability) {
    case 'telemetry':
      return {
        historian: `https://${host(stage, 'historian')}/sites/${site.name}`,
        retentionDays: retentionDays[stage],
      }
    case 'maintenance':
      return { contract: `SVC-${site.code}-2026`, responseHours: 4 }
    case 'energy-metering':
      return { tariff: 'time-of-use', currency: site.currency }
    case 'video':
      return { nvr: siteHost(site, 'nvr'), retentionDays: 30 }
    case 'control-loop':
    case 'vibration-monitoring':
      return undefined
  }
}

/** What one device is told to do for one capability; undefined where the capability's defaults suffice. */
function assignment(site: Site, device: Planned, capability: Capability): Content | Content[] | undefined {
  switch (capability) {
    case 'telemetry':
      return {
        clientId: `${site.name}-${device.device.name}`,
        broker: '@config("/network/mqttBroker")',
        password: `@(${site.name}.mqtt-${device.device.name}, primaryVault)`,
        alertChannel,
      }
    case 'control-loop':
      // The limit is local to the machine: two compressors of one site are not the same age.
      return device.pressures.map((pressureMaxBar) => ({
        samplePeriodMs: kinds[device.hardware.kind].samplePeriodMs ?? 1000,
        pressureMaxBar,
        alertChannel,
      }))
    case 'maintenance':
      return {
        window: site.window,
        vendor: device.hardware.service,
        lockout: `LOTO-${site.code}-${pad(device.index + 1)}`,
      }
    case 'vibration-monitoring':
      // Conveyors stand on rigid foundations, so their zone limits are lower than the defaults of the compressors.
      return device.hardware.kind === 'conveyor'
        ? { foundation: 'rigid', rmsAlarmMmS: 2.8, rmsTripMmS: 4.5 }
        : undefined
    case 'energy-metering':
      return device.subMeter
        ? { meterId: `EM-${site.code}-${pad(device.index + 1, 3)}`, ctRatio: device.ctRatio }
        : undefined
    case 'video':
      return {
        streamUrl: `rtsp://${device.address}:554/stream1`,
        retentionDays: 30,
        viewer: `@(${site.name}.nvr-viewer, primaryVault)`,
      }
  }
}

function jobRun(device: Planned, job: Job, variant: Variant): Content | Content[] | undefined {
  switch (job) {
    case 'sample':
      // A faster sample while the machine is observed; the diagnostic week in March sampled faster still.
      return device.device.observed ? { samplePeriodMs: variant === 'campaign-march' ? 100 : 250 } : undefined
    case 'firmware-update':
      // The gateways run 4.2.1 since the last update; the next release brings 4.3.0.
      return ['4.1.3', '4.2.1', ...(variant === 'release-candidate' ? ['4.3.0'] : [])].map((targetVersion) => ({
        family: firmwareFamily,
        targetVersion,
      }))
    case 'calibrate':
      return { referenceGauge: device.gauge, certificate: true }
    case 'spectrum-capture':
    case 'meter-export':
      return undefined
  }
}

function addDevice(b: ScopeBuilder, site: Site, device: Planned, variant: Variant) {
  const { name } = device.device
  const meta = { timeZone: site.timeZone, ...device.lifecycle }
  const history = { every: 12, lastDaysAgo: device.age }
  b.context(site.name, name, {
    title: device.title,
    description: device.device.description,
    labels: [device.hardware.kind, ...(device.device.observed ? ['observed'] : []), ...(device.device.labels ?? [])],
    values: {
      model: device.hardware.model,
      serial: device.serial,
      installDate: device.installDate,
      firmwareFamily: device.hardware.firmwareFamily,
      ...device.device.values,
    },
    ...meta,
  })
  b.configure(
    `cnt.${site.name}.${name}`,
    {
      address: device.address,
      protocol: device.hardware.protocol,
      ...(device.hardware.ratedPressureBar ? { ratedPressureBar: device.hardware.ratedPressureBar } : {}),
      ...(device.hardware.kind === 'camera' ? { resolution: '2560x1440', fps: 15 } : {}),
      ...device.device.settings,
    },
    history,
  )

  for (const capability of device.capabilities) {
    // Stripped for service: the device stays in the catalog and the history shows when it was taken out.
    const serviced = device.device.service === capability
    const key = b.execution(site.name, capability, name, {
      title: titled(device.title, capabilityTitle(capability)),
      ...meta,
      ...(serviced
        ? {
            disabled: true,
            description: `Off while ${device.title.toLowerCase()} is stripped for service; the other machines keep running.`,
            modifiedDaysAgo: 3,
            modifiedBy: 'ken' as const,
          }
        : {}),
    })
    const content = assignment(site, device, capability)
    if (content) {
      b.configure(key, content, history)
    }

    for (const job of device.jobs.filter((candidate) => jobs[candidate].capability === capability)) {
      const run = b.unitOfExecution(site.name, capability, name, job, {
        title: titled(device.title, jobs[job].meta.title!),
        ...meta,
      })
      const runContent = jobRun(device, job, variant)
      if (runContent) {
        b.configure(run, runContent, { every: 20, lastDaysAgo: device.age })
      }
    }
  }
}

/** A site, the capabilities its devices run, the devices and their assignments and job runs. */
function addSite(b: ScopeBuilder, site: Site, variant: Variant) {
  const meta = { timeZone: site.timeZone, ...site.lifecycle }
  const settled = settledDaysAgo(site, site.lifecycle ?? {})
  b.subject(site.name, {
    title: site.title,
    description: site.description,
    link: `${docs}/sites/${site.name}`,
    labels: site.labels,
    values: { siteCode: site.code, owner: site.owner, alertChannel: site.alertChannel },
    ...meta,
  })
  b.configure(
    `sbt.${site.name}`,
    (site.brokers ?? [`mqtts://${siteHost(site, 'mqtt')}:8883`]).map((mqttBroker) => ({
      timeZone: site.timeZone,
      network: { subnet: `10.${site.octet}.0.0/16`, mqttBroker, ntp: siteHost(site, 'ntp') },
    })),
    { every: 60, lastDaysAgo: settled },
  )

  const devices = site.devices.map((device, index) => plan(b, site, device, index))
  for (const capability of capabilities) {
    if (devices.some((device) => device.capabilities.includes(capability.name))) {
      const key = b.usage(site.name, capability.name, { title: `${capability.title} at ${site.title}`, ...meta })
      const content = enablement(site, capability.name)
      if (content) {
        b.configure(key, content, { lastDaysAgo: settled })
      }
    }
  }
  devices.forEach((device) => addDevice(b, site, device, variant))

  if (site.deleted) {
    b.trash(`sbt.${site.name}`, site.deleted)
  }
}

/** The next release brings ultrasonic leak detection to the compressed-air networks of Harbor Plant and North Dock. */
function leakDetection(b: ScopeBuilder) {
  const nextRelease: Meta = { createdDaysAgo: 12, createdBy: 'ken' }
  b.responsibility('leak-detection', {
    title: 'Leak detection',
    labels: ['energy'],
    description: 'Ultrasonic leak detection on the compressed-air network; a 3 mm leak at 7 bar wastes about 3.5 kW.',
    link: `${docs}/capabilities/leak-detection`,
    ...nextRelease,
  })
  b.configure('rst.leak-detection', { sensor: 'ultrasonic', thresholdDb: 38, surveyEveryDays: 30 })
  for (const [site, devices] of [
    [harborPlant, ['compressor-1']],
    [northDock, ['compressor-a', 'compressor-b', 'compressor-c']],
  ] as const) {
    b.usage(site.name, 'leak-detection', {
      title: `Leak detection at ${site.title}`,
      timeZone: site.timeZone,
      ...nextRelease,
    })
    b.configure(`usg.${site.name}.leak-detection`, { surveyEveryDays: 30, alertChannel })
    for (const device of devices) {
      b.execution(site.name, 'leak-detection', device, {
        title: titled(titleOf(device), 'Leak detection'),
        timeZone: site.timeZone,
        ...nextRelease,
      })
    }
  }
}

/** The gate camera covers the yard now, so the yard camera's video assignment was removed alone. */
function removedYardCamera(b: ScopeBuilder) {
  const removedDaysAgo = 6
  const index = northDock.devices.findIndex((device) => device.name === 'yard-camera')
  const camera = plan(b, northDock, northDock.devices[index]!, index)
  const key = b.execution(northDock.name, 'video', camera.device.name, {
    title: titled(camera.title, capabilityTitle('video')),
    timeZone: northDock.timeZone,
    ...camera.lifecycle,
  })
  b.configure(key, assignment(northDock, camera, 'video')!, { lastDaysAgo: removedDaysAgo + 2 })
  b.trash(key, { daysAgo: removedDaysAgo, by: 'ken' })
}

/** Production and its copies: every site, plus the annex and the camera assignment deleted in production. */
function production(b: ScopeBuilder, variant: Variant) {
  addCatalog(b, capabilityNames, jobNames)
  capabilityDocuments(b, variant)
  sites.forEach((site) => addSite(b, site, variant))
  schemaDocuments(b, variant)

  if (variant === 'release-candidate') {
    leakDetection(b)
  }
  if (variant === 'default') {
    // The annex closed when the refit of hall 2 ended; its devices, enablements and assignments went with it.
    addSite(b, annex, variant)
    removedYardCamera(b)
  }
}

/** The board's development view: telemetry and the control loop on one test rig, on sandbox endpoints. */
function development(b: ScopeBuilder) {
  addCatalog(b, ['telemetry', 'control-loop'], ['sample'])
  capabilityDocuments(b, 'default', 'development')
  addSite(b, developmentSite, 'default')
  schemaDocuments(b, 'default')
}

/** Staging: every capability on the benches and simulators of the integration lab, on the staging services. */
function staging(b: ScopeBuilder) {
  addCatalog(b, capabilityNames, jobNames)
  capabilityDocuments(b, 'default', 'staging')
  addSite(b, integrationLab, 'default')
  schemaDocuments(b, 'default')
}

/** Predictive maintenance, delivered to the two compressors of Harbor Plant first. */
function featureDelivery(b: ScopeBuilder) {
  addCatalog(b, ['telemetry', 'control-loop'], ['sample'])
  capabilityDocuments(b, 'default')
  addSite(b, harborPlant, 'default')
  schemaDocuments(b, 'default')

  const feature: Meta = { createdDaysAgo: 18, createdBy: 'radia', timeZone: harborPlant.timeZone }
  b.responsibility('predictive-maintenance', {
    title: 'Predictive maintenance',
    labels: ['feature', 'ml'],
    description:
      'Scores the valve and bearing wear of a compressor from its pressure, temperature and motor-current samples.',
    link: `${docs}/capabilities/predictive-maintenance`,
    ...feature,
  })
  b.configure('rst.predictive-maintenance', {
    model: 'compressor-wear-v3',
    scoreThreshold: 0.82,
    features: ['dischargePressure', 'dischargeTemperature', 'motorCurrent', 'loadCycles'],
  })
  b.unit('predictive-maintenance', 'anomaly-scoring', {
    title: 'Anomaly scoring',
    unitType: 'time',
    definition: '*/15 * * * *',
    description: 'Scores the last day of samples every fifteen minutes.',
    ...feature,
  })
  b.configure('unt.predictive-maintenance.anomaly-scoring', { windowHours: 24, minSamples: 500 })
  b.usage('harbor-plant', 'predictive-maintenance', { title: 'Predictive maintenance at Harbor Plant', ...feature })
  b.configure('usg.harbor-plant.predictive-maintenance', { pilot: true, alertChannel })
  // Compressor 2 is stripped for service, so its scoring waits until the machine runs again.
  for (const [device, baselineDays, serviced] of [
    ['compressor-1', 30, false],
    ['compressor-2', 14, true],
  ] as const) {
    const title = titleOf(device)
    b.execution('harbor-plant', 'predictive-maintenance', device, {
      title: titled(title, 'Predictive maintenance'),
      ...feature,
      ...(serviced
        ? { disabled: true, description: 'Scores again once the service is done; the baseline starts over then.' }
        : {}),
    })
    b.configure(`exe.harbor-plant.predictive-maintenance.${device}`, { baselineDays, alertChannel })
    b.unitOfExecution('harbor-plant', 'predictive-maintenance', device, 'anomaly-scoring', {
      title: titled(title, 'Anomaly scoring'),
      ...feature,
    })
  }
}

export const plantFleet: ShowcaseProject = {
  org: 'harbor-industrial',
  project: 'plant-fleet',
  summary: 'The device fleet of the documentation: five sites, their devices and the capabilities each device runs.',
  members: { radia: 'Owner', ada: 'ContributorWithSecrets', ken: 'Contributor', margaret: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Capability', color: 'violet', icon: 'i-lucide-activity' },
      unt: { label: 'Job', color: 'fuchsia', icon: 'i-lucide-clock' },
      sbt: { label: 'Site', color: 'blue', icon: 'i-lucide-factory' },
      usg: { label: 'Enablement', color: 'amber', icon: 'i-lucide-key' },
      cnt: { label: 'Device', color: 'teal', icon: 'i-lucide-cpu' },
      exe: { label: 'Assignment', color: 'emerald', icon: 'i-lucide-gauge' },
      uxe: { label: 'JobRun', color: 'orange', icon: 'i-lucide-workflow' },
    },
    defaultTimeZone: 'Europe/Amsterdam',
    dateFormat: 'yyyy-mm-dd',
  },
  views: environmentViews(
    {
      development: 'Development: the test rig at Harbor Plant',
      staging: 'Staging: the hardware-in-the-loop integration lab',
      'release-candidate': 'Release 2026.11: leak detection, gateway firmware 4.3 and a lower pressure cap',
      'feature-delivery1': 'Predictive maintenance on the Harbor Plant compressors',
    },
    [{ name: 'campaign-march', description: 'Diagnostic week: 100 ms sampling on the observed compressors' }],
  ),
  build: (b) => {
    switch (b.view) {
      case 'default':
      case 'release-candidate':
      case 'campaign-march':
        return production(b, b.view as Variant)
      case 'development':
        return development(b)
      case 'staging':
        return staging(b)
      case 'feature-delivery1':
        return featureDelivery(b)
    }
  },
}
