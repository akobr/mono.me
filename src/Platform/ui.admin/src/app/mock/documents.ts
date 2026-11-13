import type { Annotation, AuditActor, AuditStamp, VersionRestoreResult } from '@42for.net/2splatform.sdk'
import { codeOfKey, typeCodes, type AnnotationTypeCode } from '@/utils/annotation-key'
import { sameJson } from '@/utils/json-patch'
import { accountActor, stamp } from './audit'
import { annotationsOf, type MockDb } from './db'
import { templates, typeSchemas } from './schemas'
import { showcaseScope } from './showcase'

// Versioned documents of the mock API: configurations, templates and the three schema kinds. The generated base
// history is deterministic; edits copy the document into the stored overrides (db.documents) and append versions. A
// deleted document keeps its versions, as on the API: they stay listed and readable, a restore of one brings the
// document back, and the next write continues their numbering.

export interface StoredVersion {
  Version: number
  /** Who wrote the version and when, as ConfigurationVersion.Modified of the API. */
  Modified: AuditStamp
  ExpirationTime?: string
  /** undefined: the version's document has expired. */
  Content?: Record<string, unknown>
  /** The version a restore copied into this one (ConfigurationVersion.RestoredFrom). */
  RestoredFrom?: number
}

export interface StoredDocument {
  versions: StoredVersion[]
  /** The document is deleted: it has no current version, but its versions stay. */
  deleted?: boolean
}

export type DocumentId =
  | `configuration:${string}`
  | `template:${string}`
  | `schema:type:${string}`
  | `schema:annotation:${string}`
  | `schema:descendant:${string}:${string}`

export interface ScopeRef {
  org: string
  project: string
  view: string
}

const day = 86_400_000
const authors: AuditActor[] = ['user_01MOCKADA', 'user_01MOCKGRACE', 'user_01MOCKALAN'].map(accountActor)

const hash = (text: string) => {
  let value = 0
  for (const char of text) {
    value = (value * 31 + char.charCodeAt(0)) >>> 0
  }
  return value
}

const scopeId = (scope: ScopeRef) => `${scope.org}/${scope.project}/${scope.view}`

/** Builds a history of `count` versions from a content function; versions older than 30 days are expired. */
function history(count: number, content: (version: number) => Record<string, unknown>): StoredDocument {
  const now = Date.now()
  return {
    versions: Array.from({ length: count }, (_, index) => {
      const version = index + 1
      const created = now - (count - version) * 4 * day - day
      const expired = now - created > 30 * day
      return {
        Version: version,
        Modified: { At: new Date(created).toISOString(), By: authors[version % authors.length]! },
        ExpirationTime: new Date(created + 30 * day).toISOString(),
        Content: expired ? undefined : content(version),
      }
    }),
  }
}

/** Own configuration content per type; `version` varies a few values so diffs have something to show. */
function configurationContent(annotation: Annotation, version: number): Record<string, unknown> {
  const parts = annotation.AnnotationKey.split('.')
  switch (codeOfKey(annotation.AnnotationKey)) {
    case 'rst':
      return { enabled: true, retries: 2, timeout: 20 + version }
    case 'sbt':
      return { baseUrl: `https://api.${parts[1]}.example`, region: version % 2 ? 'eu-west' : 'us-east' }
    case 'unt':
      return { format: parts[2], maxSizeMb: 5 * version }
    case 'usg':
      return { mode: version % 2 ? 'batch' : 'stream', channels: ['email'] }
    case 'cnt':
      return { baseUrl: `https://${parts[2]}.${parts[1]}.example`, maintenance: version % 3 === 0 }
    case 'exe':
      return {
        retries: version % 6,
        timeout: 10 + version * 5,
        // The context configures baseUrl; the execution inherits it, so @config reads it from the snapshot.
        endpoint: '@config("/baseUrl")',
        notify: [`ops@${parts[1]}.example`],
        tiers: [
          { name: 'gold', limit: 100 * version },
          { name: 'silver', limit: 50 },
        ],
        headers: { 'X-Tenant': parts[1]! },
        ...(version >= 3 ? { notes: `Reviewed in version ${version}.` } : {}),
      }
    case 'uxe':
      return { retries: 1, channels: version % 2 ? ['email'] : ['email', 'sms'] }
    default:
      return {}
  }
}

const configurationKeysCache = new WeakMap<ReadonlyMap<string, Annotation>, Map<string, number>>()

/**
 * About a third of the seeded annotations has a configuration, with 1-12 versions. An annotation created later (by the
 * API or an import) starts without one, as on the server, so a dry run of an import predicts the real run.
 */
function configurationKeys(annotations: ReadonlyMap<string, Annotation>): Map<string, number> {
  const cached = configurationKeysCache.get(annotations)
  if (cached) {
    return cached
  }
  const result = new Map<string, number>()
  let index = 0
  for (const [key, annotation] of annotations) {
    if (annotation.Created?.By?.Kind === 'System' && hash(key) % 3 === 0) {
      result.set(key, (index++ % 12) + 1)
    }
  }
  configurationKeysCache.set(annotations, result)
  return result
}

function baseDocument(db: MockDb, scope: ScopeRef, id: DocumentId): StoredDocument | undefined {
  if (db.scenario === 'showcase') {
    return showcaseScope(scope.org, scope.project, scope.view)?.documents.get(id)
  }

  const [kind, ...rest] = id.split(':')
  const annotations = annotationsOf(db, scope.org, scope.project, scope.view)

  if (kind === 'configuration') {
    const key = rest.join(':')
    const count = configurationKeys(annotations).get(key)
    const annotation = annotations.get(key)
    return count && annotation ? history(count, (version) => configurationContent(annotation, version)) : undefined
  }

  if (kind === 'template') {
    const content = templates[rest[0] as AnnotationTypeCode]
    return content
      ? history(3, (version) => ({ ...content, retries: (content.retries as number) + version - 3 }))
      : undefined
  }

  if (kind === 'schema' && rest[0] === 'type') {
    const schema = typeSchemas[rest[1] as AnnotationTypeCode] as Record<string, unknown> | undefined
    return schema
      ? history(3, (version) =>
          version === 3 ? structuredClone(schema) : { ...structuredClone(schema), required: [] },
        )
      : undefined
  }

  if (kind === 'schema' && rest[0] === 'annotation' && rest[1] === 'exe.northwind.invoicing.prod') {
    return history(2, (version) => ({ properties: { retries: { maximum: version === 2 ? 5 : 8 } } }))
  }

  if (kind === 'schema' && rest[0] === 'descendant' && rest[1] === 'sbt.northwind' && rest[2] === 'exe') {
    return history(1, () => ({ properties: { region: { default: 'eu-west' } } }))
  }

  return undefined
}

// null marks a deletion stored before deleted documents kept their versions.
const storeOf = (db: MockDb, scope: ScopeRef) =>
  ((db.documents ??= {})[scopeId(scope)] ??= {}) as Record<string, StoredDocument | null>

/** The document with every version, also when it is deleted (`deleted`); undefined when it never had one. */
export function getHistory(db: MockDb, scope: ScopeRef, id: DocumentId): StoredDocument | undefined {
  const stored = storeOf(db, scope)[id]
  return stored === null ? undefined : (stored ?? baseDocument(db, scope, id))
}

/** The document that exists: undefined when it is deleted or was never written. */
export function getDocument(db: MockDb, scope: ScopeRef, id: DocumentId): StoredDocument | undefined {
  const document = getHistory(db, scope, id)
  return document?.deleted ? undefined : document
}

export function latest(document: StoredDocument | undefined): StoredVersion | undefined {
  return document?.versions.at(-1)
}

/** Stores the next version; a deleted document comes back with it. `restoredFrom` records the version a restore copied. */
export function putVersion(
  db: MockDb,
  scope: ScopeRef,
  id: DocumentId,
  content: Record<string, unknown>,
  restoredFrom?: number,
): StoredVersion {
  const { versions } = structuredClone(getHistory(db, scope, id) ?? { versions: [] })
  const now = new Date()
  const version: StoredVersion = {
    Version: (versions.at(-1)?.Version ?? 0) + 1,
    Modified: stamp(db, now),
    ExpirationTime: new Date(now.getTime() + 30 * day).toISOString(),
    Content: content,
    ...(restoredFrom === undefined ? {} : { RestoredFrom: restoredFrom }),
  }
  versions.push(version)
  storeOf(db, scope)[id] = { versions }
  return version
}

/** Deletes the current document; its versions stay. */
export function deleteDocument(db: MockDb, scope: ScopeRef, id: DocumentId): void {
  const document = getHistory(db, scope, id)
  storeOf(db, scope)[id] = document ? { versions: structuredClone(document.versions), deleted: true } : null
}

/** The versions of a document as the version lists of the API answer them (ConfigurationVersion). */
export function versionList(document: StoredDocument | undefined) {
  return (document?.versions ?? []).map(({ Version, Modified, ExpirationTime, RestoredFrom }) => ({
    Version,
    Modified,
    ExpirationTime,
    RestoredFrom,
  }))
}

const isExpired = (version: StoredVersion, now = Date.now()) =>
  !version.Content || (!!version.ExpirationTime && Date.parse(version.ExpirationTime) <= now)

/** What a restore of a version does, by the rules of the API: the content to store, or why nothing is written. */
export type RestorePlan =
  | { kind: 'not-found' }
  | { kind: 'unchanged'; current: StoredVersion }
  | { kind: 'restore'; content: Record<string, unknown> }

/**
 * The restore of version `number` (spec 1 of Restore a Previous Version): the current version, or a current document
 * equal to it, changes nothing; an unknown or expired version is not found; otherwise its content is stored as a new
 * version, also when the document is deleted. The caller checks the content before it calls `putVersion`.
 */
export function planRestore(db: MockDb, scope: ScopeRef, id: DocumentId, number: number): RestorePlan {
  const document = getHistory(db, scope, id)
  const current = document?.deleted ? undefined : latest(document)
  if (current && current.Version === number) {
    return { kind: 'unchanged', current }
  }
  const source = document?.versions.find((entry) => entry.Version === number)
  if (!source || isExpired(source)) {
    return { kind: 'not-found' }
  }
  if (current?.Content && sameJson(current.Content, source.Content)) {
    return { kind: 'unchanged', current }
  }
  return { kind: 'restore', content: structuredClone(source.Content!) }
}

/** The response of a restore: the version asked for, and the new version or the current one. */
export const restoreResult = (
  outcome: 'Restored' | 'Unchanged',
  fromVersion: number,
  version: StoredVersion,
): VersionRestoreResult => ({
  Outcome: outcome,
  FromVersion: fromVersion,
  Version: version.Version,
  Modified: version.Modified,
})

/**
 * The showcase's documents of a kind in the scope. A configuration is listed while its annotation is active, as the
 * generated configurations are; a restore lists it again.
 */
function showcaseDocumentIds(db: MockDb, scope: ScopeRef, prefix: string): DocumentId[] {
  const annotations = annotationsOf(db, scope.org, scope.project, scope.view)
  const documents = showcaseScope(scope.org, scope.project, scope.view)?.documents
  return [...(documents?.keys() ?? [])].filter(
    (id) =>
      id.startsWith(prefix) && (!id.startsWith('configuration:') || annotations.has(id.slice('configuration:'.length))),
  )
}

/** The generated documents of a kind in the scope, for the other datasets. */
function generatedDocumentIds(db: MockDb, scope: ScopeRef, prefix: string): DocumentId[] {
  const annotations = annotationsOf(db, scope.org, scope.project, scope.view)
  return prefix === 'configuration:'
    ? [...configurationKeys(annotations).keys()].map((key) => `configuration:${key}` as const)
    : prefix === 'template:'
      ? typeCodes.map((code) => `template:${code}` as const)
      : [
          ...typeCodes.map((code) => `schema:type:${code}` as const),
          'schema:annotation:exe.northwind.invoicing.prod' as const,
          'schema:descendant:sbt.northwind:exe' as const,
        ]
}

/** Ids of every existing document of a kind in the scope (base and stored). */
export function documentIds(db: MockDb, scope: ScopeRef, prefix: string): DocumentId[] {
  const base =
    db.scenario === 'showcase' ? showcaseDocumentIds(db, scope, prefix) : generatedDocumentIds(db, scope, prefix)
  const stored = Object.keys(storeOf(db, scope)).filter((id) => id.startsWith(prefix)) as DocumentId[]
  return [...new Set([...base, ...stored])].filter((id) => getDocument(db, scope, id) !== undefined)
}

// The showcase builds its histories with it too, and must not import this module (it imports db.ts).
export { deepMerge } from './merge'
