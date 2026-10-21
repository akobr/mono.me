import type { Annotation, AuditStamp } from '@42for.net/2splatform.sdk'
import {
  buildAnnotationKey,
  canDescend,
  getAllAncestors,
  getAnnotationTypeInfo,
  getDirectAncestors,
  parseAnnotationKey,
  typeCodes,
  type AnnotationKeyParts,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import type { DocumentId, StoredDocument } from '../documents'
import { hash, random } from '../annotations'
import { personActor, versionHistory, type HistoryOptions } from './history'
import type { PersonaId } from './people'

// Builds the annotations, documents and seeded deletions of one showcase scope (424-77). Every annotation goes through
// the SDK's key builder and withRelations, so keys and relation members are those of the API; every method checks
// that the ancestors exist, so an authoring mistake fails the unit tests instead of producing a broken tree.

const day = 86_400_000

export interface Lifecycle {
  /** Days from now when the annotation becomes valid (ValidFrom); more than 0 makes it scheduled. */
  startsIn?: number
  /** Days from now when it expires (ExpiresAt); below 0 makes it expired, above 0 is a planned end. */
  expiresIn?: number
  disabled?: boolean
}

export interface Meta extends Lifecycle {
  title?: string
  /** Markdown. */
  description?: string
  /** DocumentationLink. */
  link?: string
  labels?: string[]
  values?: Record<string, unknown>
  /** The scope's time zone by default. */
  timeZone?: string
  /** 400 by default. */
  createdDaysAgo?: number
  /** The system by default, like the annotations DbCreator seeds. */
  createdBy?: PersonaId
  /** createdDaysAgo by default. */
  modifiedDaysAgo?: number
  /** createdBy by default. */
  modifiedBy?: PersonaId
}

export interface UnitMeta extends Meta {
  unitType: 'time' | 'message' | 'file' | 'event'
  /** A CRON for time, a queue for message, an artifact for file, an event name for event. */
  definition: string
}

export interface TrashedTree {
  rootKey: string
  /** The descendants, deepest first, then the root (the order of softDelete). */
  annotations: Annotation[]
  deletedAt: string
  by: PersonaId
}

export interface ScopeBuild {
  annotations: Annotation[]
  documents: ReadonlyMap<DocumentId, StoredDocument>
  trash: TrashedTree[]
}

export interface ScopeOptions {
  project: string
  view: string
  timeZone: string
  /** The default authors of the versions: the project's members who may edit. */
  editors: readonly PersonaId[]
}

type Versions = Record<string, unknown> | readonly Record<string, unknown>[]

/** A seeded PRNG with the helpers the projects need for generated content. */
export interface Rng {
  next(): number
  /** An integer from min to max, both included. */
  int(min: number, max: number): number
  pick<T>(items: readonly T[]): T
  chance(probability: number): boolean
  /** `count` distinct items in their original order. */
  some<T>(items: readonly T[], count: number): T[]
}

export function seededRng(seed: string): Rng {
  const next = random(hash(seed))
  const int = (min: number, max: number) => min + Math.floor(next() * (max - min + 1))
  return {
    next,
    int,
    pick: (items) => items[Math.floor(next() * items.length)]!,
    chance: (probability) => next() < probability,
    some: (items, count) => {
      const indexes = items.map((_, index) => index)
      for (let index = indexes.length - 1; index > 0; index--) {
        const other = int(0, index)
        ;[indexes[index], indexes[other]] = [indexes[other]!, indexes[index]!]
      }
      return indexes
        .slice(0, Math.min(count, items.length))
        .toSorted((a, b) => a - b)
        .map((index) => items[index]!)
    },
  }
}

/** "production-eu" → "Production eu", the title an annotation gets without its own. */
export const sentenceTitle = (name: string) => name.replaceAll('-', ' ').replace(/^\w/, (char) => char.toUpperCase())

const depthOf = (key: string) => getAllAncestors(key).length

export class ScopeBuilder {
  readonly project: string
  readonly view: string
  readonly timeZone: string
  readonly editors: readonly PersonaId[]
  /** Seeded by project and view, so a build gives the same content every time. */
  readonly rng: Rng
  private readonly now = Date.now()
  private readonly annotations = new Map<string, Annotation>()
  private readonly documents = new Map<DocumentId, StoredDocument>()
  private readonly deletions: { key: string; daysAgo: number; by: PersonaId }[] = []

  constructor(options: ScopeOptions) {
    this.project = options.project
    this.view = options.view
    this.timeZone = options.timeZone
    this.editors = options.editors
    this.rng = seededRng(`${options.project}/${options.view}`)
  }

  responsibility(name: string, meta?: Meta): string {
    return this.add('rst', { name }, meta)
  }

  unit(responsibility: string, name: string, meta: UnitMeta): string {
    return this.add('unt', { responsibility, name }, meta, { UnitType: meta.unitType, UnitDefinition: meta.definition })
  }

  subject(name: string, meta?: Meta): string {
    return this.add('sbt', { name }, meta)
  }

  context(subject: string, name: string, meta?: Meta): string {
    return this.add('cnt', { subject, name }, meta)
  }

  usage(subject: string, responsibility: string, meta?: Meta): string {
    return this.add('usg', { subject, responsibility }, meta)
  }

  execution(subject: string, responsibility: string, context: string, meta?: Meta): string {
    return this.add('exe', { subject, responsibility, context }, meta)
  }

  unitOfExecution(subject: string, responsibility: string, context: string, unit: string, meta?: Meta): string {
    return this.add('uxe', { subject, responsibility, context, unit }, meta)
  }

  has(key: string): boolean {
    return this.annotations.has(key)
  }

  /** The annotations added so far, in the order they were added. */
  keys(code?: AnnotationTypeCode): string[] {
    return [...this.annotations.keys()].filter((key) => !code || key.startsWith(`${code}.`))
  }

  /** The own configuration of an annotation; one content per version, oldest first. */
  configure(key: string, versions: Versions, options?: HistoryOptions): void {
    this.require(key, 'a configuration')
    this.document(`configuration:${key}`, versions, options)
  }

  template(code: AnnotationTypeCode, versions: Versions, options?: HistoryOptions): void {
    this.document(`template:${this.code(code)}`, versions, options)
  }

  typeSchema(code: AnnotationTypeCode, versions: Versions, options?: HistoryOptions): void {
    this.document(`schema:type:${this.code(code)}`, versions, options)
  }

  descendantSchema(ancestorKey: string, code: AnnotationTypeCode, versions: Versions, options?: HistoryOptions): void {
    this.require(ancestorKey, 'a descendant-type schema')
    if (!canDescend(ancestorKey, this.code(code))) {
      throw new Error(`${this.scope}: ${code} cannot descend from ${ancestorKey}.`)
    }
    this.document(`schema:descendant:${ancestorKey}:${code}`, versions, options)
  }

  annotationSchema(key: string, versions: Versions, options?: HistoryOptions): void {
    this.require(key, 'an annotation schema')
    this.document(`schema:annotation:${key}`, versions, options)
  }

  /** Puts the key and every descendant of the scope into Deleted, as one deletion `daysAgo` days ago by `by`. */
  trash(key: string, options: { daysAgo: number; by: PersonaId }): void {
    this.require(key, 'a deletion')
    this.deletions.push({ key, ...options })
  }

  build(): ScopeBuild {
    const active = new Map(this.annotations)
    const trash: TrashedTree[] = this.deletions.map(({ key, daysAgo, by }) => {
      if (!active.has(key)) {
        throw new Error(`${this.scope}: ${key} is deleted twice.`)
      }
      // The root is the shallowest key of its tree, so it comes last.
      const keys = [...active.keys()]
        .filter((candidate) => candidate === key || getAllAncestors(candidate).includes(key))
        .toSorted((a, b) => depthOf(b) - depthOf(a) || a.localeCompare(b))
      const annotations = keys.map((deleted) => active.get(deleted)!)
      keys.forEach((deleted) => active.delete(deleted))
      return { rootKey: key, annotations, deletedAt: this.at(-daysAgo), by }
    })
    return { annotations: [...active.values()], documents: new Map(this.documents), trash }
  }

  private get scope() {
    return `${this.project}/${this.view}`
  }

  private at(days: number) {
    return new Date(this.now + days * day).toISOString()
  }

  private stamp(daysAgo: number, by: PersonaId | undefined): AuditStamp {
    return { At: this.at(-daysAgo), By: by ? personActor(by) : { Kind: 'System' } }
  }

  private code(code: AnnotationTypeCode): AnnotationTypeCode {
    if (!typeCodes.includes(code)) {
      throw new Error(`${this.scope}: unknown type ${code}.`)
    }
    return code
  }

  private require(key: string, what: string) {
    if (!this.annotations.has(key)) {
      throw new Error(`${this.scope}: ${what} needs the annotation ${key}.`)
    }
  }

  private document(id: DocumentId, versions: Versions, options: HistoryOptions = {}) {
    if (this.documents.has(id)) {
      throw new Error(`${this.scope}: ${id} is written twice.`)
    }
    const contents = Array.isArray(versions) ? versions : [versions as Record<string, unknown>]
    if (contents.length === 0) {
      throw new Error(`${this.scope}: ${id} needs at least one version.`)
    }
    this.documents.set(id, versionHistory(contents, { ...options, authors: options.authors ?? this.editors }, this.now))
  }

  private add(code: AnnotationTypeCode, parts: AnnotationKeyParts, meta: Meta = {}, members: object = {}): string {
    const key = buildAnnotationKey(code, parts)
    if (this.annotations.has(key)) {
      throw new Error(`${this.scope}: ${key} is added twice.`)
    }
    const missing = getDirectAncestors(key).filter((ancestor) => !this.annotations.has(ancestor))
    if (missing.length) {
      throw new Error(`${this.scope}: ${key} needs ${missing.join(', ')} first.`)
    }

    const parsed = parseAnnotationKey(key)!
    const createdDaysAgo = meta.createdDaysAgo ?? 400
    const annotation = {
      ProjectName: this.project,
      ViewName: this.view,
      AnnotationKey: key,
      Name: parsed.name,
      AnnotationType: getAnnotationTypeInfo(code)!.type,
      Title: meta.title ?? sentenceTitle(parsed.name),
      Description: meta.description,
      DocumentationLink: meta.link,
      Labels: meta.labels?.length ? [...meta.labels] : undefined,
      Values: meta.values ? structuredClone(meta.values) : undefined,
      TimeZone: meta.timeZone ?? this.timeZone,
      IsDisabled: meta.disabled ? true : undefined,
      ValidFrom: meta.startsIn === undefined ? undefined : this.at(meta.startsIn),
      ExpiresAt: meta.expiresIn === undefined ? undefined : this.at(meta.expiresIn),
      Created: this.stamp(createdDaysAgo, meta.createdBy),
      Modified: this.stamp(meta.modifiedDaysAgo ?? createdDaysAgo, meta.modifiedBy ?? meta.createdBy),
      ...members,
    } as Annotation
    this.annotations.set(key, withRelations(annotation))
    return key
  }
}
