import { http, HttpResponse, type HttpHandler } from 'msw'
import type {
  ConfigurationExport,
  ConfigurationExportCreate,
  ConfigurationImport,
  ConfigurationImportItem,
} from '@42for.net/2splatform.sdk'
import { getAnnotationTypeInfo, type AnnotationTypeCode } from '@/utils/annotation-key'
import { appendAudit, mockActor, newEntry } from './audit'
import { combinedSchema, ownContent, schemaErrors } from './configuration-handlers'
import { annotationsOf, overridesOf, type MockDb } from './db'
import { documentIds, getDocument, putVersion, type ScopeRef } from './documents'
import { errorResponse, requireRole, withDb } from './handlers'
import { createZip, readZip, textFile, type ZipFile } from './zip'

// Exports and imports of configurations (spec 4.4). A job is Running on its first read and ends on the second one,
// so the UI shows its progress. An export is always the folder layout (a git export only adds .git/HEAD; the mock
// writes no repository); an import writes the documents like the API, with the same outcomes.

const api = '*/api/v1/:org/:project/:view/configurations'
const day = 86_400_000

type ScopeParams = { org: string; project: string; view: string }

interface ExportJob {
  kind: 'export'
  scope: ScopeRef
  state: ConfigurationExport
  reads: number
  archive?: Uint8Array<ArrayBuffer>
}

interface ImportJob {
  kind: 'import'
  scope: ScopeRef
  state: ConfigurationImport
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

const reserved = new Set(['%', '<', '>', ':', '"', '/', '\\', '|', '?', '*'])

const mustEncode = (character: string) => {
  const code = character.codePointAt(0)!
  return reserved.has(character) || code < 0x20 || code === 0x7f
}

const encode = (character: string) =>
  [...new TextEncoder().encode(character)]
    .map((byte) => `%${byte.toString(16).toUpperCase().padStart(2, '0')}`)
    .join('')

/** <key>.json or <key>.<version>.json, each segment percent-encoded like the API (export-import.md). */
export function fileNameOf(key: string, version?: number): string {
  const segments = key
    .split('.')
    .map((segment) => [...segment].map((character) => (mustEncode(character) ? encode(character) : character)).join(''))
  return `${segments.join('.')}${version === undefined ? '' : `.${version}`}.json`
}

/** The key and the version of a file name, or undefined for any other file. */
export function parseFileName(name: string): { key: string; version?: number } | undefined {
  if (!name.toLowerCase().endsWith('.json')) {
    return undefined
  }

  const segments = name.slice(0, -5).split('.')
  const info = getAnnotationTypeInfo(segments[0] as AnnotationTypeCode)

  if (!info || segments.some((segment) => !segment)) {
    return undefined
  }

  let version: number | undefined
  if (segments.length === info.segments + 1) {
    if (!/^\d+$/.test(segments.at(-1)!)) {
      return undefined
    }
    version = Number(segments.pop())
  } else if (segments.length !== info.segments) {
    return undefined
  }

  try {
    const decoded = segments.map((segment) => decodeURIComponent(segment))
    return decoded.some((segment) => segment.includes('.')) ? undefined : { key: decoded.join('.'), version }
  } catch {
    return undefined
  }
}

const isDeleted = (db: MockDb, scope: ScopeRef, key: string) =>
  !!overridesOf(db, scope.org, scope.project, scope.view).trash?.[key]

const json = (value: unknown) => `${JSON.stringify(value, null, 2)}\n`

function buildArchive(
  db: MockDb,
  scope: ScopeRef,
  format: 'Folder' | 'Git',
): { zip: Uint8Array<ArrayBuffer>; configurations: number; versions: number } {
  const files: ZipFile[] = []
  const manifest: unknown[] = []
  let configurations = 0
  let versions = 0

  for (const id of documentIds(db, scope, 'configuration:').toSorted()) {
    const key = id.slice('configuration:'.length)
    const stored = (getDocument(db, scope, id)?.versions ?? []).filter((version) => version.Content)

    if (isDeleted(db, scope, key) || stored.length === 0) {
      continue
    }

    const current = stored.at(-1)!
    configurations += 1
    versions += stored.length

    for (const version of stored) {
      const isCurrent = version === current
      if (format === 'Folder' || isCurrent) {
        files.push(textFile(fileNameOf(key, isCurrent ? undefined : version.Version), json(version.Content)))
      }
    }

    manifest.push({ AnnotationKey: key, File: fileNameOf(key), Version: current.Version, Author: current.Modified.By?.Name ?? current.Modified.By?.Id })
  }

  if (format === 'Git') {
    files.push(textFile('.git/HEAD', 'ref: refs/heads/main\n'))
  } else {
    files.push(textFile('export.json', json({ Format: 'Folder', ...scope, Configurations: manifest })))
  }

  return { zip: createZip(files), configurations, versions }
}

async function runImport(db: MockDb, job: ImportJob): Promise<void> {
  const { scope, state } = job
  const annotations = annotationsOf(db, scope.org, scope.project, scope.view)
  const summary = { Created: 0, Updated: 0, Unchanged: 0, Skipped: 0, Failed: 0, IgnoredFiles: 0 }
  const items: ConfigurationImportItem[] = []
  const seen = new Map<string, string>()

  const report = (item: ConfigurationImportItem) => {
    summary[item.Outcome!] += 1
    if (item.Outcome !== 'Unchanged') {
      items.push(item)
    }
  }

  for (const file of job.files.toSorted((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))) {
    const segments = file.name.split('/')
    if (
      segments.some((segment) => segment === '.git') ||
      segments[0] === '__MACOSX' ||
      segments.at(-1)!.startsWith('._')
    ) {
      continue
    }

    const parsed = parseFileName(segments.at(-1)!)
    if (!parsed || parsed.version !== undefined) {
      summary.IgnoredFiles += 1
      continue
    }

    const base = { Path: file.name, AnnotationKey: parsed.key }
    if (seen.has(parsed.key)) {
      report({
        ...base,
        Outcome: 'Failed',
        ErrorCode: 'DuplicateFile',
        Message: `The file '${seen.get(parsed.key)}' already holds the configuration of ${parsed.key}.`,
      })
      continue
    }
    seen.set(parsed.key, file.name)

    let content: unknown
    try {
      content = JSON.parse(new TextDecoder().decode(file.data))
    } catch (error) {
      report({
        ...base,
        Outcome: 'Failed',
        ErrorCode: 'InvalidDocument',
        Message: `The file is not a JSON object: ${(error as Error).message}`,
      })
      continue
    }

    if (!content || typeof content !== 'object' || Array.isArray(content)) {
      report({ ...base, Outcome: 'Failed', ErrorCode: 'InvalidDocument', Message: 'The file is not a JSON object.' })
    } else if (isDeleted(db, scope, parsed.key)) {
      report({
        ...base,
        Outcome: 'Skipped',
        ErrorCode: 'AnnotationDeleted',
        Message: `The annotation ${parsed.key} is deleted; restore it before importing its configuration.`,
      })
    } else if (!annotations.has(parsed.key)) {
      report({
        ...base,
        Outcome: 'Skipped',
        ErrorCode: 'AnnotationNotFound',
        Message: `The view ${scope.view} has no annotation ${parsed.key}.`,
      })
    } else if (Object.keys(content).length === 0) {
      report({
        ...base,
        Outcome: 'Skipped',
        ErrorCode: 'EmptyDocument',
        Message: 'An empty document is not imported; delete the configuration to clear it.',
      })
    } else {
      const stored = ownContent(db, scope, parsed.key)
      const storedVersion = getDocument(db, scope, `configuration:${parsed.key}`)?.versions.at(-1)?.Version
      const errors = state.IsForced ? [] : schemaErrors(combinedSchema(db, scope, parsed.key)?.MergedContent, content)

      if (stored && JSON.stringify(stored) === JSON.stringify(content)) {
        report({ ...base, Outcome: 'Unchanged', Version: storedVersion })
      } else if (errors.length) {
        report({
          ...base,
          Outcome: 'Failed',
          ErrorCode: 'SchemaViolation',
          Message: `The document violates its schema: ${errors.join('; ')}`,
        })
      } else {
        const outcome = stored ? 'Updated' : 'Created'
        if (state.IsDryRun) {
          report({ ...base, Outcome: outcome })
        } else {
          const saved = putVersion(db, scope, `configuration:${parsed.key}`, content as Record<string, unknown>)
          appendAudit(
            db,
            scope,
            parsed.key,
            newEntry(db, 'ConfigurationChanged', { ConfigurationVersion: saved.Version }),
          )
          report({ ...base, Outcome: outcome, Version: saved.Version })
        }
      }
    }
  }

  const processed = summary.Created + summary.Updated + summary.Unchanged + summary.Skipped + summary.Failed
  job.state = {
    ...state,
    Status: 'Succeeded',
    CompletedAt: new Date().toISOString(),
    TotalCount: processed,
    ProcessedCount: processed,
    Summary: summary,
    Items: items,
    AreItemsTruncated: false,
  }
  job.files = []
}

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
      const body = (await request.json().catch(() => ({}))) as ConfigurationExportCreate
      const format = body.Format === 'Git' ? 'Git' : 'Folder'
      const now = new Date()
      const stamp = now.toISOString().replaceAll('-', '').replace('T', '-').replaceAll(':', '').slice(0, 15)
      const id = newId()
      const state: ConfigurationExport = {
        Id: id,
        Status: 'Queued',
        Format: format,
        FileName: `configurations-${params.org}-${params.project}-${params.view}${format === 'Git' ? '-git' : ''}-${stamp}Z.zip`,
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
        const built = buildArchive(db, job.scope, job.state.Format === 'Git' ? 'Git' : 'Folder')
        job.archive = built.zip
        job.state = {
          ...job.state,
          Status: 'Succeeded',
          CompletedAt: new Date().toISOString(),
          ConfigurationCount: built.configurations,
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
        return errorResponse(409, `The export '${params.id}' is not ready yet (${job.state.Status}).`, 'TransferNotReady')
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
      const bytes = new Uint8Array(await request.arrayBuffer())
      let files: ZipFile[]
      try {
        files = await readZip(bytes)
      } catch {
        return errorResponse(400, 'The body is not a zip archive.', 'InvalidArchive')
      }
      const query = new URL(request.url).searchParams
      const now = new Date()
      const id = newId()
      const state: ConfigurationImport = {
        Id: id,
        Status: 'Queued',
        FileName: query.get('fileName') ?? undefined,
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
    withDb(async (db, { params }) => {
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
        await runImport(db, job)
      }
      return HttpResponse.json(job.state)
    }),
  ),
]
