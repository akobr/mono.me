import { describe, expect, it } from 'vitest'
import { effectiveContent } from '@/mock/configuration-handlers'
import { showcaseScope } from '@/mock/showcase'
import { sentenceTitle } from '@/mock/showcase/builder'
import { clinicalIntegrations } from '@/mock/showcase/projects/clinical-integrations'
import { getAllAncestors } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { ancestorKeyOf } from '@/utils/binding-language'
import { showcaseOf, unseededShowcaseDb } from './support'

// meridian-health/clinical-integrations is example 9 of the Miro board: St. Anne as the board draws it, extended to a
// network of three hospitals (spec section 9.9).

const showcase = showcaseOf('meridian-health', 'clinical-integrations')
const views = clinicalIntegrations.views.map((view) => view.name)
const ofType = (keys: Set<string>, prefix: string) => [...keys].filter((key) => key.startsWith(prefix)).toSorted()
const scope = (view: string) => showcaseScope('meridian-health', 'clinical-integrations', view)!
const routerFeeds = [
  'uxe.st-anne.imaging.radiology.dicom-router',
  'uxe.riverside.imaging.radiology.dicom-router',
  'uxe.northgate.imaging.radiology.dicom-router',
]

/** Every string in a document. */
function strings(value: unknown): string[] {
  if (typeof value === 'string') {
    return [value]
  }
  return value && typeof value === 'object' ? Object.values(value).flatMap(strings) : []
}

describe('meridian-health/clinical-integrations', () => {
  it('has the catalog of the board', () => {
    const keys = showcase.keys('default')
    for (const key of [
      'sbt.st-anne',
      'rst.lab-results',
      'rst.imaging',
      'rst.e-prescription',
      'cnt.st-anne.emergency',
      'cnt.st-anne.cardiology',
      'cnt.st-anne.oncology',
      'usg.st-anne.lab-results',
      'usg.st-anne.imaging',
      'usg.st-anne.e-prescription',
      'exe.st-anne.lab-results.emergency',
      'exe.st-anne.lab-results.cardiology',
      'exe.st-anne.imaging.emergency',
      'exe.st-anne.imaging.oncology',
      'exe.st-anne.e-prescription.cardiology',
      'exe.st-anne.e-prescription.oncology',
      'unt.lab-results.hl7-feed',
      'uxe.st-anne.lab-results.emergency.hl7-feed',
      'uxe.st-anne.lab-results.cardiology.hl7-feed',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    // The board's departments integrate exactly the systems it draws, and only lab results has feeds there.
    const board = ['emergency', 'cardiology', 'oncology']
    const inBoard = (prefix: string) =>
      ofType(keys, prefix).filter((key) => board.includes(key.split('.')[3]!) && key.split('.')[1] === 'st-anne')
    expect(inBoard('exe.')).toEqual([
      'exe.st-anne.e-prescription.cardiology',
      'exe.st-anne.e-prescription.oncology',
      'exe.st-anne.imaging.emergency',
      'exe.st-anne.imaging.oncology',
      'exe.st-anne.lab-results.cardiology',
      'exe.st-anne.lab-results.emergency',
    ])
    expect(inBoard('uxe.')).toEqual([
      'uxe.st-anne.lab-results.cardiology.hl7-feed',
      'uxe.st-anne.lab-results.emergency.hl7-feed',
    ])
  })

  it('extends the board to a network of hospitals, clinical systems and interfaces', () => {
    const keys = showcase.keys('default')
    expect(ofType(keys, 'sbt.')).toEqual(['sbt.northgate', 'sbt.riverside', 'sbt.st-anne'])
    expect(ofType(keys, 'rst.')).toEqual([
      'rst.billing-claims',
      'rst.e-prescription',
      'rst.imaging',
      'rst.lab-results',
      'rst.patient-portal',
    ])
    for (const key of [
      'unt.imaging.dicom-router',
      'unt.patient-portal.fhir-export',
      'unt.billing-claims.claims-batch',
      'unt.e-prescription.renewal-requests',
      'cnt.riverside.maternity',
      'cnt.northgate.outpatient-clinic',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    for (const department of ['emergency', 'cardiology', 'oncology', 'radiology', 'pediatrics']) {
      expect(
        [...keys].some((key) => key.startsWith('cnt.') && key.endsWith(`.${department}`)),
        department,
      ).toBe(true)
    }
    // Riverside has no patient portal: no license, so no integration and no feed of it.
    expect([...keys].filter((key) => /^(usg|exe|uxe)\.riverside\.patient-portal\b/.test(key))).toEqual([])
  })

  it('requires an endpoint and a vault certificate on every integration that stores a document', () => {
    expect(showcase.latest('default', 'schema:type:exe')).toMatchObject({ required: ['endpoint', 'certificateRef'] })
    expect(showcase.own('default', 'exe.st-anne.lab-results.emergency')).toMatchObject({
      endpoint: 'mllp://lis.st-anne.meridian-health.example:2577',
      certificateRef: '@(certificates.st-anne-lab, primaryVault)',
      sendingFacility: '@annotation("/facilityCode", "Subject")',
      priority: 'stat',
      criticalCallbackMinutes: 15,
    })
    for (const view of views) {
      for (const key of ofType(showcase.keys(view), 'exe.')) {
        const own = showcase.own(view, key)
        if (own) {
          expect(own, `${view} ${key}`).toHaveProperty('endpoint')
          expect(own.certificateRef, `${view} ${key}`).toMatch(/^@\(certificates\.[a-z-]+, primaryVault\)$/)
        }
      }
    }
    // An integration without a document of its own inherits the connection of the hospital's license.
    const inherited = 'exe.st-anne.e-prescription.cardiology'
    expect(showcase.own('default', inherited)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), inherited)).toMatchObject({
      endpoint: 'https://hub.rxlink.example/ncpdp/script/2017071',
      certificateRef: '@(certificates.st-anne-erx, primaryVault)',
      pdmpCheck: 'required',
    })
  })

  it('gives the feeds an endpoint, message types and an acknowledgment mode over the HL7 defaults', () => {
    // Enhanced mode values MSH-15 and MSH-16; the template's original mode leaves them empty.
    expect(showcase.own('default', 'uxe.st-anne.lab-results.emergency.hl7-feed')).toMatchObject({
      endpoint: expect.stringMatching(/^mllp:\/\/engine\.st-anne\./),
      messageTypes: ['ORU^R01', 'ORU^R30'],
      ackMode: 'enhanced',
      acknowledgment: { accept: 'AL', application: 'ER' },
    })
    expect(showcase.latest('default', 'schema:descendant:unt.lab-results.hl7-feed:uxe')).toMatchObject({
      required: ['endpoint', 'messageTypes'],
    })
    const template = showcase.latest('default', 'template:uxe')!
    expect(template).toMatchObject({ ackMode: 'original', retry: { attempts: 5, deadLetter: true } })
    expect(template.acknowledgment).toEqual({ timeoutSeconds: 30 })
    for (const view of views) {
      for (const key of ofType(showcase.keys(view), 'uxe.').filter((name) => name.endsWith('.hl7-feed'))) {
        const own = showcase.own(view, key)!
        expect('acknowledgment' in own, `${view} ${key}`).toBe(own.ackMode === 'enhanced')
      }
    }
    // A feed that keeps its interface's defaults has no document and inherits the template.
    const router = 'uxe.northgate.imaging.radiology.dicom-router'
    expect(showcase.own('default', router)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), router)).toMatchObject({
      routerVersion: '4.8.2',
      retry: { attempts: 5, deadLetter: true },
    })
  })

  it('binds only to values the hospital or the department has', () => {
    for (const view of views) {
      const annotations = showcase.annotations(view)
      for (const key of annotations.keys()) {
        for (const text of strings(showcase.own(view, key))) {
          const binding = /^@annotation\("\/(\w+)", "(\w+)"\)$/.exec(text)
          if (binding) {
            const ancestor = annotations.get(ancestorKeyOf(key, binding[2]!) ?? '')
            expect(ancestor?.Values, `${view} ${key} ${text}`).toHaveProperty(binding[1]!)
          }
        }
      }
    }
    expect(showcase.annotations('default').get('sbt.st-anne')?.Values).toMatchObject({ facilityCode: 'STANNE' })
    expect(showcase.annotations('default').get('cnt.st-anne.emergency')?.Values).toMatchObject({ locationCode: 'ED' })
  })

  it('keeps the tuning of the lab defaults as the long history', () => {
    expect(showcase.versions('default', 'rst.lab-results')).toBe(18)
    expect(showcase.own('default', 'rst.lab-results')).toMatchObject({
      loincRelease: '2.81',
      criticalCallbackMinutes: 15,
      resultStatuses: ['P', 'F', 'C'],
    })
  })

  it('follows the board in staging: the St. Anne test ward with lab results and e-prescription', () => {
    expect([...showcase.keys('staging')].toSorted()).toEqual([
      'cnt.st-anne.test-ward',
      'exe.st-anne.e-prescription.test-ward',
      'exe.st-anne.lab-results.test-ward',
      'rst.e-prescription',
      'rst.lab-results',
      'sbt.st-anne',
      'unt.lab-results.hl7-feed',
      'usg.st-anne.e-prescription',
      'usg.st-anne.lab-results',
      'uxe.st-anne.lab-results.test-ward.hl7-feed',
    ])
    expect(showcase.own('staging', 'uxe.st-anne.lab-results.test-ward.hl7-feed')).toMatchObject({ processingId: 'T' })
  })

  it('shows a scheduled integration, an expired interim feed and an interface disabled for its upgrade', () => {
    const annotations = showcase.annotations('default')
    expect(annotationStatus(annotations.get('exe.riverside.e-prescription.cardiology')!)).toBe('scheduled')
    expect(annotationStatus(annotations.get('uxe.riverside.lab-results.orthopedics.hl7-feed')!)).toBe('expired')
    expect(annotationStatus(annotations.get('unt.imaging.dicom-router')!)).toBe('disabled')
    // The Clearview contract of Riverside is active, with its planned end.
    const contract = annotations.get('usg.riverside.imaging')!
    expect(annotationStatus(contract)).toBe('active')
    expect(contract.ExpiresAt).toBeDefined()
  })

  it('pauses the router’s feeds with the router, and runs them again with router 5.2 in the release candidate', () => {
    const production = showcase.annotations('default')
    const candidate = showcase.annotations('release-candidate')
    for (const key of routerFeeds) {
      expect(annotationStatus(production.get(key)!), key).toBe('disabled')
      expect(annotationStatus(candidate.get(key)!), key).toBe('active')
    }
  })

  it('keeps every ancestor of an active annotation active', () => {
    for (const view of views) {
      const annotations = showcase.annotations(view)
      for (const [key, annotation] of annotations) {
        if (annotationStatus(annotation) === 'active') {
          for (const ancestor of getAllAncestors(key)) {
            expect(annotationStatus(annotations.get(ancestor)!), `${view} ${key} under ${ancestor}`).toBe('active')
          }
        }
      }
    }
  })

  it('creates every annotation after its ancestors, and writes its configuration after it', () => {
    for (const view of views) {
      const { annotations, trash, documents } = scope(view)
      const all = new Map(
        [...annotations, ...trash.flatMap((tree) => tree.annotations)].map((annotation) => [
          annotation.AnnotationKey,
          annotation,
        ]),
      )
      for (const [key, annotation] of all) {
        const created = Date.parse(annotation.Created!.At!)
        for (const ancestor of getAllAncestors(key)) {
          expect(Date.parse(all.get(ancestor)!.Created!.At!), `${view} ${key} after ${ancestor}`).toBeLessThanOrEqual(
            created,
          )
        }
        const first = documents.get(`configuration:${key}`)?.versions[0]
        if (first) {
          expect(Date.parse(first.Modified!.At!), `${view} ${key}`).toBeGreaterThanOrEqual(created)
        }
      }
    }
  })

  it('writes nothing to a deleted tree after its deletion', () => {
    const { trash, documents } = scope('default')
    expect(trash.length).toBeGreaterThan(0)
    for (const tree of trash) {
      const deletedAt = Date.parse(tree.deletedAt)
      for (const annotation of tree.annotations) {
        const key = annotation.AnnotationKey
        expect(Date.parse(annotation.Modified!.At!), key).toBeLessThan(deletedAt)
        const newest = documents.get(`configuration:${key}`)?.versions.at(-1)
        if (newest) {
          expect(Date.parse(newest.Modified!.At!), key).toBeLessThan(deletedAt)
          // A deleted configuration keeps its content, so Deleted and a restore show it.
          expect(newest.Content, key).toBeDefined()
        }
      }
    }
  })

  it('gives every key the same audit dates and authors in production and its copies', () => {
    const production = showcase.annotations('default')
    for (const view of ['release-candidate', 'feature-delivery1']) {
      for (const [key, annotation] of showcase.annotations(view)) {
        const original = production.get(key)
        if (original) {
          expect(annotation.Created?.By, `${view} ${key}`).toEqual(original.Created?.By)
          // The views are built a few milliseconds apart.
          expect(
            Math.abs(Date.parse(annotation.Created!.At!) - Date.parse(original.Created!.At!)),
            `${view} ${key}`,
          ).toBeLessThan(60_000)
        }
      }
    }
  })

  it('starts with a deleted department and its integrations, and a feed deleted alone', () => {
    expect(showcase.trash('default').get('cnt.northgate.urgent-care-annex')).toEqual(
      expect.arrayContaining([
        'uxe.northgate.lab-results.urgent-care-annex.hl7-feed',
        'exe.northgate.lab-results.urgent-care-annex',
        'exe.northgate.e-prescription.urgent-care-annex',
        'cnt.northgate.urgent-care-annex',
      ]),
    )
    expect(showcase.trash('default').get('uxe.riverside.billing-claims.radiology.claims-batch')).toEqual([
      'uxe.riverside.billing-claims.radiology.claims-batch',
    ])
  })

  it('runs every hospital in its own time zone', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('sbt.st-anne')?.TimeZone).toBe('America/Chicago')
    expect(annotations.get('sbt.riverside')?.TimeZone).toBe('America/Indiana/Indianapolis')
    expect(annotations.get('sbt.northgate')?.TimeZone).toBe('America/Detroit')
    // The departments, licenses, integrations and feeds follow their hospital.
    for (const [key, annotation] of annotations) {
      const hospital = /^(cnt|usg|exe|uxe)\.([a-z-]+)\./.exec(key)?.[2]
      if (hospital) {
        expect(annotation.TimeZone, key).toBe(annotations.get(`sbt.${hospital}`)?.TimeZone)
      }
    }
  })

  it('licenses each system by its own measure, sized by the hospital', () => {
    expect(showcase.own('default', 'usg.st-anne.lab-results')).toMatchObject({ concurrentUsers: 410 })
    expect(showcase.versions('default', 'usg.st-anne.lab-results')).toBe(3)
    expect(showcase.own('default', 'usg.riverside.lab-results')).toMatchObject({ concurrentUsers: 180 })
    expect(showcase.own('default', 'usg.st-anne.imaging')).toMatchObject({ readingStations: 24 })
    expect(showcase.own('default', 'usg.northgate.e-prescription')).toMatchObject({ prescribers: 350 })
    expect(showcase.own('default', 'usg.northgate.patient-portal')).toMatchObject({ activePatients: 99_000 })
    expect(showcase.own('default', 'usg.riverside.billing-claims')).toMatchObject({ claimsPerMonth: 20_100 })
    expect(showcase.own('default', 'usg.st-anne.billing-claims')).not.toHaveProperty('concurrentUsers')
  })

  it('overrides the portal defaults of the network under their own names', () => {
    expect(showcase.own('default', 'rst.patient-portal')).toMatchObject({
      resultReleaseDelayHours: 0,
      proxyAgeLimit: 12,
      messageResponseHours: 48,
    })
    const effective = (key: string) => effectiveContent(unseededShowcaseDb(), showcase.scope('default'), key)
    expect(effective('exe.st-anne.patient-portal.pediatrics')).toMatchObject({
      resultReleaseDelayHours: 0,
      proxyAgeLimit: 18,
    })
    expect(effective('exe.northgate.patient-portal.oncology')).toMatchObject({ messageResponseHours: 24 })
    for (const key of ['exe.st-anne.patient-portal.pediatrics', 'exe.northgate.patient-portal.oncology']) {
      expect(effective(key), key).not.toHaveProperty('releaseDelayHours')
    }
  })

  it('titles the departments, licenses, integrations and feeds of every view', () => {
    for (const view of views) {
      for (const [key, annotation] of showcase.annotations(view)) {
        if (/^(cnt|usg|exe|uxe)\./.test(key)) {
          expect(annotation.Title, `${view} ${key}`).not.toBe(sentenceTitle(annotation.Name!))
        }
      }
    }
    const staging = showcase.annotations('staging')
    expect(staging.get('usg.st-anne.e-prescription')?.Title).toBe('St. Anne e-prescription license')
    expect(staging.get('exe.st-anne.lab-results.test-ward')?.Title).toBe('Lab results in the St. Anne test ward')
    expect(showcase.annotations('development').get('uxe.st-anne.lab-results.qa-ward.hl7-feed')?.Title).toBe(
      'HL7 results feed for the St. Anne QA ward',
    )
  })

  it('brings telehealth, the router upgrade and the downtime procedures in the release candidate', () => {
    const keys = showcase.keys('release-candidate')
    expect(keys.has('rst.telehealth')).toBe(true)
    expect(keys.has('exe.st-anne.telehealth.cardiology')).toBe(true)
    expect(showcase.keys('default').has('rst.telehealth')).toBe(false)
    expect(showcase.latest('release-candidate', 'schema:type:exe')).toMatchObject({
      required: ['endpoint', 'certificateRef', 'downtimeProcedure'],
    })
    expect(showcase.own('release-candidate', 'exe.st-anne.lab-results.emergency')?.downtimeProcedure).toBeDefined()
    expect(showcase.versions('release-candidate', 'rst.lab-results')).toBe(19)
    expect(showcase.own('release-candidate', 'unt.imaging.dicom-router')).toMatchObject({ routerVersion: '5.2.0' })
    expect(annotationStatus(showcase.annotations('release-candidate').get('unt.imaging.dicom-router')!)).toBe('active')
    // What production deleted is not part of the release.
    expect(keys.has('cnt.northgate.urgent-care-annex')).toBe(false)
    expect(keys.has('uxe.riverside.billing-claims.radiology.claims-batch')).toBe(false)
  })

  it('runs the dev and QA wards on the sandboxes in development', () => {
    const keys = showcase.keys('development')
    expect(ofType(keys, 'cnt.')).toEqual(['cnt.st-anne.dev-ward', 'cnt.st-anne.qa-ward'])
    expect(showcase.own('development', 'exe.st-anne.lab-results.dev-ward')).toMatchObject({
      endpoint: expect.stringContaining('sandbox'),
      logLevel: 'debug',
    })
  })

  it('delivers the critical result alerts in the St. Anne ED and ICU only', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(ofType(keys, 'uxe.').filter((key) => key.endsWith('.critical-alerts'))).toEqual([
      'uxe.st-anne.lab-results.emergency.critical-alerts',
      'uxe.st-anne.lab-results.intensive-care.critical-alerts',
    ])
    for (const view of views.filter((name) => name !== 'feature-delivery1')) {
      expect(showcase.keys(view).has('unt.lab-results.critical-alerts'), view).toBe(false)
    }
  })
})
