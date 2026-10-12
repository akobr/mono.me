import type { Annotation } from '@42for.net/2splatform.sdk'
import { parseAnnotationKey, type AnnotationTypeCode } from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'

// Deterministic annotation data for the mock API. Each scope is generated from a seed; edits are stored as
// overrides (see db.ts), so even the 20,000-annotation scenario fits into sessionStorage.

const typeNames: Record<AnnotationTypeCode, Annotation['AnnotationType']> = {
  rst: 'Responsibility',
  unt: 'Unit',
  sbt: 'Subject',
  usg: 'Usage',
  cnt: 'Context',
  exe: 'Execution',
  uxe: 'UnitOfExecution',
}

const baseSubjects = [
  'northwind',
  'contoso',
  'fabrikam',
  'globex',
  'initech',
  'umbrella',
  'hooli',
  'wayne',
  'stark',
  'tyrell',
]
const responsibilities: Record<string, string[]> = {
  invoicing: ['pdf', 'email'],
  payments: ['card', 'sepa'],
  reporting: ['csv', 'xlsx'],
  notifications: ['sms', 'email', 'push'],
  archiving: ['s3'],
  dunning: ['letter'],
}
const contextNames = ['prod', 'staging', 'eu', 'us', 'test']
const labelPool = ['eu', 'us', 'pci', 'beta', 'legacy', 'critical', 'gdpr', 'internal']
const timeZones = ['Europe/Prague', 'Europe/London', 'America/New_York', 'UTC', 'Asia/Tokyo']

/** Small deterministic PRNG (mulberry32). */
function random(seed: number) {
  let state = seed >>> 0
  return () => {
    state = (state + 0x6d2b79f5) >>> 0
    let t = state
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function hash(text: string): number {
  let value = 2166136261
  for (const char of text) {
    value = Math.imul(value ^ char.charCodeAt(0), 16777619)
  }
  return value >>> 0
}

const title = (name: string) => name.replaceAll('-', ' ').replace(/^\w/, (char) => char.toUpperCase())

export interface ScopeSeed {
  project: string
  view: string
  subjects: number
}

/** Generates the annotations of a scope: about 40 per subject. */
export function generateAnnotations(seed: ScopeSeed): Annotation[] {
  const next = random(hash(`${seed.project}/${seed.view}/${seed.subjects}`))
  const pick = <T>(items: readonly T[]) => items[Math.floor(next() * items.length)]!
  const chance = (probability: number) => next() < probability
  const now = Date.now()
  const day = 24 * 3600 * 1000
  const seededAt = new Date(now - 30 * day).toISOString()
  const result: Annotation[] = []

  const subjects = Array.from({ length: seed.subjects }, (_, index) =>
    index < baseSubjects.length
      ? baseSubjects[index]!
      : `${baseSubjects[index % baseSubjects.length]}-${Math.floor(index / baseSubjects.length) + 1}`,
  )

  const add = (key: string, extra: Partial<Annotation> = {}) => {
    const parsed = parseAnnotationKey(key)!
    const labels = labelPool.filter(() => chance(0.12))
    const status = next()
    result.push(
      withRelations({
        ProjectName: seed.project,
        ViewName: seed.view,
        AnnotationKey: key,
        Name: parsed.name,
        AnnotationType: typeNames[parsed.code],
        Title: `${title(parsed.name)} ${typeNames[parsed.code] === 'UnitOfExecution' ? 'unit of execution' : typeNames[parsed.code].toLowerCase()}`,
        Description: chance(0.4) ? `Settings of **${parsed.name}** used by the ${seed.view} view.` : undefined,
        DocumentationLink: chance(0.15) ? `https://docs.example.com/${key.replaceAll('.', '/')}` : undefined,
        Labels: labels.length ? labels : undefined,
        Values: chance(0.3) ? { tier: pick(['gold', 'silver', 'bronze']), retries: Math.floor(next() * 5) } : undefined,
        TimeZone: pick(timeZones),
        IsDisabled: status < 0.04 ? true : undefined,
        ValidFrom: status > 0.97 ? new Date(now + 10 * day).toISOString() : undefined,
        ExpiresAt: status > 0.94 && status <= 0.97 ? new Date(now - 5 * day).toISOString() : undefined,
        // seeded by the system, like DbCreator's annotations
        Created: { At: seededAt, By: { Kind: 'System' } },
        Modified: { At: seededAt, By: { Kind: 'System' } },
        ...extra,
      }),
    )
  }

  for (const [responsibility, units] of Object.entries(responsibilities)) {
    add(`rst.${responsibility}`)
    units.forEach((unit) => add(`unt.${responsibility}.${unit}`))
  }

  for (const subject of subjects) {
    add(`sbt.${subject}`)
    const contexts = contextNames.filter((_, index) => index < 2 || chance(0.35))
    contexts.forEach((context) => add(`cnt.${subject}.${context}`))

    for (const [responsibility, units] of Object.entries(responsibilities)) {
      if (!chance(0.7)) {
        continue
      }
      add(`usg.${subject}.${responsibility}`)

      for (const context of contexts) {
        add(`exe.${subject}.${responsibility}.${context}`)
        units.filter(() => chance(0.5)).forEach((unit) => add(`uxe.${subject}.${responsibility}.${context}.${unit}`))
      }
    }
  }

  // One unit of execution whose execution is missing, so the "(missing)" tree rows show up.
  add(`uxe.${subjects[0]}.invoicing.legacy.pdf`, { Title: 'Orphaned unit of execution', Labels: ['legacy'] })

  return result
}
