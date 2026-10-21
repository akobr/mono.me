import { describe, expect, it } from 'vitest'
import { combinedSchema, effectiveContent, schemaErrors } from '@/mock/configuration-handlers'
import { showcaseScope } from '@/mock/showcase'
import { vehiclePlatform } from '@/mock/showcase/projects/vehicle-platform'
import { getAllAncestors } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// aurora-motors/vehicle-platform follows docs/42for.net/platform/examples/physical-product.md and example 7 of the
// Miro board.

const showcase = showcaseOf('aurora-motors', 'vehicle-platform')

const board = {
  default: [
    'sbt.aurora',
    'usg.aurora.battery-pack',
    'exe.aurora.battery-pack.touring',
    'exe.aurora.battery-pack.gt',
    'exe.aurora.battery-pack.vin-4F2',
    'usg.aurora.infotainment',
    'exe.aurora.infotainment.touring',
    'exe.aurora.infotainment.gt',
    'usg.aurora.adas',
    'exe.aurora.adas.gt',
    'unt.infotainment.firmware',
    'uxe.aurora.infotainment.touring.firmware',
    'uxe.aurora.infotainment.gt.firmware',
    'rst.battery-pack',
    'rst.infotainment',
    'rst.adas',
    'rst.brakes',
    'cnt.aurora.touring',
    'cnt.aurora.vin-4F2',
    'cnt.aurora.gt',
  ],
  'feature-delivery1': [
    'sbt.aurora',
    'usg.aurora.infotainment',
    'exe.aurora.infotainment.touring',
    'usg.aurora.adas',
    'exe.aurora.adas.touring',
    'unt.infotainment.firmware',
    'uxe.aurora.infotainment.touring.firmware',
    'rst.infotainment',
    'rst.adas',
    'cnt.aurora.touring',
  ],
}

const sorted = (keys: Iterable<string>) => [...keys].toSorted()
const startingWith = (keys: Set<string>, prefix: string) => sorted([...keys].filter((key) => key.startsWith(prefix)))
const executionsOf = (keys: Set<string>, ancestor: string) =>
  sorted([...keys].filter((key) => key.startsWith('exe.') && getAllAncestors(key).includes(ancestor)))

describe('aurora-motors/vehicle-platform', () => {
  it('has the catalog of the documentation and of the board', () => {
    const keys = showcase.keys('default')
    for (const key of board.default) {
      expect(keys.has(key), key).toBe(true)
    }
  })

  it('keeps the Aurora as drawn: three builds, three fitments, six fitted parts and two firmware pins', () => {
    const keys = showcase.keys('default')
    const aurora = board.default.filter((key) => key.split('.')[1] === 'aurora')
    for (const code of ['cnt', 'usg', 'exe', 'uxe']) {
      expect(startingWith(keys, `${code}.aurora.`), code).toEqual(sorted(aurora.filter((key) => key.startsWith(code))))
    }
    // ADAS is part of the GT recipe and not of the Touring.
    expect(executionsOf(keys, 'rst.adas').filter((key) => key.startsWith('exe.aurora.'))).toEqual([
      'exe.aurora.adas.gt',
    ])
  })

  it('extends the catalog with the Borealis, the Zephyr, five more subsystems and two more firmware units', () => {
    const keys = showcase.keys('default')
    for (const key of ['rst.thermal', 'rst.charging', 'rst.suspension', 'rst.lighting', 'rst.seats']) {
      expect(keys.has(key), key).toBe(true)
    }
    for (const key of ['unt.charging.firmware', 'unt.adas.perception-model']) {
      expect(showcase.annotations('default').get(key), key).toMatchObject({ UnitType: 'file' })
    }
    for (const model of ['borealis', 'zephyr']) {
      expect(keys.has(`sbt.${model}`), model).toBe(true)
      const contexts = startingWith(keys, `cnt.${model}.`)
      expect(contexts.filter((key) => !key.includes('.vin-')).length, model).toBeGreaterThanOrEqual(2)
      expect(contexts.filter((key) => key.includes('.vin-')).length, model).toBeGreaterThanOrEqual(1)
    }
  })

  it('registers every car in a market its battery pack is homologated for', () => {
    const homologationOf = (market: string) => (['se', 'dk', 'de', 'nl'].includes(market) ? 'eu' : market)
    const cars = [...showcase.annotations('default')].filter(([key]) => key.startsWith('cnt.') && key.includes('.vin-'))
    expect(cars.length).toBeGreaterThanOrEqual(25)
    for (const [key, car] of cars) {
      const { trim, market } = car.Values as { trim: string; market: string }
      const pack = showcase.own('default', `exe.${key.split('.')[1]}.battery-pack.${trim}`)
      expect(pack?.homologation, key).toContain(homologationOf(market))
    }
  })

  it('dates every configuration after its annotation was created, and before it was deleted or expired', () => {
    for (const { name: view } of vehiclePlatform.views) {
      const { annotations, documents, trash } = showcaseScope('aurora-motors', 'vehicle-platform', view)!
      const ends = new Map<string, number>()
      for (const annotation of annotations) {
        const expired = Date.parse(annotation.ExpiresAt ?? '')
        if (expired < Date.now()) {
          ends.set(annotation.AnnotationKey, expired)
        }
      }
      for (const tree of trash) {
        tree.annotations.forEach((annotation) => ends.set(annotation.AnnotationKey, Date.parse(tree.deletedAt)))
      }
      const created = new Map(
        [...annotations, ...trash.flatMap((tree) => tree.annotations)].map((annotation) => [
          annotation.AnnotationKey,
          Date.parse(annotation.Created?.At ?? ''),
        ]),
      )
      for (const [id, document] of documents) {
        if (!id.startsWith('configuration:')) {
          continue
        }
        const key = id.slice('configuration:'.length)
        const written = document.versions.map((version) => Date.parse(version.Modified.At ?? ''))
        expect(written[0], `${view} ${key}`).toBeGreaterThanOrEqual(created.get(key)!)
        expect(written.at(-1), `${view} ${key}`).toBeLessThanOrEqual(ends.get(key) ?? Date.now())
      }
    }
  })

  it('has brakes in the catalog without a fitment or a fitted part', () => {
    const keys = showcase.keys('default')
    expect(keys.has('rst.brakes')).toBe(true)
    expect([...keys].filter((key) => getAllAncestors(key).includes('rst.brakes'))).toEqual([])
  })

  it('stores the GT pack of the documentation and rejects a pack the factory cannot build', () => {
    expect(showcase.own('default', 'exe.aurora.battery-pack.gt')).toEqual({
      chemistry: 'nmc',
      capacityKwh: 91,
      supplier: 'helios',
      homologation: ['eu', 'uk'],
    })
    const schema = showcase.latest('default', 'schema:descendant:rst.battery-pack:exe') as {
      required: string[]
      properties: { chemistry: { enum: string[] } }
    }
    expect(schema.required).toEqual(['chemistry', 'capacityKwh'])
    expect(schema.properties.chemistry.enum).toEqual(showcase.own('default', 'rst.battery-pack')?.allowedChemistries)

    const merged = combinedSchema(unseededShowcaseDb(), showcase.scope('default'), 'exe.zephyr.battery-pack.city')
    expect(schemaErrors(merged?.MergedContent, { chemistry: 'sodium-ion', capacityKwh: 60 })).toEqual([
      '#/chemistry: NotInEnumeration',
    ])
    expect(schemaErrors(merged?.MergedContent, { chemistry: 'nmc' })).toEqual(['#/capacityKwh: PropertyRequired'])
  })

  it('records VIN 4F2 as a sibling of the trims, with its complete battery record and the replacement', () => {
    const key = 'exe.aurora.battery-pack.vin-4F2'
    expect(sorted(getAllAncestors(key))).toEqual([
      'cnt.aurora.vin-4F2',
      'rst.battery-pack',
      'sbt.aurora',
      'usg.aurora.battery-pack',
    ])
    expect(showcase.own('default', key)).toMatchObject({
      chemistry: 'nmc',
      capacityKwh: 91,
      supplier: 'helios',
      replacement: { partNumber: 'BPM-91N-HX-04', replacedOn: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) },
    })
    // What changed on VIN 4F2: the history of its own execution; the GT document did not change.
    expect(showcase.versions('default', key)).toBe(2)
    expect(showcase.versions('default', 'exe.aurora.battery-pack.gt')).toBe(1)
    expect(showcase.annotations('default').get('cnt.aurora.vin-4F2')?.Values).toMatchObject({
      vin: expect.stringMatching(/^[A-HJ-NPR-Z0-9]{14}4F2$/),
      trim: 'gt',
    })
  })

  it('ships the infotainment firmware as a file, pins GT to stable and lets Touring follow its build', () => {
    expect(showcase.annotations('default').get('unt.infotainment.firmware')).toMatchObject({
      UnitType: 'file',
      UnitDefinition: expect.stringMatching(/\/ivi-image\.swu$/),
    })
    const gt = 'uxe.aurora.infotainment.gt.firmware'
    expect(showcase.own('default', gt)).toMatchObject({ channel: 'stable' })
    expect(showcase.versions('default', gt)).toBeGreaterThanOrEqual(15)

    const touring = 'uxe.aurora.infotainment.touring.firmware'
    expect(showcase.own('default', touring)).toEqual({ channel: '@config("/defaultFirmwareChannel")' })
    // The binding reads the build's channel; the template adds the install rules every pin inherits.
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), touring)).toMatchObject({
      defaultFirmwareChannel: 'stable',
      requireParked: true,
    })
    expect(showcase.latest('default', 'template:uxe')).toMatchObject({ channel: 'stable', autoInstall: true })
  })

  it('answers the questions of the documentation', () => {
    const keys = showcase.keys('default')
    // What is on a GT? Every execution of its build: the battery pack, ADAS and infotainment with its firmware pin.
    expect(executionsOf(keys, 'cnt.aurora.gt')).toEqual([
      'exe.aurora.adas.gt',
      'exe.aurora.battery-pack.gt',
      'exe.aurora.infotainment.gt',
    ])
    // Which builds use the Helios pack? Executions of the battery pack whose document names that supplier.
    const helios = executionsOf(keys, 'rst.battery-pack').filter(
      (key) => showcase.own('default', key)?.supplier === 'helios',
    )
    expect(helios).toEqual(
      expect.arrayContaining([
        'exe.aurora.battery-pack.gt',
        'exe.aurora.battery-pack.vin-4F2',
        'exe.borealis.battery-pack.summit',
      ]),
    )
    expect(helios).not.toContain('exe.aurora.battery-pack.touring')
    // A recall walks the executions of the subsystem; the repaired cars are VIN contexts naming the replacement.
    const repaired = executionsOf(keys, 'rst.charging').filter((key) => key.includes('.vin-'))
    expect(repaired.length).toBeGreaterThanOrEqual(10)
    for (const key of repaired) {
      expect(showcase.own('default', key), key).toMatchObject({
        charger: 'obc-11-b',
        recall: { campaign: 'AM-R-2026-07', vin: '@annotation("/vin", "Context")' },
      })
      const context = getAllAncestors(key).find((ancestor) => ancestor.startsWith('cnt.'))!
      expect(showcase.annotations('default').get(context)?.Values?.vin, context).toHaveLength(17)
    }
    expect(showcase.own('default', 'exe.zephyr.charging.city')).toMatchObject({ charger: 'obc-11-b' })
    expect(showcase.versions('default', 'exe.zephyr.charging.city')).toBe(2)
  })

  it('lets a canary car pin beta without changing its build', () => {
    expect(showcase.own('default', 'uxe.borealis.infotainment.vin-8c1.firmware')).toEqual({ channel: 'beta' })
    expect(showcase.own('default', 'cnt.borealis.summit')).toMatchObject({ defaultFirmwareChannel: 'stable' })
  })

  it('shows a scheduled model-year trim, an expired homologation, a planned end and a disabled firmware pin', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    expect(status('cnt.zephyr.sport')).toBe('scheduled')
    expect(status('exe.zephyr.battery-pack.sport')).toBe('scheduled')
    expect(status('cnt.borealis.us-homologation')).toBe('expired')
    expect(status('cnt.zephyr.launch-edition')).toBe('active')
    // The order book of the launch edition closes; the cars delivered keep their parts and firmware pins.
    expect(annotations.get('cnt.zephyr.launch-edition')?.ExpiresAt).toBeDefined()
    expect(annotations.get('exe.zephyr.battery-pack.launch-edition')?.ExpiresAt).toBeUndefined()
    expect(annotations.get('uxe.zephyr.charging.launch-edition.firmware')?.ExpiresAt).toBeUndefined()
    expect(status('uxe.borealis.adas.summit.perception-model')).toBe('disabled')
    expect(showcase.own('default', 'uxe.borealis.adas.summit.perception-model')).toEqual({ version: 'pm-2026.32' })
    // The homologation fleet ran the beta firmware of its build.
    const pin = 'uxe.borealis.charging.us-homologation.firmware'
    expect(showcase.own('default', pin)).toEqual({ channel: '@config("/defaultFirmwareChannel")' })
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), pin)).toMatchObject({
      defaultFirmwareChannel: 'beta',
    })
  })

  it('starts with the withdrawn tow hitch deleted with its fitment and parts, and a VIN context deleted alone', () => {
    expect(showcase.trash('default').get('rst.tow-hitch')).toEqual([
      'exe.borealis.tow-hitch.explorer',
      'exe.borealis.tow-hitch.trail',
      'usg.borealis.tow-hitch',
      'rst.tow-hitch',
    ])
    expect(showcase.trash('default').get('cnt.zephyr.vin-5s2')).toEqual(['cnt.zephyr.vin-5s2'])
  })

  it('follows the board in feature-delivery1: the Touring build with ADAS', () => {
    expect(sorted(showcase.keys('feature-delivery1'))).toEqual(sorted(board['feature-delivery1']))
    expect(showcase.own('feature-delivery1', 'exe.aurora.adas.touring')).toMatchObject({
      features: { adaptiveCruise: true, laneCentering: true },
    })
    expect(showcase.keys('default').has('exe.aurora.adas.touring')).toBe(false)
  })

  it('brings the digital key, the next GT firmware and a stricter battery schema in the release candidate', () => {
    const keys = showcase.keys('release-candidate')
    expect(keys.has('exe.borealis.digital-key.summit')).toBe(true)
    expect(showcase.keys('default').has('rst.digital-key')).toBe(false)
    expect(showcase.own('release-candidate', 'uxe.aurora.infotainment.gt.firmware')).toEqual(
      expect.objectContaining({ channel: 'stable', version: '2026.42.0' }),
    )
    expect(showcase.versions('release-candidate', 'uxe.aurora.infotainment.gt.firmware')).toBe(
      showcase.versions('default', 'uxe.aurora.infotainment.gt.firmware') + 1,
    )
    expect(showcase.latest('release-candidate', 'schema:descendant:rst.battery-pack:exe')).toMatchObject({
      required: ['chemistry', 'capacityKwh', 'supplier'],
    })
    // A close copy of production otherwise, without the deleted annotations.
    expect(executionsOf(keys, 'rst.charging').length).toBe(
      executionsOf(showcase.keys('default'), 'rst.charging').length,
    )
    expect(keys.has('rst.tow-hitch')).toBe(false)
  })

  it('runs development on test rigs, with debug logging, the sandbox OTA server and nightly firmware', () => {
    const keys = showcase.keys('development')
    expect(startingWith(keys, 'sbt.')).toEqual(['sbt.aurora'])
    expect(startingWith(keys, 'cnt.')).toEqual([
      'cnt.aurora.hil-bench',
      'cnt.aurora.sil-cluster',
      'cnt.aurora.test-mule',
    ])
    expect(showcase.own('development', 'rst.infotainment')?.otaServer).toBe('https://ota.sandbox.aurora-motors.example')
    expect(showcase.own('development', 'exe.aurora.infotainment.test-mule')).toMatchObject({ logLevel: 'debug' })
    expect(showcase.latest('development', 'template:uxe')).toMatchObject({ channel: 'nightly' })
  })

  it('tests the beta channel on the staging fleet', () => {
    const keys = showcase.keys('staging')
    expect(startingWith(keys, 'sbt.')).toEqual(['sbt.aurora', 'sbt.zephyr'])
    expect(showcase.own('staging', 'cnt.aurora.gt')).toMatchObject({ defaultFirmwareChannel: 'beta' })
    const fleet = [...keys].filter((key) => key.startsWith('cnt.aurora.vin-'))
    expect(fleet.length).toBeGreaterThanOrEqual(5)
    for (const context of fleet) {
      expect(showcase.own('staging', `uxe.aurora.infotainment.${context.slice(11)}.firmware`)).toMatchObject({
        channel: 'beta',
      })
    }
  })
})
