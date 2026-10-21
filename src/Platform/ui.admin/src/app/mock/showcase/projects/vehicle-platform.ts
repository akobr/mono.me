import type { Meta, ScopeBuilder } from '../builder'
import { evolve, type HistoryOptions } from '../history'
import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

// The physical product of the documentation (docs/42for.net/platform/examples/physical-product.md), example 7 of the
// Miro board. The model is the subject; a trim or one car (its VIN) is the context; a subsystem is the responsibility
// and a firmware component on it the unit. The Aurora is sold as Touring and GT, and VIN 4F2, a GT, had a battery
// module replaced after delivery. The SUV Borealis, the compact Zephyr, five more subsystems and the cars repaired in
// two recalls make it a catalog of a couple of hundred.

const docs = 'https://docs.aurora-motors.example/vehicle-platform'
const artifacts = 'https://artifacts.aurora-motors.example/ota'
const ota = {
  production: 'https://ota.aurora-motors.example',
  staging: 'https://ota.staging.aurora-motors.example',
  development: 'https://ota.sandbox.aurora-motors.example',
}

type Doc = Record<string, unknown>
type Versions = Doc | Doc[]
type Channel = 'stable' | 'beta' | 'nightly'
type SubsystemName =
  'battery-pack' | 'infotainment' | 'adas' | 'brakes' | 'thermal' | 'charging' | 'suspension' | 'lighting' | 'seats'

const channels: Channel[] = ['stable', 'beta', 'nightly']
/** The cell chemistries the factory can build, and the cell suppliers it buys from. */
const chemistries = ['lfp', 'nmc', 'nmca']
const suppliers = ['helios', 'nordcell', 'voltaris']

interface Subsystem {
  name: SubsystemName
  title: string
  labels: string[]
  owner: string
  description: string
}

const subsystems: Subsystem[] = [
  {
    name: 'battery-pack',
    title: 'Battery pack',
    labels: ['powertrain', 'high-voltage'],
    owner: 'battery-systems@aurora-motors.example',
    description:
      'The traction battery: cells, modules and the battery management system (BMS). A fitted pack must name a ' +
      'chemistry the factory can build and its capacity.',
  },
  {
    name: 'infotainment',
    title: 'Infotainment',
    labels: ['cockpit', 'connected'],
    owner: 'cockpit@aurora-motors.example',
    description: 'Head unit, displays, audio and the over-the-air (OTA) client.',
  },
  {
    name: 'adas',
    title: 'ADAS',
    labels: ['safety', 'ml'],
    owner: 'adas@aurora-motors.example',
    description: 'Advanced driver assistance: cameras, radar, lidar where fitted, and the perception model.',
  },
  {
    name: 'brakes',
    title: 'Brakes',
    labels: ['chassis', 'safety'],
    owner: 'chassis@aurora-motors.example',
    description:
      'The one-box brake-by-wire unit. It is the same on every model, so it is configured here and **no model has a ' +
      'fitment of its own**.',
  },
  {
    name: 'thermal',
    title: 'Thermal management',
    labels: ['powertrain'],
    owner: 'powertrain@aurora-motors.example',
    description: 'Heat pump, coolant loops and battery preconditioning.',
  },
  {
    name: 'charging',
    title: 'Charging',
    labels: ['powertrain', 'high-voltage'],
    owner: 'powertrain@aurora-motors.example',
    description: 'On-board charger (OBC), charge port and Plug & Charge contracts.',
  },
  {
    name: 'suspension',
    title: 'Suspension',
    labels: ['chassis'],
    owner: 'chassis@aurora-motors.example',
    description: 'Springs, dampers and ride-height control.',
  },
  {
    name: 'lighting',
    title: 'Lighting',
    labels: ['body'],
    owner: 'body@aurora-motors.example',
    description: 'Headlights, signal lights and the adaptive driving beam.',
  },
  {
    name: 'seats',
    title: 'Seats',
    labels: ['interior'],
    owner: 'interior@aurora-motors.example',
    description: 'Seat frames, heating, ventilation and memory.',
  },
]

const titleOf = (subsystem: SubsystemName) => subsystems.find((candidate) => candidate.name === subsystem)!.title

type FirmwareId =
  | 'infotainment.firmware'
  | 'infotainment.map-data'
  | 'charging.firmware'
  | 'adas.perception-model'
  | 'battery-pack.bms-firmware'

interface Firmware {
  subsystem: SubsystemName
  name: string
  title: string
  unitType: 'file' | 'time'
  /** The artifact a file unit installs, or the CRON of a time unit (in the car's time zone). */
  definition: string
  ecu: string
  description: string
}

const firmware: Firmware[] = [
  {
    subsystem: 'infotainment',
    name: 'firmware',
    title: 'Infotainment firmware',
    unitType: 'file',
    definition: `${artifacts}/infotainment/ivi-image.swu`,
    ecu: 'IVI-G3',
    description: 'The signed head-unit image, installed over the air.',
  },
  {
    subsystem: 'infotainment',
    name: 'map-data',
    title: 'Navigation map data',
    unitType: 'time',
    definition: '0 3 1 * *',
    ecu: 'IVI-G3',
    description: 'The map package of the region, fetched at 03:00 on the first of the month while the car is parked.',
  },
  {
    subsystem: 'charging',
    name: 'firmware',
    title: 'On-board charger firmware',
    unitType: 'file',
    definition: `${artifacts}/charging/obc-firmware.bin`,
    ecu: 'OBC',
    description: 'The firmware of the on-board charger; the obc-11-b unit of recall AM-R-2026-07 needs line 4.',
  },
  {
    subsystem: 'adas',
    name: 'perception-model',
    title: 'Perception model',
    unitType: 'file',
    definition: `${artifacts}/adas/perception-model.onnx`,
    ecu: 'ADAS-DCU',
    description:
      'The network that detects lanes, vehicles and people; it runs in shadow mode on the canary cars first.',
  },
  {
    subsystem: 'battery-pack',
    name: 'bms-firmware',
    title: 'Battery management firmware',
    unitType: 'file',
    definition: `${artifacts}/battery-pack/bms-firmware.hex`,
    ecu: 'BMS',
    description: 'Cell balancing, state of charge and the thermal limits of the pack.',
  },
]

const idOf = (unit: Firmware) => `${unit.subsystem}.${unit.name}` as FirmwareId
const unitOf = (id: FirmwareId) => firmware.find((unit) => idOf(unit) === id)!

type Plant = 'torslanda' | 'debrecen' | 'ridgeville'
const plants: Record<Plant, { title: string; code: string; timeZone: string }> = {
  torslanda: { title: 'Torslanda, Gothenburg', code: 'G', timeZone: 'Europe/Stockholm' },
  debrecen: { title: 'Debrecen', code: 'D', timeZone: 'Europe/Budapest' },
  ridgeville: { title: 'Ridgeville, South Carolina', code: 'R', timeZone: 'America/New_York' },
}

type Market = 'se' | 'no' | 'dk' | 'de' | 'nl' | 'ch' | 'uk' | 'us'
/** Where a car is registered: its time zone and the workshop that services it. */
const markets: Record<Market, { timeZone: string; workshop: string }> = {
  se: { timeZone: 'Europe/Stockholm', workshop: 'aurora-service-gothenburg' },
  no: { timeZone: 'Europe/Oslo', workshop: 'aurora-service-oslo' },
  dk: { timeZone: 'Europe/Copenhagen', workshop: 'aurora-service-copenhagen' },
  de: { timeZone: 'Europe/Berlin', workshop: 'aurora-service-munich' },
  nl: { timeZone: 'Europe/Amsterdam', workshop: 'aurora-service-amsterdam' },
  ch: { timeZone: 'Europe/Zurich', workshop: 'aurora-service-zurich' },
  uk: { timeZone: 'Europe/London', workshop: 'aurora-service-london' },
  us: { timeZone: 'America/New_York', workshop: 'aurora-service-charlotte' },
}

/** Configures the key when the view has it, so views with smaller catalogs share the documents. */
function configureIfPresent(b: ScopeBuilder, key: string, versions: Versions, options?: HistoryOptions) {
  if (b.has(key)) {
    b.configure(key, versions, options)
  }
}

function addSubsystems(b: ScopeBuilder, names: SubsystemName[] = subsystems.map((subsystem) => subsystem.name)) {
  for (const subsystem of subsystems.filter((candidate) => names.includes(candidate.name))) {
    b.responsibility(subsystem.name, {
      title: subsystem.title,
      description: subsystem.description,
      labels: subsystem.labels,
      values: { owner: subsystem.owner },
      link: `${docs}/subsystems/${subsystem.name}`,
      createdDaysAgo: 720,
    })
  }
}

/** The battery schema of the documentation; the release candidate also requires an approved cell supplier. */
function packSchemas(next: boolean): Doc[] {
  const schema = {
    type: 'object',
    properties: {
      chemistry: { enum: chemistries },
      capacityKwh: { type: 'number', minimum: 40, maximum: 150 },
      homologation: { type: 'array', items: { enum: ['eu', 'uk', 'no', 'ch', 'us', 'ca'] } },
    },
    required: ['chemistry', 'capacityKwh'],
  }
  if (!next) {
    return [schema]
  }
  return [
    schema,
    {
      ...schema,
      properties: { ...schema.properties, supplier: { enum: suppliers } },
      required: ['chemistry', 'capacityKwh', 'supplier'],
    },
  ]
}

function subsystemDocuments(b: ScopeBuilder, server: string, next: boolean) {
  // The part family: the chemistries the factory can build and the warranty of every pack.
  const warranty = { years: 8, km: 160000, minimumStateOfHealthPercent: 70 }
  configureIfPresent(
    b,
    'rst.battery-pack',
    [
      { allowedChemistries: ['lfp', 'nmc'], warranty },
      { allowedChemistries: chemistries, warranty },
      { allowedChemistries: chemistries, warranty: { ...warranty, km: 200000 } },
    ],
    { every: 60, lastDaysAgo: 6 },
  )
  if (b.has('rst.battery-pack')) {
    // A plant that records a pack the service does not know is rejected at write time (physical-product.md).
    b.descendantSchema('rst.battery-pack', 'exe', packSchemas(next))
  }
  configureIfPresent(
    b,
    'rst.infotainment',
    [
      { otaServer: server, telemetry: `${server}/telemetry`, languages: ['en', 'sv', 'de', 'fr'] },
      { otaServer: server, telemetry: `${server}/telemetry`, languages: ['en', 'sv', 'no', 'da', 'de', 'fr', 'nl'] },
    ],
    { every: 40 },
  )
  // The assistance features, switched on one by one as the validation fleet signs them off.
  configureIfPresent(
    b,
    'rst.adas',
    evolve(
      {
        driverMonitoring: 'required',
        features: { adaptiveCruise: true, laneCentering: true, trafficJamAssist: false, handsFreeHighway: false },
      },
      next ? 5 : 4,
      (version) =>
        version === 2
          ? { calibration: { afterWindscreenReplacement: true } }
          : version === 3
            ? { features: { trafficJamAssist: true } }
            : version === 4
              ? { maxAssistSpeedKph: 140 }
              : { features: { handsFreeHighway: true } },
    ),
    { every: 35, lastDaysAgo: next ? 1 : 4 },
  )
  configureIfPresent(b, 'rst.brakes', {
    system: 'one-box-brake-by-wire',
    supplier: 'bremsa',
    fluid: 'dot-4-lv',
    padWarningMm: 3,
    regenerativeBlending: true,
  })
  configureIfPresent(b, 'rst.thermal', {
    heatPump: true,
    preconditioning: { targetCelsius: 25, startMinutesBeforeDeparture: 30 },
  })
  configureIfPresent(
    b,
    'rst.charging',
    [
      { plugAndCharge: { enabled: false }, chargeLimitPercent: 90 },
      {
        // The V2G root that the contract certificates of Plug & Charge (ISO 15118) chain up to.
        plugAndCharge: { enabled: true, v2gRootCertificate: '@(charging.plug-and-charge-root-ca, primaryVault)' },
        chargeLimitPercent: 90,
      },
    ],
    { every: 50, authors: ['annie'] },
  )
  configureIfPresent(b, 'rst.lighting', {
    welcomeLight: true,
    drivingBeam: 'auto',
    homologatedFor: ['ece-r149', 'fmvss-108'],
  })
}

function addFirmware(b: ScopeBuilder, ids?: FirmwareId[]) {
  for (const unit of firmware.filter((candidate) => !ids || ids.includes(idOf(candidate)))) {
    b.unit(unit.subsystem, unit.name, {
      unitType: unit.unitType,
      definition: unit.definition,
      title: unit.title,
      description: unit.description,
      values: { ecu: unit.ecu },
      createdDaysAgo: 700,
    })
  }
}

function firmwareDocuments(b: ScopeBuilder, server: string, channel: Channel) {
  const infotainment = { server, signingKey: '@(ota.infotainment-signing-key, primaryVault)' }
  configureIfPresent(
    b,
    'unt.infotainment.firmware',
    [
      { ...infotainment, deltaUpdates: false },
      { ...infotainment, deltaUpdates: true },
      { ...infotainment, deltaUpdates: true, maxDownloadMb: 3500 },
    ],
    { every: 45, authors: ['annie'] },
  )
  configureIfPresent(b, 'unt.infotainment.map-data', {
    provider: 'northstar-maps',
    mapRegion: 'europe',
    wifiOnly: true,
  })
  configureIfPresent(
    b,
    'unt.charging.firmware',
    {
      server,
      signingKey: '@(ota.charging-signing-key, primaryVault)',
      requireParked: true,
      minimumStateOfChargePercent: 20,
    },
    { authors: ['annie'] },
  )
  configureIfPresent(
    b,
    'unt.adas.perception-model',
    [
      { shadowModeDays: 7, validationFleet: 'canary' },
      { shadowModeDays: 14, validationFleet: 'canary' },
    ],
    { every: 30 },
  )
  configureIfPresent(b, 'unt.battery-pack.bms-firmware', { flashTimeoutMinutes: 25, requireHighVoltageOff: true })

  // Every firmware pin installs at night, parked and charged, from the channel of the view unless it pins another.
  const installWindow = { from: '02:00', to: '05:00' }
  b.template(
    'uxe',
    [
      { channel, autoInstall: true, installWindow },
      { channel, autoInstall: true, installWindow, requireParked: true, minimumStateOfChargePercent: 30 },
    ],
    { every: 60 },
  )
  b.typeSchema('uxe', {
    type: 'object',
    properties: { channel: { enum: channels }, version: { type: 'string', minLength: 1 } },
  })
}

// Parts and firmware are fitted the same way to a build, a test rig and one car.

/** Where parts are fitted: the context, and the end of the titles of its parts and pins. */
interface Fitting {
  subject: string
  context: string
  title: string
  meta: Meta
}

/** The own document of a firmware pin; a pin without one installs what the unit, the template and its build set. */
interface Pin {
  versions: Versions
  options?: HistoryOptions
  meta?: Meta
}

function fitPart(
  b: ScopeBuilder,
  at: Fitting,
  subsystem: SubsystemName,
  versions?: Versions | null,
  options?: HistoryOptions,
) {
  const key = b.execution(at.subject, subsystem, at.context, {
    title: `${titleOf(subsystem)}, ${at.title}`,
    ...at.meta,
  })
  if (versions) {
    b.configure(key, versions, options)
  }
}

function pinFirmware(b: ScopeBuilder, at: Fitting, unit: Firmware, pin?: Pin) {
  const key = b.unitOfExecution(at.subject, unit.subsystem, at.context, unit.name, {
    title: `${unit.title}, ${at.title}`,
    ...at.meta,
    ...pin?.meta,
  })
  if (pin) {
    b.configure(key, pin.versions, pin.options)
  }
}

/** The firmware of the fitted subsystems that the view ships; each gets a pin. */
const firmwareOf = (b: ScopeBuilder, parts: object) =>
  firmware.filter((unit) => unit.subsystem in parts && b.has(`unt.${idOf(unit)}`))

/** A pin that follows the default channel of its build instead of pinning one. */
const followTheBuild: Pin = { versions: { channel: '@config("/defaultFirmwareChannel")' } }

/** A car's VIN context, named after the last three characters of the VIN; its parts and pins date from it. */
function addCar(
  b: ScopeBuilder,
  subject: string,
  vin: string,
  meta: Meta & { title: string },
  name = `vin-${vin.slice(-3).toLowerCase()}`,
): Fitting {
  const title = `VIN …${vin.slice(-3)}`
  b.context(subject, name, { ...meta, title: `${meta.title}, ${title}`, values: { vin, ...meta.values } })
  return {
    subject,
    context: name,
    title,
    meta: { timeZone: meta.timeZone, createdDaysAgo: meta.createdDaysAgo, createdBy: meta.createdBy },
  }
}

// The Aurora exactly as the documentation and the board show it.

type AuroraTrim = 'touring' | 'gt'
type AuroraSubsystem = 'battery-pack' | 'infotainment' | 'adas'

/** The GT pack, exactly as the documentation shows it. */
const gtPack = { chemistry: 'nmc', capacityKwh: 91, supplier: 'helios', homologation: ['eu', 'uk'] }

const auroraBuilds: Record<AuroraTrim, { title: string; recipe: Doc; parts: Record<AuroraSubsystem, Doc> }> = {
  touring: {
    title: 'Aurora Touring',
    recipe: { wheelPackage: '19-aero', interior: 'textile-slate' },
    parts: {
      'battery-pack': { chemistry: 'lfp', capacityKwh: 75, supplier: 'nordcell', homologation: ['eu', 'uk'] },
      infotainment: { centerDisplayInches: 15.4, audio: 'standard-10' },
      // Only in feature-delivery1, which brings ADAS to the Touring.
      adas: { features: { adaptiveCruise: true, laneCentering: true, trafficJamAssist: false }, rollout: 'pilot' },
    },
  },
  gt: {
    title: 'Aurora GT',
    recipe: { wheelPackage: '21-forged', interior: 'nappa-charcoal' },
    parts: {
      'battery-pack': gtPack,
      infotainment: { centerDisplayInches: 15.4, audio: 'aurora-sound-14', headUpDisplay: true },
      adas: { features: { adaptiveCruise: true, laneCentering: true, trafficJamAssist: true } },
    },
  },
}

/** What the Aurora allows per subsystem; the battery fitment lists the packs the model may ship. */
const auroraFitments: Record<AuroraSubsystem, Doc> = {
  'battery-pack': { allowedPacks: ['lfp-75', 'nmc-91'], coolant: 'glycol-50' },
  infotainment: { headUnit: 'ivi-gen3', apps: ['navigation', 'media', 'charge-planner'] },
  adas: { sensorSet: 'camera-radar-v2', calibration: 'static-target' },
}

function addAurora(b: ScopeBuilder) {
  b.subject('aurora', {
    title: 'Aurora',
    description: 'The electric fastback on the second-generation platform, sold as **Touring** and **GT**.',
    labels: ['sedan', 'gen2'],
    values: { modelCode: 'AM1', productOwner: 'Hedy Lamarr', launchYear: 2024 },
    link: `${docs}/models/aurora`,
    createdDaysAgo: 700,
  })
  // The model: its platform generation and the markets it is homologated for.
  b.configure(
    'sbt.aurora',
    [
      { platformGeneration: 'gen2', markets: ['eu', 'uk'] },
      { platformGeneration: 'gen2', markets: ['eu', 'uk', 'no'] },
      { platformGeneration: 'gen2', markets: ['eu', 'uk', 'no', 'ch'] },
    ],
    { every: 90, lastDaysAgo: 12 },
  )
}

/** A trim: the recipe every car of it shares, with the default firmware channel of the build. */
function addAuroraBuild(b: ScopeBuilder, trim: AuroraTrim, channel: Channel) {
  const build = auroraBuilds[trim]
  b.context('aurora', trim, {
    title: build.title,
    labels: ['trim'],
    values: { plant: plants.torslanda.title, modelYear: 2026 },
    createdDaysAgo: 700,
  })
  b.configure(`cnt.aurora.${trim}`, { ...build.recipe, defaultFirmwareChannel: channel })
}

function auroraFitment(b: ScopeBuilder, subsystem: AuroraSubsystem) {
  b.usage('aurora', subsystem, { title: `${titleOf(subsystem)} of the Aurora`, createdDaysAgo: 700 })
  b.configure(`usg.aurora.${subsystem}`, auroraFitments[subsystem])
}

const auroraTrim = (trim: AuroraTrim, meta: Meta = { createdDaysAgo: 700 }): Fitting => ({
  subject: 'aurora',
  context: trim,
  title: auroraBuilds[trim].title,
  meta,
})

function auroraPart(
  b: ScopeBuilder,
  subsystem: AuroraSubsystem,
  trim: AuroraTrim,
  options?: HistoryOptions,
  meta?: Meta,
) {
  fitPart(b, auroraTrim(trim, meta), subsystem, auroraBuilds[trim].parts[subsystem], options)
}

function auroraPin(b: ScopeBuilder, trim: AuroraTrim, pin: Pin) {
  pinFirmware(b, auroraTrim(trim), unitOf('infotainment.firmware'), pin)
}

/** The board's Aurora without VIN 4F2 and the GT pin, shared by production and staging. */
function auroraCatalog(b: ScopeBuilder, channel: Channel) {
  addAurora(b)
  addAuroraBuild(b, 'touring', channel)
  addAuroraBuild(b, 'gt', channel)
  for (const subsystem of ['battery-pack', 'infotainment', 'adas'] as const) {
    auroraFitment(b, subsystem)
  }
  auroraPart(b, 'battery-pack', 'touring')
  // The trim document of the GT pack; it did not change when VIN 4F2 got its new module.
  auroraPart(b, 'battery-pack', 'gt', { lastDaysAgo: 25 })
  auroraPart(b, 'infotainment', 'touring')
  auroraPart(b, 'infotainment', 'gt')
  // ADAS is part of the GT recipe and not of the Touring.
  auroraPart(b, 'adas', 'gt')
  auroraPin(b, 'touring', followTheBuild)
}

/** The stable releases GT infotainment was pinned to, one a week; the release candidate brings the next one. */
function gtFirmware(next: boolean): Doc[] {
  const releases = evolve({ channel: 'stable', version: '2026.23.1' }, 18, (version) => ({
    version: `2026.${22 + version}.${version % 3}`,
    // The GT got delta updates a few weeks before the unit switched them on for every car.
    ...(version === 9 ? { deltaUpdates: true } : {}),
  }))
  return next ? [...releases, { ...releases.at(-1)!, version: '2026.42.0' }] : releases
}

function documentedAurora(b: ScopeBuilder, next: boolean) {
  auroraCatalog(b, 'stable')
  // One car that diverged from its trim. The VIN is a sibling of the trims, so it inherits nothing from the GT.
  const car = addCar(
    b,
    'aurora',
    'XAMA1E4K0TG0434F2',
    {
      title: 'Aurora GT',
      description:
        'An Aurora GT whose battery **module 4** was replaced under warranty. Everything else on the car still ' +
        `matches the [GT recipe](${docs}/models/aurora#gt).`,
      labels: ['vin', 'service'],
      values: { trim: 'gt', market: 'se' },
      timeZone: markets.se.timeZone,
      createdDaysAgo: 20,
      createdBy: 'annie',
    },
    'vin-4F2',
  )
  // The complete battery record of the car: the pack as delivered, then the replacement module and its date.
  fitPart(
    b,
    car,
    'battery-pack',
    [
      gtPack,
      {
        ...gtPack,
        replacement: {
          partNumber: 'BPM-91N-HX-04',
          module: 4,
          replacedOn: '2026-10-12',
          reason: 'cell-imbalance',
          workshop: markets.se.workshop,
        },
      },
    ],
    { every: 11, lastDaysAgo: 9, authors: ['annie'] },
  )
  // GT pins the stable channel; every stable release moves the pinned version.
  auroraPin(b, 'gt', { versions: gtFirmware(next), options: { every: 7, lastDaysAgo: next ? 1 : 3 } })
}

// More models of the same kind: a build recipe per trim and a fitted part per subsystem of the build.

interface Build {
  name: string
  title: string
  plant: Plant
  modelYear: number
  recipe: Doc
  /** The fitted part per subsystem, versions oldest first; null where the build keeps the model's fitment. */
  parts: Partial<Record<SubsystemName, Versions | null>>
  /** Firmware the build pins besides the defaults of pinOf. */
  pins?: Partial<Record<FirmwareId, Pin>>
  /** The lifecycle of the build and of everything fitted to it. */
  lifecycle?: Meta
  /** Days until the build can no longer be ordered; it ends the build only, the cars built keep their parts. */
  ordersCloseIn?: number
  description?: string
  labels?: string[]
  values?: Doc
}

interface Model {
  name: 'borealis' | 'zephyr'
  title: string
  /** The model's letter in the VIN. */
  vinCode: string
  description: string
  labels: string[]
  values: Doc
  document: Versions
  createdDaysAgo: number
  /** The fitments: what the model allows per subsystem; null where the model has nothing to say. */
  fitments: Partial<Record<SubsystemName, Doc | null>>
  builds: Build[]
}

const seats = (front: string, extras: Doc = {}): Doc[] => [
  { front, heating: true, ...extras, frame: 'sf-1' },
  // Recall AM-R-2026-09: new cars get the reinforced frame.
  { front, heating: true, ...extras, frame: 'sf-2' },
]
/** Recall AM-R-2026-07: new Zephyrs get the obc-11-b charger, the early ones are repaired car by car. */
const zephyrCharger: Doc[] = [
  { onBoardChargerKw: 11, maxDcKw: 135, charger: 'obc-11-a' },
  { onBoardChargerKw: 11, maxDcKw: 135, charger: 'obc-11-b' },
]
/** The obc-11-b charger runs firmware line 4. */
const chargerLine4: Pin = { versions: { version: 'obc-4.1.2' } }

/** The Summit's perception model is held back after reports of phantom braking under motorway gantries. */
const heldBack: Meta = {
  disabled: true,
  description: 'Held back after reports of phantom braking under motorway gantries; the cars stay on pm-2026.32.',
  modifiedDaysAgo: 6,
  modifiedBy: 'ada',
}

const borealis: Model = {
  name: 'borealis',
  title: 'Borealis',
  vinCode: 'B',
  description:
    'The seven-seat SUV on the second-generation platform, built in Torslanda for Europe and in Ridgeville for ' +
    'North America.',
  labels: ['suv', 'gen2'],
  values: { modelCode: 'AM2', productOwner: 'Hedy Lamarr', launchYear: 2025 },
  document: [
    { platformGeneration: 'gen2', markets: ['eu', 'uk', 'no'], towingKg: 2000 },
    { platformGeneration: 'gen2', markets: ['eu', 'uk', 'no', 'ch', 'us', 'ca'], towingKg: 2000 },
  ],
  createdDaysAgo: 420,
  fitments: {
    'battery-pack': { allowedPacks: ['lfp-82', 'nmc-107', 'nmca-107'], coolant: 'glycol-50' },
    infotainment: { headUnit: 'ivi-gen3', apps: ['navigation', 'media', 'charge-planner', 'trailer-assist'] },
    adas: { sensorSet: 'camera-radar-v2', calibration: 'static-target' },
    thermal: { heatPump: true, cabinFilter: 'hepa' },
    charging: { connector: 'ccs2', onBoardChargerKw: 11, maxDcKw: 200 },
    suspension: null,
    lighting: { headlights: 'led', adaptiveDrivingBeam: false },
    seats: null,
  },
  builds: [
    {
      name: 'explorer',
      title: 'Borealis Explorer',
      plant: 'torslanda',
      modelYear: 2026,
      recipe: { wheelPackage: '20-all-season', interior: 'wool-blend-dawn', defaultFirmwareChannel: 'stable' },
      parts: {
        'battery-pack': { chemistry: 'lfp', capacityKwh: 82, supplier: 'nordcell', homologation: ['eu', 'uk', 'no'] },
        infotainment: {
          centerDisplayInches: 15.4,
          audio: 'standard-10',
          vehicleModel: '@annotation("/modelCode", "Subject")',
        },
        adas: null,
        thermal: null,
        charging: null,
        suspension: { type: 'steel', rideHeightMm: 205 },
        lighting: null,
        seats: seats('comfort'),
      },
    },
    {
      name: 'summit',
      title: 'Borealis Summit',
      plant: 'torslanda',
      modelYear: 2026,
      recipe: { wheelPackage: '22-turbine', interior: 'nappa-amber', defaultFirmwareChannel: 'stable' },
      parts: {
        'battery-pack': {
          chemistry: 'nmc',
          capacityKwh: 107,
          supplier: 'helios',
          homologation: ['eu', 'uk', 'no', 'ch'],
        },
        infotainment: { centerDisplayInches: 17, audio: 'aurora-sound-16', headUpDisplay: true, rearScreens: 2 },
        adas: {
          sensorSet: 'camera-radar-lidar',
          features: { adaptiveCruise: true, laneCentering: true, trafficJamAssist: true, handsFreeHighway: true },
        },
        thermal: { heatPump: true, cabinFilter: 'hepa', climateZones: 4 },
        charging: { onBoardChargerKw: 22, maxDcKw: 250 },
        suspension: { type: 'air', rideHeightMm: 215, adaptiveDampers: true },
        lighting: { headlights: 'matrix-led', adaptiveDrivingBeam: true },
        seats: seats('comfort', { ventilation: true, memory: true, massage: true }),
      },
      pins: {
        // Rolled back to the previous model and disabled until the fix is validated.
        'adas.perception-model': {
          versions: ['pm-2026.28', 'pm-2026.32', 'pm-2026.36', 'pm-2026.32'].map((version) => ({ version })),
          options: { every: 14, lastDaysAgo: 6 },
          meta: heldBack,
        },
      },
    },
    {
      name: 'trail',
      title: 'Borealis Trail',
      plant: 'ridgeville',
      modelYear: 2026,
      description: 'The North American trim: NACS charge port, air suspension with an off-road mode.',
      recipe: { wheelPackage: '19-all-terrain', interior: 'vegan-leather-sand', defaultFirmwareChannel: 'stable' },
      parts: {
        'battery-pack': { chemistry: 'nmca', capacityKwh: 107, supplier: 'voltaris', homologation: ['us', 'ca'] },
        infotainment: { centerDisplayInches: 15.4, audio: 'standard-10', units: 'imperial' },
        adas: null,
        thermal: { heatPump: true, cabinFilter: 'hepa', batteryHeaterKw: 7 },
        charging: { connector: 'nacs', onBoardChargerKw: 11.5, maxDcKw: 250 },
        suspension: { type: 'air', rideHeightMm: 245, adaptiveDampers: true, offRoadMode: true },
        lighting: { headlights: 'led', adaptiveDrivingBeam: false, sideMarkers: true },
        seats: seats('comfort', { ventilation: true }),
      },
      pins: { 'infotainment.map-data': { versions: { mapRegion: 'north-america' } } },
    },
    {
      name: 'us-homologation',
      title: 'Borealis US homologation fleet',
      plant: 'ridgeville',
      modelYear: 2026,
      description:
        'Pre-series cars built for the FMVSS crash tests and the EPA range certification. The test registration ' +
        'ended when the certificates were issued.',
      labels: ['homologation', 'pre-series'],
      values: { certificate: 'EPA-AMX-2026-0142', cars: 6 },
      lifecycle: { expiresIn: -20, createdDaysAgo: 240, createdBy: 'hedy' },
      recipe: { wheelPackage: '19-all-terrain', interior: 'vegan-leather-sand', defaultFirmwareChannel: 'beta' },
      parts: {
        'battery-pack': { chemistry: 'nmca', capacityKwh: 107, supplier: 'voltaris', homologation: ['us'] },
        adas: { sensorSet: 'camera-radar-v2', dataLogging: true },
        charging: { connector: 'nacs', onBoardChargerKw: 11.5, maxDcKw: 250 },
        lighting: { headlights: 'led', adaptiveDrivingBeam: false, sideMarkers: true },
      },
      // The pre-series cars ran every firmware from the beta channel of their build.
      pins: {
        'battery-pack.bms-firmware': followTheBuild,
        'adas.perception-model': followTheBuild,
        'charging.firmware': followTheBuild,
      },
    },
  ],
}

const zephyr: Model = {
  name: 'zephyr',
  title: 'Zephyr',
  vinCode: 'Z',
  description: 'The compact hatchback on the third-generation platform, built in Debrecen.',
  labels: ['compact', 'gen3'],
  values: { modelCode: 'AM3', productOwner: 'Hedy Lamarr', launchYear: 2026 },
  document: { platformGeneration: 'gen3', markets: ['eu', 'uk', 'no', 'ch'] },
  createdDaysAgo: 260,
  fitments: {
    'battery-pack': { allowedPacks: ['lfp-58', 'nmc-69', 'nmca-77'], coolant: 'glycol-50' },
    infotainment: { headUnit: 'ivi-gen3', apps: ['navigation', 'media', 'charge-planner'] },
    adas: { sensorSet: 'camera-radar-v2', calibration: 'dynamic' },
    thermal: null,
    charging: { connector: 'ccs2' },
    suspension: { type: 'steel', rideHeightMm: 150 },
    lighting: { headlights: 'led', adaptiveDrivingBeam: false },
    seats: null,
  },
  builds: [
    {
      name: 'city',
      title: 'Zephyr City',
      plant: 'debrecen',
      modelYear: 2026,
      recipe: { wheelPackage: '17-aero', interior: 'recycled-pet-ash', defaultFirmwareChannel: 'stable' },
      parts: {
        'battery-pack': { chemistry: 'lfp', capacityKwh: 58, supplier: 'nordcell', homologation: ['eu', 'uk', 'ch'] },
        infotainment: { centerDisplayInches: 12.3, audio: 'standard-6' },
        adas: null,
        thermal: { heatPump: false, ptcHeaterKw: 5 },
        charging: zephyrCharger,
        suspension: null,
        lighting: null,
        seats: { front: 'standard', heating: true },
      },
      pins: { 'charging.firmware': chargerLine4 },
    },
    {
      name: 'launch-edition',
      title: 'Zephyr Launch Edition',
      plant: 'debrecen',
      modelYear: 2026,
      description:
        'The first 2,000 cars, in two colours. The order book closes when the series is sold out; the cars ' +
        'delivered keep their fitted parts and firmware pins.',
      labels: ['trim', 'limited'],
      ordersCloseIn: 45,
      recipe: {
        wheelPackage: '18-aero',
        interior: 'recycled-pet-ash',
        paints: ['glacier-blue', 'aurora-green'],
        defaultFirmwareChannel: 'stable',
      },
      parts: {
        'battery-pack': { chemistry: 'nmc', capacityKwh: 69, supplier: 'helios', homologation: ['eu', 'uk', 'no'] },
        infotainment: { centerDisplayInches: 12.3, audio: 'aurora-sound-10' },
        adas: { features: { adaptiveCruise: true, laneCentering: true, trafficJamAssist: true } },
        thermal: { heatPump: true },
        charging: zephyrCharger,
        suspension: null,
        lighting: { headlights: 'matrix-led', adaptiveDrivingBeam: true },
        seats: { front: 'sport', heating: true, ventilation: false },
      },
      pins: { 'charging.firmware': chargerLine4 },
    },
    {
      name: 'sport',
      title: 'Zephyr Sport (MY2027)',
      plant: 'debrecen',
      modelYear: 2027,
      description: 'The model-year 2027 trim with dual motors, on sale from the start of the model year.',
      labels: ['trim', 'my2027'],
      lifecycle: { startsIn: 40, createdDaysAgo: 12, createdBy: 'hedy' },
      recipe: { wheelPackage: '19-sport', interior: 'microsuede-graphite', defaultFirmwareChannel: 'stable' },
      parts: {
        'battery-pack': { chemistry: 'nmca', capacityKwh: 77, supplier: 'helios', homologation: ['eu', 'uk', 'no'] },
        infotainment: { centerDisplayInches: 14.5, audio: 'aurora-sound-10', headUpDisplay: true },
        adas: { features: { adaptiveCruise: true, laneCentering: true, trafficJamAssist: true } },
        thermal: { heatPump: true },
        charging: { onBoardChargerKw: 22, maxDcKw: 175, charger: 'obc-22-a' },
        suspension: { type: 'adaptive', rideHeightMm: 135 },
        lighting: { headlights: 'matrix-led', adaptiveDrivingBeam: true },
        seats: { front: 'sport', heating: true, ventilation: true },
      },
    },
  ],
}

const models = [borealis, zephyr]

const buildOf = (model: Model, name: string) => model.builds.find((build) => build.name === name)!
const partsOf = (build: Build) => Object.entries(build.parts) as [SubsystemName, Versions | null][]

/** The latest document of a build's part: what a car that diverged from its build stores in full. */
function recipePart(build: Build, subsystem: SubsystemName): Doc {
  const versions = build.parts[subsystem]
  return (Array.isArray(versions) ? versions.at(-1) : versions) ?? {}
}

/** Infotainment follows the channel of its build; other pins install the channel's latest unless the build pins one. */
const pinOf = (unit: Firmware, build: Build) =>
  build.pins?.[idOf(unit)] ?? (idOf(unit) === 'infotainment.firmware' ? followTheBuild : undefined)

/**
 * When the documents of a build were written: after it was created and, for a build that ended, before its end. The
 * latest version is at most 25 days old, so it keeps its content.
 */
function documentDays(meta: Meta) {
  const age = meta.createdDaysAgo ?? 400
  return { age, newest: Math.max(0, -(meta.expiresIn ?? 0)) + 1, oldest: Math.min(25, age) }
}

/** A single part document was written while the build was open; a part with a history changed in a recall. */
function partHistory(b: ScopeBuilder, versions: Versions, meta: Meta): HistoryOptions {
  const { age, newest, oldest } = documentDays(meta)
  if (!Array.isArray(versions)) {
    return { lastDaysAgo: b.rng.int(Math.max(2, newest), oldest) }
  }
  // The recalls changed the parts 25 days ago; the versions before go back at most to the start of the build.
  return { every: Math.min(60, Math.floor((age - oldest) / (versions.length - 1))), lastDaysAgo: oldest }
}

function addBuild(b: ScopeBuilder, model: Model, build: Build) {
  // A scheduled or ended build passes its lifecycle on to everything fitted to it.
  const meta: Meta = {
    createdDaysAgo: model.createdDaysAgo,
    timeZone: plants[build.plant].timeZone,
    ...build.lifecycle,
  }
  const { newest } = documentDays(meta)
  b.context(model.name, build.name, {
    title: build.title,
    description: build.description,
    labels: build.labels ?? ['trim'],
    values: { plant: plants[build.plant].title, modelYear: build.modelYear, ...build.values },
    ...meta,
    expiresIn: build.ordersCloseIn ?? meta.expiresIn,
  })
  b.configure(`cnt.${model.name}.${build.name}`, build.recipe, { lastDaysAgo: newest })

  const at: Fitting = { subject: model.name, context: build.name, title: build.title, meta }
  for (const [subsystem, versions] of partsOf(build)) {
    fitPart(b, at, subsystem, versions, versions ? partHistory(b, versions, meta) : undefined)
  }
  for (const unit of firmwareOf(b, build.parts)) {
    const pin = pinOf(unit, build)
    pinFirmware(b, at, unit, pin && { ...pin, options: pin.options ?? { lastDaysAgo: newest } })
  }
}

function addModel(b: ScopeBuilder, model: Model, builds: Build[] = model.builds) {
  b.subject(model.name, {
    title: model.title,
    description: model.description,
    labels: model.labels,
    values: model.values,
    link: `${docs}/models/${model.name}`,
    createdDaysAgo: model.createdDaysAgo,
  })
  b.configure(`sbt.${model.name}`, model.document, { every: 120, lastDaysAgo: 18 })
  for (const [subsystem, document] of Object.entries(model.fitments) as [SubsystemName, Doc | null][]) {
    b.usage(model.name, subsystem, {
      title: `${titleOf(subsystem)} of the ${model.title}`,
      createdDaysAgo: model.createdDaysAgo,
    })
    if (document) {
      b.configure(`usg.${model.name}.${subsystem}`, document)
    }
  }
  builds.forEach((build) => addBuild(b, model, build))
}

// Cars that diverged from their build: a VIN context is created only when a car needs one.

interface Car {
  /** The last six characters of the VIN; the context is named after the last three. */
  serial: string
  build: string
  market: Market
  /** Days since the car got its VIN context: the repair, or the start of its beta. */
  daysAgo: number
}

const modelYears: Record<number, string> = { 2025: 'S', 2026: 'T', 2027: 'V' }
const vinOf = (model: Model, build: Build, serial: string) =>
  `XAM${model.vinCode}1E4K0${modelYears[build.modelYear]}${plants[build.plant].code}${serial}`

/** Borealis cars that, like VIN 4F2, had a battery module replaced; each stores its complete pack record. */
const packRepairs: (Car & { module: number; partNumber: string; replacedOn: string })[] = [
  {
    serial: '02A4D7',
    build: 'summit',
    market: 'no',
    daysAgo: 13,
    module: 7,
    partNumber: 'BPM-107N-HX-07',
    replacedOn: '2026-10-08',
  },
  {
    serial: '0558K3',
    build: 'explorer',
    market: 'de',
    daysAgo: 20,
    module: 2,
    partNumber: 'BPM-82L-NC-02',
    replacedOn: '2026-10-01',
  },
]

/** Borealis cars repaired in recall AM-R-2026-09: a weld on the first front seat frames (sf-1). */
const seatRecall: Car[] = [
  { serial: '0117K9', build: 'explorer', market: 'se', daysAgo: 24 },
  { serial: '0232C8', build: 'explorer', market: 'no', daysAgo: 22 },
  { serial: '0415M1', build: 'summit', market: 'de', daysAgo: 21 },
  { serial: '0469R4', build: 'explorer', market: 'nl', daysAgo: 19 },
  { serial: '0583W7', build: 'trail', market: 'us', daysAgo: 17 },
  { serial: '0626H2', build: 'summit', market: 'uk', daysAgo: 15 },
  { serial: '0748T5', build: 'explorer', market: 'dk', daysAgo: 12 },
  { serial: '0811N6', build: 'trail', market: 'us', daysAgo: 9 },
  { serial: '0957V3', build: 'summit', market: 'ch', daysAgo: 6 },
  { serial: '1032Y8', build: 'explorer', market: 'se', daysAgo: 3 },
]

/** Zephyr cars repaired in recall AM-R-2026-07: the obc-11-a charger overheats at full AC power. */
const chargerRecall: Car[] = [
  { serial: '0213F8', build: 'city', market: 'de', daysAgo: 24 },
  { serial: '0347A2', build: 'launch-edition', market: 'se', daysAgo: 23 },
  { serial: '0389D6', build: 'city', market: 'nl', daysAgo: 21 },
  { serial: '0412K4', build: 'launch-edition', market: 'no', daysAgo: 20 },
  { serial: '0455B9', build: 'city', market: 'de', daysAgo: 18 },
  { serial: '0516E3', build: 'city', market: 'uk', daysAgo: 17 },
  { serial: '0568G7', build: 'launch-edition', market: 'dk', daysAgo: 15 },
  { serial: '0621H5', build: 'city', market: 'ch', daysAgo: 13 },
  { serial: '0674J8', build: 'city', market: 'de', daysAgo: 11 },
  { serial: '0727L3', build: 'launch-edition', market: 'se', daysAgo: 9 },
  { serial: '0792M6', build: 'city', market: 'nl', daysAgo: 7 },
  { serial: '0839P1', build: 'city', market: 'uk', daysAgo: 5 },
  { serial: '0883R7', build: 'launch-edition', market: 'no', daysAgo: 4 },
  { serial: '0946T2', build: 'city', market: 'de', daysAgo: 2 },
]

/** Canary cars of the infotainment beta: they pin the beta channel without changing their build. */
const canaries: [Model, Car][] = [
  [borealis, { serial: '0128C1', build: 'summit', market: 'se', daysAgo: 18 }],
  [borealis, { serial: '0190B6', build: 'explorer', market: 'se', daysAgo: 18 }],
  [zephyr, { serial: '0044F9', build: 'city', market: 'se', daysAgo: 16 }],
]

/** A Zephyr whose warranty claim was rejected; its VIN context was deleted again. */
const rejectedClaim: Car = { serial: '0975S2', build: 'city', market: 'de', daysAgo: 30 }

/** A car of a build that diverged from it: the service opened its VIN context. */
function serviceCar(b: ScopeBuilder, model: Model, car: Car, label: string, description: string) {
  const build = buildOf(model, car.build)
  const at = addCar(b, model.name, vinOf(model, build, car.serial), {
    title: build.title,
    description,
    labels: ['vin', label],
    values: { trim: car.build, market: car.market },
    timeZone: markets[car.market].timeZone,
    createdDaysAgo: car.daysAgo,
    createdBy: 'annie',
  })
  return { at, build }
}

/** The service writes the complete record of the part, or the firmware it installed, when the car comes in. */
const serviced = (car: Car): HistoryOptions => ({ lastDaysAgo: car.daysAgo, authors: ['annie'] })

/** The recall record names the campaign, the new part and the workshop; the VIN comes from the car's context. */
const recallOf = (campaign: string, partNumber: string, car: Car) => ({
  campaign,
  partNumber,
  workshop: markets[car.market].workshop,
  vin: '@annotation("/vin", "Context")',
})
const recallNote = (campaign: string, part: string) =>
  `${part} replaced in recall [${campaign}](${docs}/recalls/${campaign.toLowerCase()}).`

function divergedCars(b: ScopeBuilder) {
  for (const car of packRepairs) {
    const note = `Battery module ${car.module} replaced under warranty.`
    const { at, build } = serviceCar(b, borealis, car, 'service', note)
    const replacement = {
      partNumber: car.partNumber,
      module: car.module,
      replacedOn: car.replacedOn,
      workshop: markets[car.market].workshop,
    }
    fitPart(b, at, 'battery-pack', { ...recipePart(build, 'battery-pack'), replacement }, serviced(car))
  }

  // A part recall starts from the subsystem; the cars already repaired are the VIN contexts naming the replacement.
  for (const car of seatRecall) {
    const { at, build } = serviceCar(b, borealis, car, 'recall', recallNote('AM-R-2026-09', 'Front seat frames'))
    const recall = recallOf('AM-R-2026-09', 'SF-2-FR', car)
    fitPart(b, at, 'seats', { ...recipePart(build, 'seats'), recall }, serviced(car))
  }
  for (const car of chargerRecall) {
    const note = `${recallNote('AM-R-2026-07', 'On-board charger')} The new unit runs firmware line 4.`
    const { at, build } = serviceCar(b, zephyr, car, 'recall', note)
    const recall = recallOf('AM-R-2026-07', 'OBC-11-B', car)
    fitPart(b, at, 'charging', { ...recipePart(build, 'charging'), charger: 'obc-11-b', recall }, serviced(car))
    pinFirmware(b, at, unitOf('charging.firmware'), { ...chargerLine4, options: serviced(car) })
  }

  for (const [model, car] of canaries) {
    const { at, build } = serviceCar(b, model, car, 'canary', 'Canary car of the infotainment beta program.')
    fitPart(b, at, 'infotainment', { ...recipePart(build, 'infotainment'), canary: true }, serviced(car))
    // The beta program ends with the next stable release.
    const beta: Pin = { versions: { channel: 'beta' }, options: serviced(car), meta: { expiresIn: 30 } }
    pinFirmware(b, at, unitOf('infotainment.firmware'), beta)
  }
}

/** The electric tow hitch of the Borealis, withdrawn before the first car was built with it. */
function towHitch(b: ScopeBuilder) {
  b.responsibility('tow-hitch', {
    title: 'Electric tow hitch',
    description:
      'Retractable hitch, withdrawn in recall AM-R-2026-02 before the first deliveries: the locking pin could ' +
      'release under load. No car was built with it, so the option was removed from the catalog.',
    labels: ['body', 'accessory'],
    values: { owner: 'body@aurora-motors.example' },
    createdDaysAgo: 380,
  })
  // The stop-ship is the last change before the option was deleted.
  const stopShip: HistoryOptions = { every: 200, lastDaysAgo: 25, authors: ['hedy'] }
  const hitch = { maxTowingKg: 2000, noseWeightKg: 100 }
  b.configure('rst.tow-hitch', [hitch, { ...hitch, stopShip: 'AM-R-2026-02' }], stopShip)
  b.usage('borealis', 'tow-hitch', { title: 'Electric tow hitch of the Borealis', createdDaysAgo: 380 })
  const part = { type: 'electric-retractable', partNumber: 'TH-E-2000' }
  for (const build of ['explorer', 'trail']) {
    const title = `Electric tow hitch, ${buildOf(borealis, build).title}`
    b.execution('borealis', 'tow-hitch', build, { title, createdDaysAgo: 380 })
    b.configure(`exe.borealis.tow-hitch.${build}`, [part, { ...part, orderable: false }], stopShip)
  }
}

/** Release 2026.42 brings the phone as a key to the Borealis. */
function digitalKey(b: ScopeBuilder) {
  b.responsibility('digital-key', {
    title: 'Digital key',
    description: 'The phone as a key over UWB and NFC, shared from the Aurora app.',
    labels: ['connected', 'feature'],
    values: { owner: 'connected@aurora-motors.example' },
    link: `${docs}/subsystems/digital-key`,
    createdDaysAgo: 10,
    createdBy: 'hedy',
  })
  b.configure(
    'rst.digital-key',
    { standard: 'ccc-3.0', maxSharedKeys: 5, issuerCertificate: '@(digital-key.issuer-certificate, primaryVault)' },
    { authors: ['annie'] },
  )
  b.usage('borealis', 'digital-key', { title: 'Digital key of the Borealis', createdDaysAgo: 10, createdBy: 'hedy' })
  for (const build of ['explorer', 'summit']) {
    const title = `Digital key, ${buildOf(borealis, build).title}`
    b.execution('borealis', 'digital-key', build, { title, createdDaysAgo: 10, createdBy: 'hedy' })
    b.configure(`exe.borealis.digital-key.${build}`, { uwb: true, nfcCard: build === 'summit' })
  }
}

function production(b: ScopeBuilder, next: boolean) {
  addSubsystems(b)
  subsystemDocuments(b, ota.production, next)
  addFirmware(b)
  firmwareDocuments(b, ota.production, 'stable')
  documentedAurora(b, next)
  models.forEach((model) => addModel(b, model))
  divergedCars(b)

  if (next) {
    digitalKey(b)
    return
  }

  // Deleted: the tow hitch that was withdrawn before any car was built with it, with its fitment and parts, and a VIN
  // context that was not needed after all.
  towHitch(b)
  serviceCar(
    b,
    zephyr,
    rejectedClaim,
    'service',
    'Opened for a warranty claim on the charge port; the claim was rejected.',
  )
  b.trash('rst.tow-hitch', { daysAgo: 21, by: 'hedy' })
  b.trash('cnt.zephyr.vin-5s2', { daysAgo: 9, by: 'annie' })
}

interface Rig {
  name: string
  title: string
  description: string
  values: Doc
  parts: Partial<Record<SubsystemName, Doc>>
}

/** The test rigs of the R&D lab, each with the parts it carries. */
const rigs: Rig[] = [
  {
    name: 'hil-bench',
    title: 'HIL bench',
    description: 'Hardware-in-the-loop bench: a real head unit and ADAS computer, an emulated battery.',
    values: { rig: 'HIL-TOR-03', lab: 'Torslanda R&D, building 12' },
    parts: {
      'battery-pack': { chemistry: 'nmc', capacityKwh: 91, supplier: 'helios', emulator: 'bat-emu-800' },
      infotainment: { logLevel: 'debug', developerMenu: true },
      adas: { logLevel: 'trace', replayDataset: 'https://replay.dev.aurora-motors.example/gothenburg-rain' },
    },
  },
  {
    name: 'test-mule',
    title: 'Test mule',
    description: 'A hand-built prototype Aurora on the Hällered proving ground.',
    values: { vin: 'XAMA1E4K0TGP00M7X', driver: 'Ada Lovelace' },
    parts: {
      'battery-pack': { chemistry: 'nmca', capacityKwh: 95, supplier: 'voltaris', prototype: true },
      infotainment: { logLevel: 'debug', developerMenu: true },
      adas: { logLevel: 'debug', recordSensorData: true },
      charging: { logLevel: 'debug', chargerSimulator: 'https://evse-sim.dev.aurora-motors.example' },
    },
  },
  {
    name: 'sil-cluster',
    title: 'SIL cluster',
    description: 'Software-in-the-loop simulation: one virtual car per pull request.',
    values: { cluster: 'sil.dev.aurora-motors.example' },
    parts: {
      infotainment: { logLevel: 'debug', mockGps: true },
      adas: { logLevel: 'trace', simulator: 'scenario-runner' },
    },
  },
]

/** Development: the Aurora on the rigs of the R&D lab, with debug logging, the sandbox OTA server and nightlies. */
function development(b: ScopeBuilder) {
  addSubsystems(b, ['battery-pack', 'infotainment', 'adas', 'charging'])
  subsystemDocuments(b, ota.development, false)
  addFirmware(b, ['infotainment.firmware', 'adas.perception-model', 'charging.firmware'])
  firmwareDocuments(b, ota.development, 'nightly')
  addAurora(b)
  for (const subsystem of ['battery-pack', 'infotainment', 'adas'] as const) {
    auroraFitment(b, subsystem)
  }
  b.usage('aurora', 'charging', { title: 'Charging of the Aurora' })

  // The rigs run the nightly head-unit image with debug symbols; the other firmware follows the template.
  const nightly: Pin = { versions: { channel: 'nightly', debugSymbols: true } }
  for (const rig of rigs) {
    const at: Fitting = {
      subject: 'aurora',
      context: rig.name,
      title: rig.title,
      meta: { createdDaysAgo: 150, createdBy: 'ada' },
    }
    b.context('aurora', rig.name, {
      title: rig.title,
      description: rig.description,
      labels: ['test-rig'],
      values: rig.values,
      ...at.meta,
    })
    for (const [subsystem, document] of Object.entries(rig.parts) as [SubsystemName, Doc][]) {
      fitPart(b, at, subsystem, document, { lastDaysAgo: b.rng.int(1, 10) })
    }
    for (const unit of firmwareOf(b, rig.parts)) {
      pinFirmware(b, at, unit, idOf(unit) === 'infotainment.firmware' ? nightly : undefined)
    }
  }
}

/** Pre-series Auroras that test the beta firmware before it reaches the canary cars. */
function preSeriesFleet(b: ScopeBuilder) {
  const suffixes = ['2B7', '3C9', '4D1', '5E8', '6F3', '7G2', '8H6', '9J4', 'A5K', 'B7L', 'C2M', 'D8N']
  for (const suffix of b.rng.some(suffixes, 6)) {
    const trim = b.rng.pick(['touring', 'gt'] as const)
    const at = addCar(b, 'aurora', `XAMA1E4K0TGP00${suffix}`, {
      title: `Pre-series ${auroraBuilds[trim].title}`,
      labels: ['vin', 'pre-series'],
      values: { trim, testProgram: b.rng.pick(['winter', 'durability', 'ota-soak']) },
      createdDaysAgo: b.rng.int(20, 90),
      createdBy: 'annie',
    })
    const infotainment = { ...auroraBuilds[trim].parts.infotainment, logLevel: 'info', telemetryUpload: 'every-trip' }
    fitPart(b, at, 'infotainment', infotainment)
    pinFirmware(b, at, unitOf('infotainment.firmware'), {
      versions: { channel: 'beta', version: '2026.42.0-rc.2' },
    })
  }
}

/** Staging: the staging OTA server with the Aurora, the Zephyr City and the pre-series fleet on the beta channel. */
function staging(b: ScopeBuilder) {
  addSubsystems(b)
  subsystemDocuments(b, ota.staging, false)
  addFirmware(b)
  firmwareDocuments(b, ota.staging, 'beta')
  auroraCatalog(b, 'beta')
  auroraPin(b, 'gt', followTheBuild)
  const city = buildOf(zephyr, 'city')
  addModel(b, zephyr, [{ ...city, recipe: { ...city.recipe, defaultFirmwareChannel: 'beta' } }])
  preSeriesFleet(b)
}

/** The board's feature delivery: ADAS on the Touring, next to its infotainment and firmware pin. */
function featureDelivery(b: ScopeBuilder) {
  addSubsystems(b, ['infotainment', 'adas'])
  subsystemDocuments(b, ota.production, false)
  addFirmware(b, ['infotainment.firmware'])
  firmwareDocuments(b, ota.production, 'stable')
  addAurora(b)
  addAuroraBuild(b, 'touring', 'stable')
  auroraFitment(b, 'infotainment')
  auroraFitment(b, 'adas')
  auroraPart(b, 'infotainment', 'touring')
  // The new feature: the camera and radar assistance of the GT, fitted to the Touring two weeks ago.
  auroraPart(
    b,
    'adas',
    'touring',
    { lastDaysAgo: 5, authors: ['ada'] },
    {
      description: 'Pilot of the GT camera and radar assistance on the Touring, without the traffic jam assist.',
      createdDaysAgo: 14,
      createdBy: 'hedy',
    },
  )
  auroraPin(b, 'touring', followTheBuild)
}

export const vehiclePlatform: ShowcaseProject = {
  org: 'aurora-motors',
  project: 'vehicle-platform',
  summary: 'The physical product of the documentation: vehicle models, trims and VINs, and the parts fitted to them.',
  members: { hedy: 'Owner', annie: 'ContributorWithSecrets', ada: 'Contributor', frances: 'Reader' },
  settings: {
    types: {
      rst: { label: 'Subsystem', color: 'violet', icon: 'i-lucide-component' },
      unt: { label: 'Firmware', plural: 'Firmware', color: 'fuchsia', icon: 'i-lucide-cpu' },
      sbt: { label: 'Model', color: 'blue', icon: 'i-lucide-truck' },
      usg: { label: 'Fitment', color: 'amber', icon: 'i-lucide-wrench' },
      cnt: { label: 'Build', color: 'teal', icon: 'i-lucide-factory' },
      exe: { label: 'Fitted part', color: 'emerald', icon: 'i-lucide-package' },
      uxe: { label: 'Firmware pin', color: 'orange', icon: 'i-lucide-git-branch' },
    },
    defaultTimeZone: 'Europe/Stockholm',
    dateFormat: 'yyyy-mm-dd',
  },
  views: environmentViews({
    development: 'Development: the Aurora on the HIL bench, a test mule and the SIL cluster',
    staging: 'Staging: the pre-series fleet on the beta channel',
    'release-candidate': 'Release 2026.42: the digital key and a stricter battery schema',
    'feature-delivery1': 'ADAS on the Touring trim',
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
