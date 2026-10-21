import { describe, expect, it } from 'vitest'
import { combinedSchema, effectiveContent, schemaErrors } from '@/mock/configuration-handlers'
import { showcaseScope } from '@/mock/showcase'
import { ancestorKeyOf } from '@/utils/binding-language'
import { annotationStatus } from '@/utils/annotation-status'
import { getAt } from '@/utils/json-pointer'
import { showcaseOf, unseededShowcaseDb } from './support'

// harbor-industrial/plant-fleet follows docs/42for.net/platform/examples/device-fleet.md and example 8 of the Miro
// board: the site is the subject, the device the context, a capability the responsibility, a sampling job the unit.

const showcase = showcaseOf('harbor-industrial', 'plant-fleet')
const startingWith = (keys: Iterable<string>, prefix: string) => [...keys].filter((key) => key.startsWith(prefix))
const sorted = (keys: Iterable<string>) => [...keys].toSorted()

const day = 86_400_000
const views = ['default', 'development', 'staging', 'release-candidate', 'feature-delivery1', 'campaign-march']
/** The members who may edit: Radia, Ada and Ken. */
const editors = ['Radia Perlman', 'Ada Lovelace', 'Ken Thompson']

const alertBinding = '@annotation("$.alertChannel", "Subject")'
const annotationCall = /^@annotation\("\$\.([^"]+)", "([^"]+)"\)$/

/** The value an `@annotation("$.x", "<Type>")` binding of `key` reads: the Values of its ancestor of that type. */
function bound(view: string, key: string, binding: unknown) {
  const [, path, type] = annotationCall.exec(String(binding)) ?? []
  const ancestor = ancestorKeyOf(key, type ?? '')
  return { ancestor, value: getAt(showcase.annotations(view).get(ancestor ?? '')?.Values, path?.split('.') ?? []) }
}

const boardDefault = [
  'sbt.harbor-plant',
  'rst.telemetry',
  'rst.control-loop',
  'rst.maintenance',
  'cnt.harbor-plant.gateway',
  'cnt.harbor-plant.compressor-1',
  'cnt.harbor-plant.compressor-2',
  'unt.control-loop.sample',
  'usg.harbor-plant.telemetry',
  'usg.harbor-plant.control-loop',
  'usg.harbor-plant.maintenance',
  'exe.harbor-plant.telemetry.gateway',
  'exe.harbor-plant.control-loop.compressor-1',
  'exe.harbor-plant.control-loop.compressor-2',
  'exe.harbor-plant.maintenance.compressor-1',
  'uxe.harbor-plant.control-loop.compressor-1.sample',
  'uxe.harbor-plant.control-loop.compressor-2.sample',
]

const boardDevelopment = [
  'sbt.harbor-plant',
  'rst.telemetry',
  'rst.control-loop',
  'cnt.harbor-plant.test-rig',
  'unt.control-loop.sample',
  'usg.harbor-plant.telemetry',
  'usg.harbor-plant.control-loop',
  'exe.harbor-plant.telemetry.test-rig',
  'exe.harbor-plant.control-loop.test-rig',
  'uxe.harbor-plant.control-loop.test-rig.sample',
]

describe('harbor-industrial/plant-fleet', () => {
  it('has the catalog of the documentation and of the board', () => {
    const keys = showcase.keys('default')
    for (const key of [...boardDefault, 'sbt.west-quay', 'usg.west-quay.telemetry']) {
      expect(keys.has(key), key).toBe(true)
    }
    // Harbor Plant is exactly the board's frame: two compressors and one gateway, three capabilities.
    for (const code of ['cnt', 'usg', 'exe', 'uxe']) {
      expect(sorted(startingWith(keys, `${code}.harbor-plant.`)), code).toEqual(
        sorted(startingWith(boardDefault, `${code}.harbor-plant.`)),
      )
    }
    // West Quay runs the same capabilities as Harbor Plant, on other hardware.
    expect(sorted(startingWith(keys, 'usg.west-quay.'))).toEqual(
      expect.arrayContaining(['usg.west-quay.control-loop', 'usg.west-quay.maintenance', 'usg.west-quay.telemetry']),
    )
    expect(showcase.annotations('default').get('cnt.west-quay.compressor-a')?.Values?.model).not.toBe(
      showcase.annotations('default').get('cnt.harbor-plant.compressor-1')?.Values?.model,
    )
    // The extension of the documentation: five sites, six capabilities and a job of every unit type.
    expect(sorted(startingWith(keys, 'sbt.'))).toEqual(
      sorted(['sbt.harbor-plant', 'sbt.west-quay', 'sbt.north-dock', 'sbt.river-mill', 'sbt.east-yard']),
    )
    for (const key of [
      'rst.vibration-monitoring',
      'rst.energy-metering',
      'rst.video',
      'unt.maintenance.calibrate',
      'unt.telemetry.firmware-update',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    const unitType = (key: string) => (showcase.annotations('default').get(key) as { UnitType?: string }).UnitType
    expect(new Set(startingWith(keys, 'unt.').map(unitType))).toEqual(new Set(['time', 'message', 'file', 'event']))
    for (const site of ['north-dock', 'river-mill', 'east-yard']) {
      const devices = startingWith(keys, `cnt.${site}.`).length
      expect(devices, site).toBeGreaterThanOrEqual(8)
      expect(devices, site).toBeLessThanOrEqual(15)
    }
  })

  it('stores the tuned thresholds of compressor 1, in their third version', () => {
    expect(showcase.own('default', 'exe.harbor-plant.control-loop.compressor-1')).toEqual({
      samplePeriodMs: 500,
      pressureMaxBar: 8.4,
      alertChannel: alertBinding,
    })
    // Why did compressor 1 alert at 8.4 bar? The version history says who set the maximum, and when.
    expect(showcase.versions('default', 'exe.harbor-plant.control-loop.compressor-1')).toBe(3)
    const third = showcaseScope('harbor-industrial', 'plant-fleet', 'default')!
      .documents.get('configuration:exe.harbor-plant.control-loop.compressor-1')
      ?.versions.at(-1)
    expect(editors).toContain(third?.Modified.By?.Name)
    expect(Date.now() - Date.parse(third?.Modified.At ?? '')).toBeLessThan(30 * day)
    expect(third?.Content).toMatchObject({ pressureMaxBar: 8.4 })
    // The limit is local to the machine: compressor 2 is older and trips earlier.
    expect(showcase.own('default', 'exe.harbor-plant.control-loop.compressor-2')?.pressureMaxBar).toBeLessThan(8.4)
    // The safe defaults ship with the capability, tuned over a long history.
    expect(showcase.own('default', 'rst.control-loop')).toMatchObject({ samplePeriodMs: 1000, pressureMaxBar: 7 })
    expect(showcase.versions('default', 'rst.control-loop')).toBeGreaterThanOrEqual(15)
  })

  it('reads the alert channel of every assignment from the Values of its site', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    expect(showcase.annotations('default').get('sbt.harbor-plant')?.Values).toMatchObject({
      alertChannel: 'pagerduty:harbor-plant-shift',
    })
    // Both compressors and the gateway mention the channel once, through the binding.
    for (const key of [
      'exe.harbor-plant.control-loop.compressor-1',
      'exe.harbor-plant.control-loop.compressor-2',
      'exe.harbor-plant.telemetry.gateway',
    ]) {
      const binding = effectiveContent(db, scope, key).alertChannel
      expect(binding, key).toBe(alertBinding)
      expect(bound('default', key, binding), key).toEqual({
        ancestor: 'sbt.harbor-plant',
        value: 'pagerduty:harbor-plant-shift',
      })
    }
    // Every binding of the fleet to the Values of an ancestor finds its value, in every view.
    for (const view of views) {
      const { documents } = showcaseScope('harbor-industrial', 'plant-fleet', view)!
      const keys = showcase.keys(view)
      const configured = [...documents.keys()]
        .filter((id) => id.startsWith('configuration:'))
        .map((id) => id.slice('configuration:'.length))
        .filter((key) => keys.has(key))
      for (const key of configured) {
        for (const value of Object.values(showcase.own(view, key) ?? {})) {
          if (String(value).startsWith('@annotation(')) {
            expect(bound(view, key, value).value, `${view} ${key}`).toEqual(expect.any(String))
          }
        }
      }
    }
    // A firmware update reads the family of its gateway from the device.
    const update = 'uxe.north-dock.telemetry.gateway.firmware-update'
    expect(bound('default', update, showcase.own('default', update)?.family)).toEqual({
      ancestor: 'cnt.north-dock.gateway',
      value: 'norvik-edge-4',
    })
  })

  it('reads the broker of a telemetry assignment from its site, and the safe defaults from the template', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    const telemetry = effectiveContent(db, scope, 'exe.harbor-plant.telemetry.gateway')
    expect(telemetry.broker).toBe('@config("/network/mqttBroker")')
    expect(getAt(telemetry, ['network', 'mqttBroker'])).toBe('mqtts://mqtt.harbor-plant.harbor-industrial.example:8883')
    // An assignment without a document of its own still knows what to do when it loses its controller.
    const vibration = 'exe.west-quay.vibration-monitoring.compressor-a'
    expect(showcase.own('default', vibration)).toBeUndefined()
    expect(effectiveContent(db, scope, vibration)).toMatchObject({
      onCommunicationLoss: 'hold-safe-state',
      heartbeatSeconds: 30,
      standard: 'ISO 20816-3',
    })
  })

  it('switches off the control loop of compressor 2 while it is serviced, and keeps the device', () => {
    const annotations = showcase.annotations('default')
    expect(annotationStatus(annotations.get('exe.harbor-plant.control-loop.compressor-2')!)).toBe('disabled')
    expect(annotations.get('exe.harbor-plant.control-loop.compressor-2')?.Modified?.By).toMatchObject({
      Name: 'Ken Thompson',
    })
    expect(annotationStatus(annotations.get('cnt.harbor-plant.compressor-2')!)).toBe('active')
    expect(annotationStatus(annotations.get('exe.harbor-plant.control-loop.compressor-1')!)).toBe('active')
    expect(
      [...annotations.values()]
        .filter((annotation) => annotationStatus(annotation) === 'disabled')
        .map((annotation) => annotation.AnnotationKey),
    ).toEqual(['exe.harbor-plant.control-loop.compressor-2'])
  })

  it('requires the pressure limit on control-loop assignments and caps it', () => {
    expect(showcase.latest('default', 'schema:descendant:rst.control-loop:exe')).toMatchObject({
      required: ['pressureMaxBar'],
      properties: { pressureMaxBar: { maximum: 10 } },
    })
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    const schema = combinedSchema(db, scope, 'exe.harbor-plant.control-loop.compressor-1')?.MergedContent
    expect(schemaErrors(schema, { samplePeriodMs: 500, pressureMaxBar: 84, alertChannel: alertBinding })).toEqual([
      '#/pressureMaxBar: NumberTooBig',
    ])
    expect(schemaErrors(schema, { samplePeriodMs: 500 })).toEqual(['#/pressureMaxBar: PropertyRequired'])
    // Other capabilities are not under the schema.
    expect(combinedSchema(db, scope, 'exe.harbor-plant.telemetry.gateway')).toBeUndefined()
    // West Quay's Brenner vessels are rated 9.5 bar: the site's schema lowers the cap and keeps the required limit.
    const westQuay = combinedSchema(db, scope, 'exe.west-quay.control-loop.compressor-a')
    expect(westQuay?.AppliedSchemas.map((applied) => applied.AnnotationKey)).toEqual([
      'rst.control-loop',
      'sbt.west-quay',
    ])
    expect(schemaErrors(westQuay?.MergedContent, { pressureMaxBar: 9.8 })).toEqual(['#/pressureMaxBar: NumberTooBig'])
    expect(schemaErrors(westQuay?.MergedContent, {})).toEqual(['#/pressureMaxBar: PropertyRequired'])
    // Every control-loop assignment of the fleet names its limit.
    for (const key of startingWith(showcase.keys('default'), 'exe.')) {
      if (key.split('.')[2] === 'control-loop') {
        expect(showcase.own('default', key)?.pressureMaxBar, key).toEqual(expect.any(Number))
      }
    }
  })

  it('samples compressor 1 faster than its assignment while it is observed', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    const observed = 'uxe.harbor-plant.control-loop.compressor-1.sample'
    expect(showcase.own('default', observed)).toEqual({ samplePeriodMs: 250 })
    expect(effectiveContent(db, scope, observed)).toMatchObject({ samplePeriodMs: 250, pressureMaxBar: 8.4 })
    // Compressor 2 keeps the period of its assignment.
    const other = 'uxe.harbor-plant.control-loop.compressor-2.sample'
    expect(showcase.own('default', other)).toBeUndefined()
    expect(effectiveContent(db, scope, other)).toMatchObject({ samplePeriodMs: 500 })
  })

  it('records model, serial, install date and firmware family on every device', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('cnt.harbor-plant.compressor-1')?.Values).toEqual({
      model: 'Kestrel KX-37 VSD',
      serial: 'KX37-19-04412',
      installDate: '2019-03-18',
      firmwareFamily: 'kestrel-ctrl-5',
    })
    // The two compressors are not the same age.
    expect(String(annotations.get('cnt.harbor-plant.compressor-2')?.Values?.installDate) < '2019-03-18').toBe(true)
    for (const key of startingWith(annotations.keys(), 'cnt.')) {
      expect(Object.keys(annotations.get(key)?.Values ?? {}), key).toEqual(
        expect.arrayContaining(['model', 'serial', 'installDate', 'firmwareFamily']),
      )
      expect(showcase.own('default', key), key).toMatchObject({ address: expect.any(String) })
    }
    // A device keeps its facts in every view.
    expect(showcase.annotations('campaign-march').get('cnt.north-dock.compressor-a')?.Values).toEqual(
      annotations.get('cnt.north-dock.compressor-a')?.Values,
    )
  })

  it('follows the board in development: one test rig with telemetry and the control loop', () => {
    expect(sorted(showcase.keys('development'))).toEqual(sorted(boardDevelopment))
    // Developer values: a local broker, the sandbox upstream and debug logging on the assignments of the rig.
    expect(showcase.own('development', 'sbt.harbor-plant')).toMatchObject({
      network: { mqttBroker: 'mqtt://localhost:1883' },
    })
    const db = unseededShowcaseDb()
    const scope = showcase.scope('development')
    const telemetry = effectiveContent(db, scope, 'exe.harbor-plant.telemetry.test-rig')
    expect(telemetry).toMatchObject({ upstream: 'mqtts://mqtt.dev.harbor-industrial.example:8883', logLevel: 'debug' })
    expect(getAt(telemetry, ['network', 'mqttBroker'])).toBe('mqtt://localhost:1883')
    expect(effectiveContent(db, scope, 'exe.harbor-plant.control-loop.test-rig').logLevel).toBe('debug')
    const production = effectiveContent(db, showcase.scope('default'), 'exe.harbor-plant.control-loop.compressor-1')
    expect(production.logLevel).toBe('warn')
  })

  it('points development and staging at the services of their stage, never at production', () => {
    for (const [view, stage] of [
      ['development', 'dev'],
      ['staging', 'staging'],
    ] as const) {
      const { documents } = showcaseScope('harbor-industrial', 'plant-fleet', view)!
      const ofStage = new RegExp(String.raw`^[\w-]+\.${stage}\.harbor-industrial\.example$`)
      for (const [id, document] of documents) {
        const hosts = JSON.stringify(document.versions.at(-1)?.Content ?? {}).match(
          /[\w.-]+\.harbor-industrial\.example/g,
        )
        for (const host of hosts ?? []) {
          expect(host, `${view} ${id}`).toMatch(ofStage)
        }
      }
    }
    expect(showcase.own('staging', 'usg.integration-lab.telemetry')).toEqual({
      historian: 'https://historian.staging.harbor-industrial.example/sites/integration-lab',
      retentionDays: 30,
    })
  })

  it('copies production in campaign-march, with a faster sample on the observed compressors', () => {
    expect(sorted(showcase.keys('campaign-march'))).toEqual(sorted(showcase.keys('default')))
    const campaign = showcaseScope('harbor-industrial', 'plant-fleet', 'campaign-march')!
    const changed: string[] = []
    for (const id of campaign.documents.keys()) {
      const before = showcase.latest('default', id)
      const after = showcase.latest('campaign-march', id)
      if (JSON.stringify(before) !== JSON.stringify(after)) {
        changed.push(id)
        // Diffing a job run against default shows the single number that changes.
        expect(before, id).toEqual({ samplePeriodMs: 250 })
        expect(after, id).toEqual({ samplePeriodMs: 100 })
      }
    }
    // Compressor 1 of the documentation and both observed compressors of West Quay.
    expect(sorted(changed)).toEqual([
      'configuration:uxe.harbor-plant.control-loop.compressor-1.sample',
      'configuration:uxe.west-quay.control-loop.compressor-a.sample',
      'configuration:uxe.west-quay.control-loop.compressor-b.sample',
    ])
    for (const id of changed) {
      const [, site, , device] = id.slice('configuration:'.length).split('.')
      expect(showcase.annotations('default').get(`cnt.${site}.${device}`)?.Labels, id).toContain('observed')
    }
  })

  it('brings leak detection, gateway firmware 4.3 and a lower pressure cap in the release candidate', () => {
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.control-loop:exe')).toMatchObject({
      required: ['pressureMaxBar', 'alertChannel'],
      properties: { pressureMaxBar: { maximum: 9.5 } },
    })
    expect(showcase.versions('release-candidate', 'rst.control-loop')).toBe(
      showcase.versions('default', 'rst.control-loop') + 1,
    )
    const update = 'uxe.west-quay.telemetry.gateway.firmware-update'
    expect(showcase.own('default', update)?.targetVersion).toBe('4.2.1')
    expect(showcase.own('release-candidate', update)?.targetVersion).toBe('4.3.0')
    expect(showcase.keys('release-candidate').has('exe.harbor-plant.leak-detection.compressor-1')).toBe(true)
    expect(showcase.keys('release-candidate').has('exe.north-dock.leak-detection.compressor-c')).toBe(true)
    expect(showcase.keys('default').has('rst.leak-detection')).toBe(false)
    // The capability is new: created for the release by a member who may edit.
    const leak = showcase.annotations('release-candidate').get('rst.leak-detection')!
    expect(Date.now() - Date.parse(leak.Created!.At!)).toBeLessThan(30 * day)
    expect(editors).toContain(leak.Created?.By?.Name)
  })

  it('delivers predictive maintenance to the Harbor Plant compressors in feature-delivery1', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(keys.has('unt.predictive-maintenance.anomaly-scoring')).toBe(true)
    expect(keys.has('uxe.harbor-plant.predictive-maintenance.compressor-1.anomaly-scoring')).toBe(true)
    expect(startingWith(keys, 'sbt.')).toEqual(['sbt.harbor-plant'])
    expect(keys.has('rst.maintenance')).toBe(false)
    expect(showcase.keys('default').has('rst.predictive-maintenance')).toBe(false)
    const feature = showcase.annotations('feature-delivery1')
    expect(Date.now() - Date.parse(feature.get('rst.predictive-maintenance')!.Created!.At!)).toBeLessThan(30 * day)
    // The pilot waits for compressor 2 to come back from service.
    expect(annotationStatus(feature.get('exe.harbor-plant.predictive-maintenance.compressor-2')!)).toBe('disabled')
  })

  it('starts with a decommissioned boiler, a scheduled bottling line and a rental chiller that goes back', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('cnt.river-mill.steam-boiler-b')).toBe('expired')
    expect(status('exe.river-mill.control-loop.steam-boiler-b')).toBe('expired')
    expect(status('cnt.river-mill.steam-boiler-c')).toBe('active')
    for (const device of ['bottling-compressor', 'bottling-chiller', 'bottling-conveyor']) {
      expect(status(`cnt.east-yard.${device}`), device).toBe('scheduled')
    }
    expect(status('exe.east-yard.control-loop.bottling-compressor')).toBe('scheduled')
    expect(status('cnt.west-quay.rental-chiller')).toBe('active')
    expect(Date.parse(annotations.get('cnt.west-quay.rental-chiller')!.ExpiresAt!)).toBeGreaterThan(Date.now())
  })

  it('starts with a deleted site extension and its devices, and a camera assignment deleted alone', () => {
    const trash = showcase.trash('default')
    expect(trash.get('sbt.harbor-plant-annex')).toEqual(
      expect.arrayContaining([
        'cnt.harbor-plant-annex.rental-compressor',
        'usg.harbor-plant-annex.control-loop',
        'exe.harbor-plant-annex.control-loop.rental-compressor',
        'uxe.harbor-plant-annex.control-loop.rental-compressor.sample',
      ]),
    )
    expect(trash.get('sbt.harbor-plant-annex')?.at(-1)).toBe('sbt.harbor-plant-annex')
    expect(trash.get('exe.north-dock.video.yard-camera')).toEqual(['exe.north-dock.video.yard-camera'])
    // The camera stays in the catalog as a spare; the annex is gone from every other view.
    expect(showcase.keys('default').has('cnt.north-dock.yard-camera')).toBe(true)
    expect(showcase.keys('release-candidate').has('sbt.harbor-plant-annex')).toBe(false)
  })

  it('changes no document before its annotation was created, nor after it was deleted or expired', () => {
    const now = new Date().toISOString()
    for (const view of views) {
      const { annotations, documents, trash } = showcaseScope('harbor-industrial', 'plant-fleet', view)!
      // Every annotation with the moment its documents were frozen: now, or when it was deleted.
      const frozen = [
        ...annotations.map((active) => [active, now] as const),
        ...trash.flatMap((tree) => tree.annotations.map((deleted) => [deleted, tree.deletedAt] as const)),
      ]
      for (const [annotation, until] of frozen) {
        const key = annotation.AnnotationKey
        const end = [until, ...(annotation.ExpiresAt ? [annotation.ExpiresAt] : [])].toSorted()[0]!
        for (const version of documents.get(`configuration:${key}`)?.versions ?? []) {
          const at = version.Modified.At ?? ''
          expect(at > annotation.Created!.At! && at < end, `${view} ${key} ${at}`).toBe(true)
        }
      }
    }
  })

  it('runs telemetry on the gateways only, and leaves out what a view does not run', () => {
    const keys = showcase.keys('default')
    for (const key of startingWith(keys, 'exe.')) {
      const [, site, capability, device] = key.split('.')
      if (capability === 'telemetry') {
        expect(showcase.annotations('default').get(`cnt.${site}.${device}`)?.Labels, key).toContain('gateway')
      }
    }
    // Compressor 2 has no maintenance window, and Harbor Plant films nothing.
    expect(keys.has('exe.harbor-plant.maintenance.compressor-2')).toBe(false)
    expect(keys.has('usg.harbor-plant.video')).toBe(false)
    // Development has no maintenance, and none of the production devices.
    const development = showcase.keys('development')
    expect(startingWith(development, 'usg.').some((key) => key.endsWith('.maintenance'))).toBe(false)
    expect(development.has('cnt.harbor-plant.compressor-1')).toBe(false)
  })
})
