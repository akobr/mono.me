import { http, HttpResponse, type HttpHandler } from 'msw'
import { parse as parseJsonc, printParseErrorCode, type ParseError } from 'jsonc-parser'
import type {
  Annotation,
  AuditActor,
  ViewArchiveContent,
  ViewExport,
  ViewExportCreate,
  ViewExportFormat,
  ViewImport,
  ViewImportExisting,
  ViewImportItem,
  ViewImportItemOutcome,
  ViewImportSummary,
  ViewImportWarning,
} from '@42for.net/2splatform.sdk'
import { codeOfKey, getAllAncestors, parseAnnotationKey, type AnnotationTypeCode } from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { validateSchemaDocument } from '@/utils/schema-validation'
import { annotationProblems, byDepthTypeKey, storeAnnotation } from './annotation-handlers'
import {
  describe,
  getArchiveFileName,
  getContent,
  getPath,
  identityOf,
  isSkippedPath,
  manifestFileName,
  parseArchiveName,
  type ArchiveName,
  type ViewDocumentKind,
} from './archive-names'
import { appendAudit, diffAnnotations, mockActor, newEntry } from './audit'
import { ownContent, schemaErrors } from './configuration-handlers'
import { annotationsOf, overridesOf, type MockDb } from './db'
import {
  deepMerge,
  documentIds,
  getDocument,
  latest,
  putVersion,
  type DocumentId,
  type ScopeRef,
  type StoredVersion,
} from './documents'
import { errorResponse, requireRole, withDb } from './handlers'
import { createZip, readZip, textFile, type ZipFile } from './zip'

// Exports and imports of a view, on the routes and with the options of the API (export-import.md). A job is Running on
// its first read and ends on the second one, so the UI shows its progress.
//
// An archive holds the four kinds of the mock database: annotations, the template: and schema: documents, and
// configurations. The files are named like the API names them (archive-names.ts), one folder per kind, with
// export.json (manifest version 2) and the earlier versions as version files. A git export is the same folders with the
// current documents and .git/HEAD; the mock writes no repository. The mock keeps no versions of a deleted document, so
// its exports never list one.
//
// An import reads the files by their names in any folder and runs the passes of the API: annotations (ancestors
// first), schemas, templates, configurations, with Include, Existing, Force and DryRun. An overlay holds what the
// import will write, so a schema and its configurations can change together and a dry run predicts the real run. The
// outcomes, error codes, messages, warnings and summaries are the API's.

const api = '*/api/v1/:org/:project/:view'
const day = 86_400_000
const kinds: readonly ViewArchiveContent[] = ['Annotations', 'Schemas', 'Templates', 'Configurations']
const existingModes: readonly ViewImportExisting[] = ['Overwrite', 'Skip']
/** The kinds of documents in import order (ViewDocumentKind). */
const documentKinds: readonly ViewDocumentKind[] = [
  'Annotation',
  'TypeSchema',
  'DescendantTypeSchema',
  'AnnotationSchema',
  'Template',
  'Configuration',
]

type ScopeParams = { org: string; project: string; view: string }
type Content = Record<string, unknown>

interface ExportJob {
  kind: 'export'
  scope: ScopeRef
  state: ViewExport
  reads: number
  archive?: Uint8Array<ArrayBuffer>
}

interface ImportJob {
  kind: 'import'
  scope: ScopeRef
  state: ViewImport
  reads: number
  files: ZipFile[]
}

const jobs = new Map<string, ExportJob | ImportJob>()
let sequence = 0

const scopeOf = (params: ScopeParams): ScopeRef => ({ org: params.org, project: params.project, view: params.view })
const sameScope = (left: ScopeRef, right: ScopeRef) =>
  left.org === right.org && left.project === right.project && left.view === right.view
const newId = () => `job${Date.now().toString(36)}${(++sequence).toString(36)}`

/** Clears the jobs between tests. */
export function resetTransfers(): void {
  jobs.clear()
}

/** A defined name in any case, as the API reads kinds and modes. */
const matchName = <T extends string>(names: readonly T[], value: unknown) =>
  typeof value === 'string' ? names.find((name) => name.toLowerCase() === value.trim().toLowerCase()) : undefined

/** The included kinds in import order, all four for none, as the API resolves them; or the first unknown value. */
export function resolveInclude(values: readonly unknown[]): { include: ViewArchiveContent[] } | { unknown: unknown } {
  const picked = new Set<ViewArchiveContent>()

  for (const value of values) {
    const kind = matchName(kinds, value)
    if (!kind) {
      return { unknown: value }
    }
    picked.add(kind)
  }

  return { include: picked.size ? kinds.filter((kind) => picked.has(kind)) : [...kinds] }
}

const kindNames = `${kinds.slice(0, -1).join(', ')} or ${kinds.at(-1)}`

/** An ordinal comparison, as the API sorts names and keys; undefined first. */
const ordinal = (left = '', right = '') => (left < right ? -1 : left > right ? 1 : 0)

const isObject = (value: unknown): value is Content => !!value && typeof value === 'object' && !Array.isArray(value)
const hasContent = (value: unknown): value is Content => isObject(value) && Object.keys(value).length > 0

/** Equal JSON values, whatever the order of object members (JToken.DeepEquals). */
function sameJson(left: unknown, right: unknown): boolean {
  if (Array.isArray(left) || Array.isArray(right)) {
    return (
      Array.isArray(left) &&
      Array.isArray(right) &&
      left.length === right.length &&
      left.every((item, index) => sameJson(item, right[index]))
    )
  }
  if (isObject(left) && isObject(right)) {
    const members = Object.keys(left)
    return (
      members.length === Object.keys(right).length &&
      members.every((member) => member in right && sameJson(left[member], right[member]))
    )
  }
  return left === right
}

const isDeleted = (db: MockDb, scope: ScopeRef, key: string) =>
  !!overridesOf(db, scope.org, scope.project, scope.view).trash?.[key]

// ---------------------------------------------------------------------------------------------------------------------
// Documents of a view

/** The id of a schema in the mock database. */
function schemaId(kind: ViewDocumentKind, type?: string, key?: string): DocumentId {
  return kind === 'TypeSchema'
    ? `schema:type:${type}`
    : kind === 'AnnotationSchema'
      ? `schema:annotation:${key}`
      : `schema:descendant:${key}:${type}`
}

interface DocumentIdentity {
  kind: ViewDocumentKind
  type?: string
  key?: string
}

/** The kind, type code and key of a schema id; keys may contain ':', type codes do not. */
function schemaIdentity(id: DocumentId): DocumentIdentity {
  if (id.startsWith('schema:type:')) {
    return { kind: 'TypeSchema', type: id.slice('schema:type:'.length) }
  }
  if (id.startsWith('schema:annotation:')) {
    return { kind: 'AnnotationSchema', key: id.slice('schema:annotation:'.length) }
  }
  const rest = id.slice('schema:descendant:'.length)
  const separator = rest.lastIndexOf(':')
  return { kind: 'DescendantTypeSchema', key: rest.slice(0, separator), type: rest.slice(separator + 1) }
}

/** One versioned document: its versions with content, oldest first, and the current one when it has content. */
interface History extends DocumentIdentity {
  versions: StoredVersion[]
  current?: StoredVersion
}

function historyOf(db: MockDb, scope: ScopeRef, id: DocumentId, identity: DocumentIdentity): History | undefined {
  const document = getDocument(db, scope, id)
  // an expired or empty document is not a version
  const versions = (document?.versions ?? []).filter((version) => hasContent(version.Content))
  const last = latest(document)
  return versions.length ? { ...identity, versions, current: hasContent(last?.Content) ? last : undefined } : undefined
}

/**
 * The documents of the included kinds, each kind in the order of the API's writer. Like the API, the export leaves out
 * deleted annotations and the schemas and configurations that hang on an annotation that is not active.
 */
function viewDocuments(db: MockDb, scope: ScopeRef, include: readonly ViewArchiveContent[]) {
  const active = annotationsOf(db, scope.org, scope.project, scope.view)
  const histories = (prefix: string, identity: (id: DocumentId) => DocumentIdentity | undefined) =>
    documentIds(db, scope, prefix).flatMap((id) => {
      const found = identity(id)
      const history = found && historyOf(db, scope, id, found)
      return history ? [history] : []
    })

  return {
    annotations: include.includes('Annotations')
      ? [...active.values()].toSorted((a, b) => ordinal(a.AnnotationKey, b.AnnotationKey))
      : [],
    schemas: include.includes('Schemas')
      ? histories('schema:', (id) => {
          const identity = schemaIdentity(id)
          return identity.key && !active.has(identity.key) ? undefined : identity
        }).toSorted(
          (a, b) =>
            documentKinds.indexOf(a.kind) - documentKinds.indexOf(b.kind) ||
            ordinal(a.type, b.type) ||
            ordinal(a.key, b.key),
        )
      : [],
    templates: include.includes('Templates')
      ? histories('template:', (id) => ({ kind: 'Template', type: id.slice('template:'.length) })).toSorted((a, b) =>
          ordinal(a.type, b.type),
        )
      : [],
    configurations: include.includes('Configurations')
      ? histories('configuration:', (id) => {
          const key = id.slice('configuration:'.length)
          return active.has(key) ? { kind: 'Configuration', key } : undefined
        }).toSorted((a, b) => ordinal(a.key, b.key))
      : [],
  }
}

// The editable members of an annotation (AnnotationFactory.Normalize), in the order the API writes them.
const editableMembers = [
  'Title',
  'Description',
  'DocumentationLink',
  'Labels',
  'Values',
  'IsDisabled',
  'ValidFrom',
  'ExpiresAt',
  'TimeZone',
] as const
const unitMembers = ['UnitType', 'UnitDefinition'] as const

const membersOf = (code: AnnotationTypeCode): string[] =>
  code === 'unt' ? [...editableMembers, ...unitMembers] : [...editableMembers]

/** The file of an annotation: its editable members, null members left out (ViewAnnotationDocument.Write). */
export function annotationDocument(annotation: Annotation): Content {
  const record = annotation as unknown as Content
  return Object.fromEntries(
    membersOf(codeOfKey(annotation.AnnotationKey) ?? 'rst')
      .filter((member) => record[member] !== undefined && record[member] !== null)
      .map((member) => [member, record[member]]),
  )
}

/** The author of a version as the manifest names it: the name of its actor, or the id. */
const authorOf = (version: StoredVersion) => version.Modified.By?.Name ?? version.Modified.By?.Id ?? 'System'

const json = (value: unknown) => `${JSON.stringify(value, null, 2)}\n`

/**
 * The zip of an export (ViewArchiveWriter): one folder per included kind with documents, version files next to their
 * current file, each entry with the time of its version, and export.json last. A git export has the current documents
 * and .git/HEAD instead of version files and the manifest.
 */
function buildArchive(
  db: MockDb,
  scope: ScopeRef,
  format: ViewExportFormat,
  include: readonly ViewArchiveContent[],
  exportedBy: AuditActor,
) {
  const exportedAt = new Date()
  const view = viewDocuments(db, scope, include)
  const files: ZipFile[] = []
  const timeOf = (at: string | undefined) => (at ? new Date(at) : exportedAt)

  const annotations = view.annotations.map((annotation) => {
    const path = getPath('Annotation', undefined, annotation.AnnotationKey)
    const at = annotation.Modified?.At ?? annotation.Created?.At
    files.push(textFile(path, json(annotationDocument(annotation)), timeOf(at)))
    return {
      Key: annotation.AnnotationKey,
      File: path,
      Created: annotation.Created ?? null,
      Modified: annotation.Modified ?? null,
    }
  })

  const write = (history: History) => {
    let current: string | null = null
    const versions = []

    for (const version of history.versions) {
      const isCurrent = version === history.current
      const path = getPath(history.kind, history.type, history.key, isCurrent ? undefined : version.Version)
      if (format === 'Folder' || isCurrent) {
        files.push(textFile(path, json(version.Content), timeOf(version.Modified.At)))
      }
      if (isCurrent) {
        current = path
      } else {
        versions.push({
          Version: version.Version,
          File: path,
          Author: authorOf(version),
          CreatedAt: version.Modified.At,
        })
      }
    }

    // a deleted document (versions, no current one) has File and Version null
    return {
      File: current,
      Version: history.current?.Version ?? null,
      Author: history.current ? authorOf(history.current) : null,
      UpdatedAt: history.current?.Modified.At ?? null,
      Versions: versions,
    }
  }

  const schemas = view.schemas.map((history) => ({
    Kind: history.kind,
    AnnotationType: history.type ?? null,
    AnnotationKey: history.key ?? null,
    ...write(history),
  }))
  const templates = view.templates.map((history) => ({ AnnotationType: history.type, ...write(history) }))
  const configurations = view.configurations.map((history) => ({ AnnotationKey: history.key, ...write(history) }))

  if (format === 'Git') {
    files.push(textFile('.git/HEAD', 'ref: refs/heads/main\n', exportedAt))
  } else {
    const manifest = {
      ManifestVersion: 2,
      Organization: scope.org,
      Project: scope.project,
      View: scope.view,
      ExportedAt: exportedAt.toISOString(),
      ExportedBy: exportedBy,
      Include: include,
      Annotations: annotations,
      Schemas: schemas,
      Templates: templates,
      Configurations: configurations,
    }
    files.push(textFile(manifestFileName, json(manifest), exportedAt))
  }

  const withCurrent = (histories: History[]) => histories.filter((history) => history.current).length
  return {
    zip: createZip(files, exportedAt),
    annotations: view.annotations.length,
    schemas: withCurrent(view.schemas),
    templates: withCurrent(view.templates),
    configurations: withCurrent(view.configurations),
    versions: [...view.schemas, ...view.templates, ...view.configurations].reduce(
      (sum, history) => sum + history.versions.length,
      0,
    ),
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Reading an archive

/** A document file of an included kind (ViewArchiveFile): its content, or why it cannot be imported. */
interface ArchiveFile {
  path: string
  name: ArchiveName
  content?: Content
  errorCode?: string
  message?: string
}

/** A JSON object with comments and trailing commas allowed, as the API reads document files. */
function parseDocument(data: Uint8Array): Pick<ArchiveFile, 'content' | 'errorCode' | 'message'> {
  const invalid = (reason: string) => ({
    errorCode: 'InvalidDocument',
    message: `The file is not a JSON object: ${reason}`,
  })
  let text: string
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(data)
  } catch (error) {
    return invalid((error as Error).message)
  }

  const errors: ParseError[] = []
  const value: unknown = parseJsonc(text, errors, { allowTrailingComma: true, allowEmptyContent: false })
  if (errors.length) {
    return invalid(`${printParseErrorCode(errors[0]!.error)} at offset ${errors[0]!.offset}.`)
  }
  return isObject(value) ? { content: value } : invalid('The document is not a JSON object.')
}

/**
 * The document files of the included kinds in path order (ViewArchiveReader.Read). Hidden paths and __MACOSX are
 * skipped without being counted; version files, export.json and names that are not documents are ignored; files of
 * the other kinds are excluded. One document per identity, whatever the folders and the spelling of the key: the
 * first path wins, the others fail with DuplicateFile.
 */
function readArchive(zipFiles: readonly ZipFile[], include: readonly ViewArchiveContent[]) {
  const entries: { path: string; name: ArchiveName; data: Uint8Array }[] = []
  let ignored = 0
  let excluded = 0

  for (const file of zipFiles) {
    const path = file.name.replaceAll('\\', '/')
    if (path.endsWith('/') || isSkippedPath(path)) {
      continue
    }

    // an import never replays a history, so version files are ignored whatever their kind
    const name = parseArchiveName(path.slice(path.lastIndexOf('/') + 1))
    if (!name || name.version !== undefined) {
      ignored += 1
    } else if (!include.includes(getContent(name.kind))) {
      excluded += 1
    } else {
      entries.push({ path, name, data: file.data })
    }
  }

  const seen = new Map<string, string>()
  const files = entries
    .toSorted((a, b) => ordinal(a.path, b.path))
    .map(({ path, name, data }): ArchiveFile => {
      const identity = identityOf(name)
      const first = seen.get(identity)
      if (first !== undefined) {
        return {
          path,
          name,
          errorCode: 'DuplicateFile',
          message: `The file '${first}' already holds the ${describe(name.kind, name.type, name.key)}.`,
        }
      }

      seen.set(identity, path)
      return name.errorCode
        ? { path, name, errorCode: name.errorCode, message: name.message ?? 'The file cannot be imported.' }
        : { path, name, ...parseDocument(data) }
    })

  return { files, ignored, excluded }
}

/**
 * The passes of an import (Order): annotations by depth in the graph, type and key, so ancestors come first; type,
 * descendant-type and annotation schemas, each by type and key; templates by type; configurations by key. A duplicate
 * follows the file that holds its document.
 */
function byPass(a: ArchiveFile, b: ArchiveFile): number {
  return (
    documentKinds.indexOf(a.name.kind) - documentKinds.indexOf(b.name.kind) ||
    (a.name.kind === 'Annotation' ? byDepthTypeKey(a.name.key!, b.name.key!) : 0) ||
    ordinal(a.name.type, b.name.type) ||
    ordinal(a.name.key, b.name.key) ||
    ordinal(a.path, b.path)
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Importing the documents

interface ImportOptions {
  existing: ViewImportExisting
  force: boolean
  dryRun: boolean
}

/** The view as the import will leave it (ViewImportOverlay), for the checks that look beyond one document. */
interface Overlay {
  /** Keys the import makes active (created or re-created); a dry run writes nothing, so later passes ask here first. */
  active: Set<string>
  /** The deleted keys among them: the re-create empties their configurations. */
  recreated: Set<string>
  /** Keys whose annotation failed in this import. */
  failed: Set<string>
  /** Schemas of the archive the import will write: valid, included, and allowed by Existing. */
  schemas: Map<DocumentId, Content>
  /** Configurations of the archive the import will write: not empty, allowed by Existing, with an annotation. */
  configurations: Map<string, Content>
}

/** The outcome of one file (ViewImportResult). */
interface ImportResult {
  Outcome: ViewImportItemOutcome
  Version?: number
  ErrorCode?: string
  Message?: string
  Warnings?: ViewImportWarning[]
}

const skipped = (code: string, message: string): ImportResult => ({
  Outcome: 'Skipped',
  ErrorCode: code,
  Message: message,
})
const failed = (code: string, message: string): ImportResult => ({
  Outcome: 'Failed',
  ErrorCode: code,
  Message: message,
})

/** What the overlay needs to know of the view before the annotation pass. */
function readStored(db: MockDb, scope: ScopeRef) {
  const annotations = new Set(annotationsOf(db, scope.org, scope.project, scope.view).keys())
  return {
    annotations,
    schemas: new Set(
      documentIds(db, scope, 'schema:').filter((id) => hasContent(latest(getDocument(db, scope, id))?.Content)),
    ),
    configurations: new Set(
      documentIds(db, scope, 'configuration:')
        .map((id) => id.slice('configuration:'.length))
        .filter((key) => annotations.has(key) && hasContent(ownContent(db, scope, key))),
    ),
  }
}

/**
 * The schemas and configurations of the archive that the import will write, once the annotation pass is done: not
 * empty, of an annotation that is active (stored, or made active by the import), and allowed by Existing. With Skip, a
 * document the view already has stays, except the configuration of a re-created annotation.
 */
function fillOverlay(
  files: readonly ArchiveFile[],
  stored: ReturnType<typeof readStored>,
  options: ImportOptions,
  overlay: Overlay,
): void {
  const skip = options.existing === 'Skip'

  for (const { name, content, errorCode } of files) {
    if (errorCode || !hasContent(content) || name.kind === 'Annotation' || name.kind === 'Template') {
      continue
    }

    const key = name.key
    const hasAnnotation = !!key && (stored.annotations.has(key) || overlay.active.has(key))

    if (name.kind === 'Configuration') {
      if (hasAnnotation && (!skip || !stored.configurations.has(key!) || overlay.recreated.has(key!))) {
        overlay.configurations.set(key!, content)
      }
      continue
    }

    const id = schemaId(name.kind, name.type, key)
    if ((name.kind === 'TypeSchema' || hasAnnotation) && (!skip || !stored.schemas.has(id))) {
      overlay.schemas.set(id, content)
    }
  }
}

/**
 * The overlay follows the outcomes: an annotation that fails, or is skipped for a failed ancestor, fails what hangs on
 * it; a schema or configuration that fails or is skipped is not written, so it leaves the overlay.
 */
function track(file: ArchiveFile, result: ImportResult, overlay: Overlay): void {
  if (file.errorCode === 'DuplicateFile') {
    return
  }

  const { kind, type, key } = file.name
  const dropped = result.Outcome === 'Failed' || result.Outcome === 'Skipped'

  if (kind === 'Annotation') {
    if (result.Outcome === 'Failed' || result.ErrorCode === 'DependencyFailed') {
      overlay.failed.add(key!)
    }
  } else if (kind === 'Configuration') {
    if (dropped) {
      overlay.configurations.delete(key!)
    }
  } else if (kind !== 'Template' && dropped) {
    overlay.schemas.delete(schemaId(kind, type, key))
  }
}

// The members of a GET response that the server owns: an annotation file may hold them, the import ignores them.
const serverMembers = [
  'ProjectName',
  'ViewName',
  'AnnotationKey',
  'Name',
  'AnnotationType',
  'Created',
  'Modified',
  'Deletion',
]
const relationMembers: Record<AnnotationTypeCode, string[]> = {
  rst: ['UnitNames'],
  sbt: ['ContextNames', 'ResponsibilityNames'],
  unt: ['ResponsibilityKey', 'ResponsibilityName'],
  usg: ['SubjectKey', 'ResponsibilityKey', 'SubjectName', 'ResponsibilityName', 'ContextNames'],
  cnt: ['SubjectKey', 'SubjectName', 'ResponsibilityNames'],
  exe: [
    'ResponsibilityKey',
    'SubjectKey',
    'ContextKey',
    'UsageKey',
    'SubjectName',
    'ResponsibilityName',
    'ContextName',
    'UnitNames',
  ],
  uxe: [
    'ResponsibilityKey',
    'UnitKey',
    'SubjectKey',
    'ContextKey',
    'UsageKey',
    'ExecutionKey',
    'ResponsibilityName',
    'UnitName',
    'SubjectName',
    'ContextName',
  ],
}

const isText = (value: unknown) => typeof value === 'string'

/** The value checks of the members that are not text. */
const memberTypes: Record<string, (value: unknown) => boolean> = {
  Labels: (value) => Array.isArray(value) && value.every((label) => typeof label === 'string'),
  Values: isObject,
  IsDisabled: (value) => typeof value === 'boolean',
  ValidFrom: (value) => typeof value === 'string' && !Number.isNaN(Date.parse(value)),
  ExpiresAt: (value) => typeof value === 'string' && !Number.isNaN(Date.parse(value)),
}

/** The type code may differ in case, as in the file name; the names may not. */
function isKey(value: unknown, key: string): boolean {
  const parsed = typeof value === 'string' ? parseAnnotationKey(value) : undefined
  return !!parsed && `${parsed.code}${value!.toString().slice(3)}` === key
}

/**
 * The annotation a file holds, as a write stores it (ViewAnnotationDocument.TryRead): a member missing from the file
 * is cleared, a unit without UnitType or UnitDefinition gets the defaults of a create, and member names match in any
 * case. The server-owned members of a GET response are ignored; any other member fails the file.
 */
function readAnnotation(document: Content, key: string, scope: ScopeRef): { annotation: Annotation } | ImportResult {
  const parsed = parseAnnotationKey(key)!
  const editable = membersOf(parsed.code)
  const ignored = [...serverMembers, ...relationMembers[parsed.code]].map((name) => name.toLowerCase())
  const members: Content = {}
  const unknown: string[] = []

  for (const [member, value] of Object.entries(document)) {
    const name = editable.find((candidate) => candidate.toLowerCase() === member.toLowerCase())
    if (name) {
      if (name in members) {
        return failed('InvalidDocument', `The member '${name}' is in the file twice.`)
      }
      members[name] = value
    } else if (!ignored.includes(member.toLowerCase())) {
      unknown.push(member)
    } else if (member.toLowerCase() === 'annotationkey' && !isKey(value, key)) {
      return failed(
        'InvalidDocument',
        `The AnnotationKey ${JSON.stringify(value)} is not ${key}, the key of the file name.`,
      )
    }
  }

  if (unknown.length) {
    return failed(
      'InvalidDocument',
      unknown.length === 1
        ? `Unknown member '${unknown[0]}'.`
        : `Unknown members ${unknown.map((name) => `'${name}'`).join(', ')}.`,
    )
  }

  const present = Object.entries(members).filter(([, value]) => value !== null && value !== undefined)
  const wrong = present.find(([member, value]) => !(memberTypes[member] ?? isText)(value))
  if (wrong) {
    return failed('InvalidDocument', `The file is not a valid annotation: the value of ${wrong[0]} has a wrong type.`)
  }

  const annotation = withRelations({
    ProjectName: scope.project,
    ViewName: scope.view,
    AnnotationKey: key,
    Name: parsed.name,
    AnnotationType: parsed.type,
    ...Object.fromEntries(present),
  } as Annotation)
  const problems = annotationProblems(annotation)
  return problems.length
    ? failed('InvalidAnnotation', `The annotation '${key}' is not valid: ${problems.join(' ')}`)
    : { annotation }
}

const list = (keys: readonly string[]) => keys.join(', ')

/** ImportAnnotationAsync: an update of an active key, or a create or re-create with the missing ancestors. */
function importAnnotation(
  db: MockDb,
  scope: ScopeRef,
  key: string,
  content: Content,
  options: ImportOptions,
  overlay: Overlay,
): ImportResult {
  const read = readAnnotation(content, key, scope)
  if (!('annotation' in read)) {
    return read
  }

  // creating the key would create the failed ancestor with defaults
  const ancestors = getAllAncestors(key).toSorted(byDepthTypeKey)
  const failedAncestors = ancestors.filter((ancestor) => overlay.failed.has(ancestor))
  if (failedAncestors.length) {
    return skipped(
      'DependencyFailed',
      failedAncestors.length === 1
        ? `The ancestor ${failedAncestors[0]} failed in this import.`
        : `The ancestors ${list(failedAncestors)} failed in this import.`,
    )
  }

  const active = annotationsOf(db, scope.org, scope.project, scope.view)
  const existing = active.get(key)

  if (existing) {
    if (!diffAnnotations(existing, read.annotation)) {
      return { Outcome: 'Unchanged' }
    }
    if (options.existing === 'Skip') {
      return skipped('DocumentExists', `The view has a different annotation ${key}, and Skip keeps it.`)
    }
    if (!options.dryRun) {
      storeAnnotation(db, scope, read.annotation)
    }
    return { Outcome: 'Updated' }
  }

  // the keys the create writes, ancestors first; an ancestor that an earlier file of this import made active is not
  // among them, also in a dry run, where it is not stored
  const written = [...ancestors.filter((ancestor) => !active.has(ancestor) && !overlay.active.has(ancestor)), key].map(
    (writtenKey) => ({ key: writtenKey, isDeleted: isDeleted(db, scope, writtenKey) }),
  )
  const deleted = written.filter((write) => write.isDeleted).map((write) => write.key)

  if (deleted.length && options.existing === 'Skip') {
    return skipped(
      'AnnotationDeleted',
      deleted.length === 1
        ? `The annotation ${deleted[0]} is deleted, and Skip leaves it deleted.`
        : `The annotations ${list(deleted)} are deleted, and Skip leaves them deleted.`,
    )
  }

  if (!options.dryRun) {
    storeAnnotation(db, scope, read.annotation)
  }

  for (const write of written) {
    overlay.active.add(write.key)
    if (write.isDeleted) {
      overlay.recreated.add(write.key)
    }
  }

  const created = written
    .slice(0, -1)
    .filter((write) => !write.isDeleted)
    .map((write) => write.key)
  const warnings: ViewImportWarning[] = []

  if (created.length) {
    warnings.push({
      Code: 'AncestorCreated',
      Message:
        created.length === 1
          ? `The missing ancestor ${created[0]} is created with defaults.`
          : `The missing ancestors ${list(created)} are created with defaults.`,
    })
  }
  if (deleted.length) {
    warnings.push({
      Code: 'AnnotationRecreated',
      Message:
        deleted.length === 1
          ? `The deleted annotation ${deleted[0]} is re-created: its configuration goes to the versions and starts empty, and the children deleted with it stay deleted.`
          : `The deleted annotations ${list(deleted)} are re-created: their configurations go to the versions and start empty, and the children deleted with them stay deleted.`,
    })
  }

  return { Outcome: 'Created', Warnings: warnings.length ? warnings : undefined }
}

/** The skip of a document whose annotation is not active, neither stored nor made active by this import. */
function inactiveAnnotation(
  db: MockDb,
  scope: ScopeRef,
  key: string,
  overlay: Overlay,
  what: string,
): ImportResult | undefined {
  if (overlay.active.has(key) || annotationsOf(db, scope.org, scope.project, scope.view).has(key)) {
    return undefined
  }
  if (overlay.failed.has(key)) {
    return skipped('DependencyFailed', `The annotation ${key} failed in this import.`)
  }
  return isDeleted(db, scope, key)
    ? skipped('AnnotationDeleted', `The annotation ${key} is deleted; restore it before importing its ${what}.`)
    : skipped('AnnotationNotFound', `The view ${scope.view} has no annotation ${key}.`)
}

const schemaValidity = new WeakMap<Content, boolean>()

/** Whether a document is a valid JSON Schema, checked once per document. */
function isValidSchema(content: Content): boolean {
  let valid = schemaValidity.get(content)
  if (valid === undefined) {
    valid = validateSchemaDocument(content).length === 0
    schemaValidity.set(content, valid)
  }
  return valid
}

/**
 * The layers of combined schemas: the archive's schema where the overlay has one, else the stored one. An invalid
 * schema of the archive is not written, so the stored layer stays.
 */
function layerOf(db: MockDb, scope: ScopeRef, overlay: Overlay | undefined) {
  return (id: DocumentId): Content | undefined => {
    const brought = overlay?.schemas.get(id)
    return brought && isValidSchema(brought) ? brought : latest(getDocument(db, scope, id))?.Content
  }
}

/** The combined schema of a configuration: the type schema, the descendant-type schemas of its ancestors, its own. */
function mergedSchema(key: string, layer: (id: DocumentId) => Content | undefined): Content | undefined {
  const code = codeOfKey(key)!
  const ids: DocumentId[] = [
    `schema:type:${code}`,
    ...getAllAncestors(key).map((ancestor): DocumentId => `schema:descendant:${ancestor}:${code}`),
    `schema:annotation:${key}`,
  ]
  return ids.reduce<Content | undefined>((merged, id) => {
    const content = layer(id)
    return hasContent(content) ? (deepMerge(merged ?? {}, content) as Content) : merged
  }, undefined)
}

function isAffected(name: ArchiveName, key: string): boolean {
  switch (name.kind) {
    case 'TypeSchema':
      return codeOfKey(key) === name.type
    case 'AnnotationSchema':
      return key === name.key
    default:
      return codeOfKey(key) === name.type && getAllAncestors(key).includes(name.key!)
  }
}

/**
 * The keys of the configurations that would violate the schema, as the import leaves the view: the archive's
 * configurations replace the stored ones and are checked too, and the archive's schemas are the other layers.
 */
function violations(db: MockDb, scope: ScopeRef, name: ArchiveName, content: Content, overlay: Overlay): string[] {
  const active = annotationsOf(db, scope.org, scope.project, scope.view)
  const stored = documentIds(db, scope, 'configuration:')
    .map((id) => id.slice('configuration:'.length))
    .filter((key) => active.has(key) && !overlay.recreated.has(key) && hasContent(ownContent(db, scope, key)))
  const id = schemaId(name.kind, name.type, name.key)
  const others = layerOf(db, scope, overlay)
  const layer = (layerId: DocumentId) => (layerId === id ? content : others(layerId))

  return [...new Set([...stored, ...overlay.configurations.keys()])]
    .filter((key) => isAffected(name, key))
    .filter(
      (key) =>
        schemaErrors(mergedSchema(key, layer), overlay.configurations.get(key) ?? ownContent(db, scope, key)).length >
        0,
    )
    .toSorted(ordinal)
}

/** ("configuration of rst.payments", "violates") for the message of a violation: the keys in order, at most 20. */
function describeViolations(keys: readonly string[]): [string, string] {
  const listed = `${list(keys.slice(0, 20))}${keys.length > 20 ? ` … and ${keys.length - 20} more` : ''}`
  return keys.length === 1 ? [`configuration of ${listed}`, 'violates'] : [`configurations of ${listed}`, 'violate']
}

/** ImportSchemaAsync: checked against the configurations as the import leaves them. */
function importSchema(
  db: MockDb,
  scope: ScopeRef,
  name: ArchiveName,
  content: Content,
  options: ImportOptions,
  overlay: Overlay,
): ImportResult {
  if (name.kind !== 'TypeSchema') {
    const inactive = inactiveAnnotation(db, scope, name.key!, overlay, 'schemas')
    if (inactive) {
      return inactive
    }
  }
  if (!hasContent(content)) {
    return skipped('EmptyDocument', 'An empty document is not imported; delete the schema to clear it.')
  }

  // Force never stores a broken schema
  const issue = validateSchemaDocument(content)[0]
  if (issue) {
    return failed('InvalidSchema', `Invalid JSON Schema: ${issue.pointer || '/'} ${issue.message}.`)
  }

  const id = schemaId(name.kind, name.type, name.key)
  const existing = latest(getDocument(db, scope, id))
  const stored = hasContent(existing?.Content)

  if (stored && sameJson(existing!.Content, content)) {
    return { Outcome: 'Unchanged', Version: existing!.Version }
  }
  if (stored && options.existing === 'Skip') {
    return skipped(
      'DocumentExists',
      `The view has a different ${describe(name.kind, name.type, name.key)}, and Skip keeps it.`,
    )
  }

  const broken = violations(db, scope, name, content, overlay)
  let warnings: ViewImportWarning[] | undefined

  if (broken.length) {
    const [configurations, verb] = describeViolations(broken)
    if (!options.force) {
      return failed('SchemaViolation', `The ${configurations} ${verb} this schema.`)
    }
    warnings = [
      { Code: 'SchemaViolation', Message: `Force stores this schema although the ${configurations} ${verb} it.` },
    ]
  }

  const outcome = stored ? 'Updated' : 'Created'
  return options.dryRun
    ? { Outcome: outcome, Warnings: warnings }
    : { Outcome: outcome, Version: putVersion(db, scope, id, content).Version, Warnings: warnings }
}

/** ImportTemplateAsync: a template replaces the stored one; no schema validates it. */
function importTemplate(
  db: MockDb,
  scope: ScopeRef,
  type: string,
  content: Content,
  options: ImportOptions,
): ImportResult {
  if (!hasContent(content)) {
    return skipped('EmptyDocument', 'An empty document is not imported; delete the template to clear it.')
  }

  const id: DocumentId = `template:${type}`
  const existing = latest(getDocument(db, scope, id))
  const stored = hasContent(existing?.Content)

  if (stored && sameJson(existing!.Content, content)) {
    return { Outcome: 'Unchanged', Version: existing!.Version }
  }
  if (stored && options.existing === 'Skip') {
    return skipped('DocumentExists', `The view has a different template ${type}, and Skip keeps it.`)
  }

  const outcome = stored ? 'Updated' : 'Created'
  return options.dryRun
    ? { Outcome: outcome }
    : { Outcome: outcome, Version: putVersion(db, scope, id, content).Version }
}

/** ImportConfigurationAsync: validated against the schemas as the import leaves them. */
function importConfiguration(
  db: MockDb,
  scope: ScopeRef,
  key: string,
  content: Content,
  options: ImportOptions,
  overlay: Overlay,
): ImportResult {
  const inactive = inactiveAnnotation(db, scope, key, overlay, 'configuration')
  if (inactive) {
    return inactive
  }
  if (!hasContent(content)) {
    return skipped('EmptyDocument', 'An empty document is not imported; delete the configuration to clear it.')
  }

  const existing = latest(getDocument(db, scope, `configuration:${key}`))
  // the re-create of the annotation empties its configuration (in a dry run, it is still stored), so the file starts
  // the new lifetime
  const stored = hasContent(existing?.Content) && !overlay.recreated.has(key)

  if (stored && sameJson(existing!.Content, content)) {
    return { Outcome: 'Unchanged', Version: existing!.Version }
  }
  if (stored && options.existing === 'Skip') {
    return skipped('DocumentExists', `The view has a different configuration of ${key}, and Skip keeps it.`)
  }

  // a real run has written the schemas of the archive already; a dry run takes them from the overlay
  const errors = schemaErrors(mergedSchema(key, layerOf(db, scope, options.dryRun ? overlay : undefined)), content)
  let warnings: ViewImportWarning[] | undefined

  if (errors.length) {
    if (!options.force) {
      return failed('SchemaViolation', `The document violates its schema: ${errors.join('; ')}`)
    }
    warnings = [
      {
        Code: 'SchemaViolation',
        Message: `Force stores the document although it violates its schema: ${errors.join('; ')}`,
      },
    ]
  }

  const outcome = stored ? 'Updated' : 'Created'
  if (options.dryRun) {
    return { Outcome: outcome, Warnings: warnings }
  }

  const saved = putVersion(db, scope, `configuration:${key}`, content)
  appendAudit(db, scope, key, newEntry(db, 'ConfigurationChanged', { ConfigurationVersion: saved.Version }))
  return { Outcome: outcome, Version: saved.Version, Warnings: warnings }
}

function importFile(
  db: MockDb,
  scope: ScopeRef,
  file: ArchiveFile,
  options: ImportOptions,
  overlay: Overlay,
): ImportResult {
  if (file.errorCode) {
    return failed(file.errorCode, file.message ?? 'The file cannot be imported.')
  }

  const { kind, type, key } = file.name
  switch (kind) {
    case 'Annotation':
      return importAnnotation(db, scope, key!, file.content!, options, overlay)
    case 'Template':
      return importTemplate(db, scope, type!, file.content!, options)
    case 'Configuration':
      return importConfiguration(db, scope, key!, file.content!, options, overlay)
    default:
      return importSchema(db, scope, file.name, file.content!, options, overlay)
  }
}

type Counts = Required<Pick<ViewImportSummary, 'Created' | 'Updated' | 'Unchanged' | 'Skipped' | 'Failed' | 'Warnings'>>

const newCounts = (): Counts => ({ Created: 0, Updated: 0, Unchanged: 0, Skipped: 0, Failed: 0, Warnings: 0 })

function count(counts: Counts, result: ImportResult): void {
  counts[result.Outcome] += 1
  if (result.Outcome !== 'Unchanged' && result.Warnings?.length) {
    counts.Warnings += 1
  }
}

/**
 * Runs an import job (ViewTransferService.ImportAsync): the files of the included kinds are read, the annotations are
 * imported, the overlay is filled, and the schemas, templates and configurations follow. The items are every outcome
 * except Unchanged, in pass order; the summaries are one per included kind and the total.
 */
function runImport(db: MockDb, job: ImportJob): void {
  const { scope, state } = job
  const include = state.Include?.length ? state.Include : [...kinds]
  const options: ImportOptions = {
    existing: state.Existing ?? 'Overwrite',
    force: !!state.IsForced,
    dryRun: !!state.IsDryRun,
  }
  const { files, ignored, excluded } = readArchive(job.files, include)
  const ordered = files.toSorted(byPass)
  const stored = readStored(db, scope)
  const overlay: Overlay = {
    active: new Set(),
    recreated: new Set(),
    failed: new Set(),
    schemas: new Map(),
    configurations: new Map(),
  }
  const total = newCounts()
  const perKind = new Map(include.map((content) => [content, newCounts()]))
  const items: ViewImportItem[] = []
  let isOverlayFilled = false

  for (const file of ordered) {
    // after the annotation pass, the keys that the import makes active are known
    if (!isOverlayFilled && file.name.kind !== 'Annotation') {
      fillOverlay(ordered, stored, options, overlay)
      isOverlayFilled = true
    }

    const result = importFile(db, scope, file, options, overlay)
    track(file, result, overlay)
    count(total, result)
    count(perKind.get(getContent(file.name.kind))!, result)

    if (result.Outcome !== 'Unchanged') {
      items.push({
        Path: file.path,
        Kind: file.name.kind,
        AnnotationType: file.name.type,
        AnnotationKey: file.name.key,
        Outcome: result.Outcome,
        Version: result.Version,
        ErrorCode: result.ErrorCode,
        Message: result.Message,
        Warnings: result.Warnings,
      })
    }
  }

  job.state = {
    ...state,
    Status: 'Succeeded',
    CompletedAt: new Date().toISOString(),
    TotalCount: files.length,
    ProcessedCount: files.length,
    Summary: { ...total, ExcludedFiles: excluded, IgnoredFiles: ignored },
    Summaries: include.map((content) => ({
      Content: content,
      ...perKind.get(content)!,
      ExcludedFiles: 0,
      IgnoredFiles: 0,
    })),
    Items: items,
    AreItemsTruncated: false,
  }
  job.files = []
}

// ---------------------------------------------------------------------------------------------------------------------
// Routes

function findJob<T extends 'export' | 'import'>(kind: T, params: ScopeParams & { id: string }) {
  const job = jobs.get(params.id)
  return job && job.kind === kind && sameScope(job.scope, scopeOf(params))
    ? (job as Extract<ExportJob | ImportJob, { kind: T }>)
    : undefined
}

const notFound = (kind: string, id: string) =>
  errorResponse(404, `The ${kind} '${id}' does not exist or has expired.`, 'NotFound')

export const transferHandlers: HttpHandler[] = [
  http.post<ScopeParams>(
    `${api}/exports`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, `${params.org}.${params.project}`, 'Reader')
      if (denied) {
        return denied
      }
      const body = ((await request.json().catch(() => ({}))) ?? {}) as ViewExportCreate
      const format = body.Format === 'Git' ? 'Git' : 'Folder'
      const resolved = resolveInclude(body.Include ?? [])
      if ('unknown' in resolved) {
        return errorResponse(
          400,
          `Include has the unknown kind ${JSON.stringify(resolved.unknown)}. Use ${kindNames}, or an empty list for all four.`,
        )
      }
      const now = new Date()
      const id = newId()
      const state: ViewExport = {
        Id: id,
        Status: 'Queued',
        Format: format,
        Include: resolved.include,
        FileName: getArchiveFileName(params.org, params.project, params.view, format, now),
        CreatedAt: now.toISOString(),
        CreatedBy: mockActor(db),
        ExpiresAt: new Date(now.getTime() + day).toISOString(),
      }
      jobs.set(id, { kind: 'export', scope: scopeOf(params), state, reads: 0 })
      return HttpResponse.json(state, { status: 202 })
    }),
  ),

  http.get<ScopeParams & { id: string }>(
    `${api}/exports/:id`,
    withDb((db, { params }) => {
      const denied = requireRole(db, `${params.org}.${params.project}`, 'Reader')
      if (denied) {
        return denied
      }
      const job = findJob('export', params)
      if (!job) {
        return notFound('export', params.id)
      }
      job.reads += 1
      if (job.state.Status === 'Queued') {
        job.state = { ...job.state, Status: 'Running', StartedAt: new Date().toISOString() }
      } else if (job.state.Status === 'Running') {
        const include = job.state.Include ?? kinds
        const built = buildArchive(db, job.scope, job.state.Format ?? 'Folder', include, job.state.CreatedBy!)
        // the count of a kind the export left out is null, which the API leaves out of its answer
        const countOf = (content: ViewArchiveContent, value: number) => (include.includes(content) ? value : undefined)
        job.archive = built.zip
        job.state = {
          ...job.state,
          Status: 'Succeeded',
          CompletedAt: new Date().toISOString(),
          AnnotationCount: countOf('Annotations', built.annotations),
          SchemaCount: countOf('Schemas', built.schemas),
          TemplateCount: countOf('Templates', built.templates),
          ConfigurationCount: countOf('Configurations', built.configurations),
          VersionCount: built.versions,
          Size: built.zip.length,
        }
      }
      return HttpResponse.json(job.state)
    }),
  ),

  http.get<ScopeParams & { id: string }>(
    `${api}/exports/:id/archive`,
    withDb((db, { params }) => {
      const denied = requireRole(db, `${params.org}.${params.project}`, 'Reader')
      if (denied) {
        return denied
      }
      const job = findJob('export', params)
      if (!job) {
        return notFound('export', params.id)
      }
      if (!job.archive) {
        return errorResponse(
          409,
          `The export '${params.id}' is not ready yet (${job.state.Status}).`,
          'TransferNotReady',
        )
      }
      return new HttpResponse(job.archive, {
        headers: {
          'Content-Type': 'application/zip',
          'Content-Disposition': `attachment; filename=${job.state.FileName}`,
        },
      })
    }),
  ),

  http.post<ScopeParams>(
    `${api}/imports`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, `${params.org}.${params.project}`, 'Contributor')
      if (denied) {
        return denied
      }
      const query = new URL(request.url).searchParams
      // the options first, as in the API; include is comma-separated or repeated
      const resolved = resolveInclude(
        query
          .getAll('include')
          .flatMap((value) => value.split(','))
          .filter((value) => value.trim()),
      )
      if ('unknown' in resolved) {
        return errorResponse(
          400,
          `The query parameter 'include' has the unknown kind '${String(resolved.unknown)}'. Use ${kindNames}, comma-separated, or leave it empty for all four.`,
          'InvalidQuery',
        )
      }
      const existingQuery = query.get('existing')?.trim()
      const existing = existingQuery ? matchName(existingModes, existingQuery) : 'Overwrite'
      if (!existing) {
        return errorResponse(
          400,
          `The query parameter 'existing' must be Overwrite or Skip, not '${existingQuery}'.`,
          'InvalidQuery',
        )
      }
      const bytes = new Uint8Array(await request.arrayBuffer())
      let files: ZipFile[]
      try {
        files = await readZip(bytes)
      } catch {
        return errorResponse(400, 'The body is not a zip archive.', 'InvalidArchive')
      }
      const now = new Date()
      const id = newId()
      const state: ViewImport = {
        Id: id,
        Status: 'Queued',
        FileName: query.get('fileName') ?? undefined,
        Include: resolved.include,
        Existing: existing,
        IsDryRun: query.get('dryRun') === 'true',
        IsForced: query.get('force') === 'true',
        CreatedAt: now.toISOString(),
        CreatedBy: mockActor(db),
        ExpiresAt: new Date(now.getTime() + day).toISOString(),
      }
      jobs.set(id, { kind: 'import', scope: scopeOf(params), state, reads: 0, files })
      return HttpResponse.json(state, { status: 202 })
    }),
  ),

  http.get<ScopeParams & { id: string }>(
    `${api}/imports/:id`,
    withDb((db, { params }) => {
      const denied = requireRole(db, `${params.org}.${params.project}`, 'Contributor')
      if (denied) {
        return denied
      }
      const job = findJob('import', params)
      if (!job) {
        return notFound('import', params.id)
      }
      job.reads += 1
      if (job.state.Status === 'Queued') {
        job.state = { ...job.state, Status: 'Running', StartedAt: new Date().toISOString() }
      } else if (job.state.Status === 'Running') {
        runImport(db, job)
      }
      return HttpResponse.json(job.state)
    }),
  ),
]
