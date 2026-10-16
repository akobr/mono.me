import { http, HttpResponse, type HttpHandler } from 'msw'
import type {
  Annotation,
  AnnotationAuditCause,
  AnnotationDeletion,
  AnnotationKeyFailure,
  AnnotationKeysRequest,
  AnnotationRestoreRequest,
} from '@42for.net/2splatform.sdk'
import {
  codeOfKey,
  getAllAncestors,
  getAnnotationTypeInfo,
  getDirectAncestors,
  isValidAnnotationKey,
  parseAnnotationKey,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { annotationStatus } from '@/utils/annotation-status'
import {
  appendAudit,
  auditOf,
  diffAnnotations,
  dropAudit,
  mockActor,
  newEntry,
  retentionDays,
  stamp,
  type MockScope,
} from './audit'
import { annotationsOf, overridesOf, type AnnotationOverrides, type MockDb } from './db'
import { deleteDocument } from './documents'
import { errorResponse, requireRole, withDb } from './handlers'
import { keyOfSimpleLine } from './simple-lines'

const api = '*/api/v1/:org/:project/:view'
const pageSize = 1000
const maxKeys = 100
const day = 24 * 3600 * 1000

type ScopeParams = { org: string; project: string; view: string }

const projectKey = (params: ScopeParams) => `${params.org}.${params.project}`
const scopeOf = (params: ScopeParams): MockScope => ({ org: params.org, project: params.project, view: params.view })

/** One page of a list, with the offset as the continuation token (like the API's 1000-item pages). */
function page<T>(items: T[], request: Request, wrap: (items: T[], token?: string) => Record<string, unknown>) {
  const offset = Number(new URL(request.url).searchParams.get('continuationToken') ?? 0) || 0
  const slice = items.slice(offset, offset + pageSize)
  const next = offset + pageSize < items.length ? String(offset + pageSize) : undefined
  return HttpResponse.json(wrap(slice, next) as never)
}

const listOf = (db: MockDb, params: ScopeParams, code?: AnnotationTypeCode) =>
  [...annotationsOf(db, params.org, params.project, params.view).values()].filter(
    (annotation) => !code || codeOfKey(annotation.AnnotationKey) === code,
  )

/** The include flags of the annotation reads; without them the API returns only active annotations. */
function includesOf(request: Request) {
  const query = new URL(request.url).searchParams
  return {
    inactive: query.get('includeInactive') === 'true',
    deleted: query.get('includeDeleted') === 'true',
  }
}

/**
 * A list read like the API's: active annotations, plus the inactive ones with `includeInactive` and the deleted
 * ones with `includeDeleted` (a deleted one that is not active needs both).
 */
function readableOf(db: MockDb, params: ScopeParams, request: Request, code?: AnnotationTypeCode) {
  const include = includesOf(request)
  const now = Date.now()
  const deleted = include.deleted
    ? Object.values(overridesOf(db, params.org, params.project, params.view).trash ?? {}).filter(
        (annotation) => !code || codeOfKey(annotation.AnnotationKey) === code,
      )
    : []
  return [...listOf(db, params, code), ...deleted].filter(
    (annotation) => include.inactive || annotationStatus(annotation, now) === 'active',
  )
}

const perType: [string, AnnotationTypeCode][] = [
  ['responsibilities', 'rst'],
  ['subjects', 'sbt'],
  ['usages', 'usg'],
  ['contexts', 'cnt'],
  ['executions', 'exe'],
  ['units', 'unt'],
  ['units-of-execution', 'uxe'],
]

const typeNames: Record<AnnotationTypeCode, Annotation['AnnotationType']> = {
  rst: 'Responsibility',
  unt: 'Unit',
  sbt: 'Subject',
  usg: 'Usage',
  cnt: 'Context',
  exe: 'Execution',
  uxe: 'UnitOfExecution',
}

/** The longest path to a root of the graph (AnnotationKeyExtensions.GetDepth). */
const depths: Record<AnnotationTypeCode, number> = { rst: 0, sbt: 0, unt: 1, usg: 1, cnt: 1, exe: 2, uxe: 3 }
const depthOf = (key: string) => depths[codeOfKey(key) ?? 'rst']

/** The AnnotationType enum value (rst, unt, sbt, usg, cnt, exe, uxe): the order CreateAnnotationsAsync writes in. */
const mergeOrderOf = (key: string) => getAnnotationTypeInfo(key.slice(0, key.indexOf('.')))?.mergeOrder ?? 0

/** The order of GetAllAncestorKeys: by depth, then type, then an ordinal comparison of the key (roots first). */
export const byDepthTypeKey = (a: string, b: string) =>
  depthOf(a) - depthOf(b) || mergeOrderOf(a) - mergeOrderOf(b) || (a < b ? -1 : a > b ? 1 : 0)

const isDescendant = (candidate: string, key: string) => candidate !== key && getAllAncestors(candidate).includes(key)

const trashOf = (overrides: AnnotationOverrides) => (overrides.trash ??= {})

/** The catalog gets the annotation back (or for the first time); a deletion mark of the key goes away. */
function put(overrides: AnnotationOverrides, annotation: Annotation): void {
  overrides.upserts[annotation.AnnotationKey] = annotation
  overrides.deleted = overrides.deleted.filter((key) => key !== annotation.AnnotationKey)
}

function remember(overrides: AnnotationOverrides, key: string): void {
  delete overrides.upserts[key]
  if (!overrides.deleted.includes(key)) {
    overrides.deleted.push(key)
  }
}

/**
 * A deleted key written again is re-created: it leaves Deleted without its configuration, and the children
 * deleted with it stay deleted as roots of Deleted.
 */
function leaveTrashRecreated(db: MockDb, params: ScopeParams, overrides: AnnotationOverrides, key: string): void {
  const trash = trashOf(overrides)
  const deletionId = trash[key]?.Deletion?.DeletionId
  delete trash[key]
  deleteDocument(db, scopeOf(params), `configuration:${key}`)
  markChildrenAsRoots(trash, key, deletionId)
}

function markChildrenAsRoots(trash: Record<string, Annotation>, key: string, deletionId: string | undefined): void {
  for (const [childKey, child] of Object.entries(trash)) {
    if (child.Deletion?.DeletionId === deletionId && getDirectAncestors(childKey).includes(key)) {
      trash[childKey] = { ...child, Deletion: { ...child.Deletion, IsRoot: true } }
    }
  }
}

function defaultAncestor(params: ScopeParams, key: string, from: Annotation): Annotation {
  const parsed = parseAnnotationKey(key)!
  return withRelations({
    ProjectName: params.project,
    ViewName: params.view,
    AnnotationKey: key,
    Name: parsed.name,
    AnnotationType: typeNames[parsed.code],
    TimeZone: from.TimeZone,
    Labels: from.Labels,
  })
}

/**
 * Stores an annotation like SetAnnotation: a missing key is created with its missing ancestors, an active one is
 * updated (nothing is written without a change), and a deleted key or ancestor is re-created empty. The import of an
 * archive writes through it too (transfer-handlers.ts).
 */
export function storeAnnotation(db: MockDb, params: ScopeParams, annotation: Annotation): Annotation {
  const scope = scopeOf(params)
  const overrides = overridesOf(db, params.org, params.project, params.view)
  const trash = trashOf(overrides)
  const existing = annotationsOf(db, params.org, params.project, params.view)
  const key = annotation.AnnotationKey
  const now = new Date()

  for (const ancestor of getAllAncestors(key)) {
    if (existing.has(ancestor)) {
      continue
    }
    const deletionId = trash[ancestor]?.Deletion?.DeletionId
    const cause: AnnotationAuditCause = { Kind: 'Ancestor', Key: key }
    put(overrides, {
      ...defaultAncestor(params, ancestor, annotation),
      Created: stamp(db, now),
      Modified: stamp(db, now),
    })
    appendAudit(
      db,
      scope,
      ancestor,
      newEntry(db, deletionId ? 'Recreated' : 'Created', { Cause: cause, DeletionId: deletionId }),
    )
    if (deletionId) {
      leaveTrashRecreated(db, params, overrides, ancestor)
    }
  }

  const previous = existing.get(key)
  const { Created: _created, Modified: _modified, Deletion: _deletion, ...body } = annotation
  const stored = withRelations({
    ...body,
    ProjectName: params.project,
    ViewName: params.view,
    Created: previous?.Created ?? stamp(db, now),
    Modified: stamp(db, now),
  })

  if (previous) {
    const changes = diffAnnotations(previous, stored)
    if (!changes) {
      return previous
    }
    appendAudit(db, scope, key, newEntry(db, 'Updated', changes))
  } else {
    const deletionId = trash[key]?.Deletion?.DeletionId
    appendAudit(db, scope, key, newEntry(db, deletionId ? 'Recreated' : 'Created', { DeletionId: deletionId }))
    if (deletionId) {
      leaveTrashRecreated(db, params, overrides, key)
    }
  }

  put(overrides, stored)
  return stored
}

/** The members the API's runtime types declare as required (Usage.cs, Execution.cs, …), besides the base fields. */
const requiredMembers: Record<AnnotationTypeCode, string[]> = {
  rst: [],
  sbt: [],
  unt: ['ResponsibilityKey', 'ResponsibilityName', 'UnitType', 'UnitDefinition'],
  cnt: ['SubjectKey', 'SubjectName'],
  usg: ['SubjectKey', 'ResponsibilityKey', 'SubjectName', 'ResponsibilityName'],
  exe: [
    'ResponsibilityKey',
    'SubjectKey',
    'ContextKey',
    'UsageKey',
    'SubjectName',
    'ResponsibilityName',
    'ContextName',
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

// The limits of AnnotationRules: a violation answers 400 InvalidAnnotation and names the field.
const limits: [keyof Annotation, number][] = [
  ['Title', 200],
  ['Description', 10_000],
  ['DocumentationLink', 2048],
]

/** The problems AnnotationRules finds, one sentence each; none for a valid annotation. */
export function annotationProblems(annotation: Annotation): string[] {
  const record = annotation as unknown as Record<string, unknown>
  return limits
    .map(([field, max]) => [field, String(record[field] ?? '').length, max] as const)
    .filter(([, length, max]) => length > max)
    .map(([field, length, max]) => `${field} has ${length} characters, at most ${max} are allowed.`)
}

function invalidAnnotation(annotation: Annotation, key?: string): Response | undefined {
  if (!isValidAnnotationKey(annotation.AnnotationKey ?? '')) {
    return errorResponse(400, `The annotation key '${annotation.AnnotationKey}' is not valid.`, 'InvalidKey')
  }
  if (key && annotation.AnnotationKey !== key) {
    return errorResponse(400, 'The annotation key in the body does not match the route.', 'InvalidInput')
  }
  // Like System.Text.Json with the API's runtime types: a body without the required members is rejected.
  const record = annotation as unknown as Record<string, unknown>
  const missing = requiredMembers[codeOfKey(annotation.AnnotationKey)!]?.filter((member) => !record[member])
  if (missing?.length) {
    return errorResponse(400, `Invalid annotation object in body: missing ${missing.join(', ')}.`, 'InvalidInput')
  }
  const problems = annotationProblems(annotation)
  if (problems.length) {
    return errorResponse(
      400,
      `The annotation '${annotation.AnnotationKey}' is not valid: ${problems.join(' ')}`,
      'InvalidAnnotation',
    )
  }
  return undefined
}

function keysOf(body: AnnotationKeysRequest | AnnotationRestoreRequest | null): string[] | Response {
  const keys = body?.Keys ?? []
  if (keys.length === 0 || keys.length > maxKeys) {
    return errorResponse(400, `Send between 1 and ${maxKeys} annotation keys.`, 'InvalidKey')
  }
  const invalid = keys.filter((key) => !isValidAnnotationKey(key))
  return invalid.length ? errorResponse(400, `Invalid annotation keys: ${invalid.join(', ')}`, 'InvalidKey') : keys
}

/** Soft delete of the key and its active descendants, like DeleteAnnotationAsync. */
function softDelete(db: MockDb, params: ScopeParams, key: string): Response {
  const scope = scopeOf(params)
  const overrides = overridesOf(db, params.org, params.project, params.view)
  const trash = trashOf(overrides)
  const active = annotationsOf(db, params.org, params.project, params.view)

  const existing = trash[key]?.Deletion
  if (existing) {
    return HttpResponse.json({ Deletion: existing, DeletedKeys: [] })
  }
  if (!active.has(key)) {
    return errorResponse(404, `The annotation ${key} does not exist.`, 'NotFound')
  }

  const descendants = [...active.keys()]
    .filter((candidate) => isDescendant(candidate, key))
    .toSorted((a, b) => depthOf(b) - depthOf(a) || a.localeCompare(b))
  const now = new Date()
  const deletion: AnnotationDeletion = {
    DeletionId: `del_${now.getTime().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`,
    RootKey: key,
    DeletedAt: now.toISOString(),
    DeletedBy: mockActor(db),
    IsRoot: false,
    DescendantCount: null,
    PurgeAt: new Date(now.getTime() + retentionDays * day).toISOString(),
  }

  for (const deletedKey of [...descendants, key]) {
    const isRoot = deletedKey === key
    trash[deletedKey] = {
      ...active.get(deletedKey)!,
      Deletion: { ...deletion, IsRoot: isRoot, DescendantCount: isRoot ? descendants.length : null },
    }
    remember(overrides, deletedKey)
    appendAudit(
      db,
      scope,
      deletedKey,
      newEntry(db, 'Deleted', {
        DeletionId: deletion.DeletionId,
        Cause: isRoot ? undefined : { Kind: 'Cascade', Key: key },
        DescendantCount: isRoot ? descendants.length : undefined,
      }),
    )
  }

  return HttpResponse.json({
    Deletion: { ...deletion, IsRoot: true, DescendantCount: descendants.length },
    DeletedKeys: [...descendants, key],
  })
}

/** RestoreAnnotationsAsync: ancestors first; a failure of one key does not stop the others. */
function restore(db: MockDb, params: ScopeParams, keys: string[], withDescendants: boolean) {
  const scope = scopeOf(params)
  const overrides = overridesOf(db, params.org, params.project, params.view)
  const trash = trashOf(overrides)
  const restored: Annotation[] = []
  const createdKeys: string[] = []
  const failed: AnnotationKeyFailure[] = []
  const handled = new Set<string>()
  const now = new Date()

  const restoreOne = (key: string, cause?: AnnotationAuditCause, descendantCount?: number) => {
    const deleted = trash[key]!
    delete trash[key]
    const { Deletion, ...annotation } = deleted
    const back = { ...annotation, Modified: stamp(db, now) }
    put(overrides, back)
    appendAudit(
      db,
      scope,
      key,
      newEntry(db, 'Restored', { DeletionId: Deletion?.DeletionId, Cause: cause, DescendantCount: descendantCount }),
    )
    restored.push(back)
    handled.add(key)
  }

  const ordered = [...new Set(keys)].toSorted((a, b) => depthOf(a) - depthOf(b) || a.localeCompare(b))

  for (const key of ordered) {
    if (handled.has(key)) {
      continue
    }

    const active = annotationsOf(db, params.org, params.project, params.view)
    const target = trash[key]
    if (!target && !active.has(key)) {
      failed.push({
        Key: key,
        ErrorCode: 'NotFound',
        Message: `The annotation '${key}' does not exist or was deleted permanently.`,
      })
      continue
    }

    // keys restored without their deleted children, with the deletion they came from
    const alone = new Map<string, string | undefined>()
    const ancestorCause: AnnotationAuditCause = { Kind: 'Ancestor', Key: key }

    for (const ancestor of getAllAncestors(key)) {
      if (active.has(ancestor) || handled.has(ancestor)) {
        continue
      }
      if (trash[ancestor]) {
        alone.set(ancestor, trash[ancestor].Deletion?.DeletionId)
        restoreOne(ancestor, ancestorCause)
      } else {
        const created = {
          ...defaultAncestor(params, ancestor, target ?? active.get(key)!),
          Created: stamp(db, now),
          Modified: stamp(db, now),
        }
        put(overrides, created)
        appendAudit(db, scope, ancestor, newEntry(db, 'Created', { Cause: ancestorCause }))
        restored.push(created)
        createdKeys.push(ancestor)
        handled.add(ancestor)
      }
    }

    const deletionId = target?.Deletion?.DeletionId
    const descendants =
      withDescendants && deletionId
        ? Object.keys(trash)
            .filter(
              (candidate) => trash[candidate]!.Deletion?.DeletionId === deletionId && isDescendant(candidate, key),
            )
            .toSorted((a, b) => depthOf(a) - depthOf(b) || a.localeCompare(b))
        : []

    if (target) {
      restoreOne(key, undefined, withDescendants ? descendants.length : undefined)
      if (!withDescendants) {
        alone.set(key, deletionId)
      }
    }

    descendants.forEach((descendant) => restoreOne(descendant, { Kind: 'Cascade', Key: key }))

    for (const [parent, parentDeletionId] of alone) {
      markChildrenAsRoots(trash, parent, parentDeletionId)
    }
    handled.add(key)
  }

  return { Annotations: restored, CreatedKeys: createdKeys, Failed: failed }
}

function purge(db: MockDb, params: ScopeParams, key: string): Response {
  const scope = scopeOf(params)
  const overrides = overridesOf(db, params.org, params.project, params.view)
  const trash = trashOf(overrides)

  if (annotationsOf(db, params.org, params.project, params.view).has(key)) {
    return errorResponse(
      409,
      `The annotation '${key}' is not deleted; delete it before deleting it permanently.`,
      'AnnotationNotDeleted',
    )
  }
  if (!trash[key]) {
    return errorResponse(404, `The annotation ${key} does not exist.`, 'NotFound')
  }

  const purged = [...Object.keys(trash).filter((candidate) => isDescendant(candidate, key)), key]
  for (const purgedKey of purged) {
    delete trash[purgedKey]
    dropAudit(db, scope, purgedKey)
    deleteDocument(db, scope, `configuration:${purgedKey}`)
    remember(overrides, purgedKey)
  }
  return HttpResponse.json({ PurgedKeys: purged })
}

export const annotationHandlers: HttpHandler[] = [
  ...perType.map(([segment, code]) =>
    http.get<ScopeParams>(
      `${api}/${segment}`,
      withDb((db, { params, request }) => {
        const denied = requireRole(db, projectKey(params), 'Reader')
        return (
          denied ??
          page(readableOf(db, params, request, code), request, (items, token) => ({
            Annotations: items,
            ContinuationToken: token,
            Count: items.length,
          }))
        )
      }),
    ),
  ),

  http.get<ScopeParams>(
    `${api}/annotations`,
    withDb((db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      return (
        denied ??
        page(readableOf(db, params, request), request, (items, token) => ({
          Annotations: items,
          ContinuationToken: token,
          Count: items.length,
        }))
      )
    }),
  ),

  http.post<ScopeParams>(
    `${api}/annotations`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }

      const body = (await request.json()) as Annotation[]
      for (const annotation of body) {
        const invalid = invalidAnnotation(annotation)
        if (invalid) {
          return invalid
        }
      }
      // Like CreateAnnotationsAsync: an existing key is skipped, the created annotations come back.
      const existing = annotationsOf(db, params.org, params.project, params.view)
      const created = body
        .filter((annotation) => !existing.has(annotation.AnnotationKey))
        .map((annotation) => storeAnnotation(db, params, annotation))
      return HttpResponse.json(created)
    }),
  ),

  // SetAnnotationsSimple, like CreateAnnotationsFromStringAsync: each entry is read into a key (keyOfSimpleLine),
  // the keys are created in type order with their missing ancestors, and existing or repeated keys are skipped.
  // The response lists, per key, the ancestors created for it (roots first) and then the key. The literal segment
  // "simple" wins over {key}, like the API's route table.
  http.post<ScopeParams>(
    `${api}/annotations/simple`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }

      const body: unknown = await request.json().catch(() => null)
      if (!Array.isArray(body)) {
        // the server dereferences the null list
        return errorResponse(500, 'Object reference not set to an instance of an object.')
      }
      if (body.length > maxKeys) {
        return errorResponse(400, `The number of annotations to create is too large, maximum allowed is ${maxKeys}.`)
      }

      const keys = body
        .map((entry) => (typeof entry === 'string' ? keyOfSimpleLine(entry) : undefined))
        .filter((key): key is string => !!key)
        .toSorted((a, b) => mergeOrderOf(a) - mergeOrderOf(b))
      const created: Annotation[] = []
      const handled = new Set<string>()

      for (const key of keys) {
        // annotationsOf is rebuilt after every write, so an ancestor created for an earlier key counts as active
        const active = annotationsOf(db, params.org, params.project, params.view)
        if (handled.has(key) || active.has(key)) {
          continue
        }

        const missing = getAllAncestors(key).filter((ancestor) => !active.has(ancestor))
        // only what the key gives; storeAnnotation() adds the relations
        const parsed = parseAnnotationKey(key)!
        const stored = storeAnnotation(db, params, {
          ProjectName: params.project,
          ViewName: params.view,
          AnnotationKey: key,
          Name: parsed.name,
          AnnotationType: typeNames[parsed.code],
        })
        const after = annotationsOf(db, params.org, params.project, params.view)
        created.push(...missing.toSorted(byDepthTypeKey).map((ancestor) => after.get(ancestor)!), stored)
        for (const written of [...missing, key]) {
          handled.add(written)
        }
      }

      return HttpResponse.json(created)
    }),
  ),

  // The literal segment "deleted" wins over {key}, like the API's route table.
  http.get<ScopeParams>(
    `${api}/annotations/deleted`,
    withDb((db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const query = new URL(request.url).searchParams
      const roots = query.get('roots') !== 'false'
      const deletionId = query.get('deletionId')
      const items = Object.values(trashOf(overridesOf(db, params.org, params.project, params.view)))
        .filter(
          (annotation) =>
            (!roots || annotation.Deletion?.IsRoot) && (!deletionId || annotation.Deletion?.DeletionId === deletionId),
        )
        .toSorted(
          (a, b) =>
            Date.parse(b.Deletion?.DeletedAt ?? '') - Date.parse(a.Deletion?.DeletedAt ?? '') ||
            a.AnnotationKey.localeCompare(b.AnnotationKey),
        )
      return page(items, request, (slice, token) => ({
        Annotations: slice,
        ContinuationToken: token,
        Count: slice.length,
      }))
    }),
  ),

  http.get<ScopeParams & { key: string }>(
    `${api}/annotations/deleted/:key`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const deleted = overridesOf(db, params.org, params.project, params.view).trash?.[params.key]
      return deleted
        ? HttpResponse.json(deleted)
        : errorResponse(404, `The annotation ${params.key} is not deleted.`, 'NotFound')
    }),
  ),

  http.post<ScopeParams>(
    `${api}/annotations/deleted/lookup`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const keys = keysOf((await request.json()) as AnnotationKeysRequest)
      if (keys instanceof Response) {
        return keys
      }
      const trash = overridesOf(db, params.org, params.project, params.view).trash ?? {}
      return HttpResponse.json(keys.map((key) => trash[key]).filter((annotation) => !!annotation))
    }),
  ),

  http.post<ScopeParams>(
    `${api}/annotations/deleted/restore`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as AnnotationRestoreRequest
      const keys = keysOf(body)
      if (keys instanceof Response) {
        return keys
      }
      return HttpResponse.json(restore(db, params, keys, body.Descendants !== false))
    }),
  ),

  http.delete<ScopeParams & { key: string }>(
    `${api}/annotations/deleted/:key`,
    withDb((db, { params }) => requireRole(db, projectKey(params), 'Administrator') ?? purge(db, params, params.key)),
  ),

  http.get<ScopeParams & { key: string }>(
    `${api}/annotations/:key/history`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const active = annotationsOf(db, params.org, params.project, params.view).get(params.key)
      const deleted = overridesOf(db, params.org, params.project, params.view).trash?.[params.key]
      const annotation = active ?? deleted
      if (!annotation) {
        return errorResponse(404, `The annotation ${params.key} has no history.`, 'NotFound')
      }

      const entries = [...(auditOf(db, scopeOf(params), params.key) ?? [])]
      // The seeded annotations were created before the mock recorded anything.
      if (entries[0]?.Action !== 'Created') {
        entries.unshift({
          Id: `adt_seed_${params.key}`,
          At: annotation.Created?.At ?? new Date(Date.now() - 30 * day).toISOString(),
          Action: 'Created',
          Actor: { Kind: 'System' },
        })
      }
      return HttpResponse.json({
        AnnotationKey: params.key,
        State: deleted ? 'Deleted' : 'Active',
        Entries: entries.toReversed(),
        IsTrimmed: false,
      })
    }),
  ),

  http.get<ScopeParams & { key: string }>(
    `${api}/annotations/:key`,
    withDb((db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const include = includesOf(request)
      const annotation =
        annotationsOf(db, params.org, params.project, params.view).get(params.key) ??
        (include.deleted ? overridesOf(db, params.org, params.project, params.view).trash?.[params.key] : undefined)
      if (!annotation) {
        return errorResponse(404, `The annotation ${params.key} does not exist.`, 'NotFound')
      }
      const status = annotationStatus(annotation)
      return status === 'active' || include.inactive
        ? HttpResponse.json(annotation)
        : errorResponse(
            404,
            `The annotation '${params.key}' is ${status}; read it with includeInactive=true.`,
            'AnnotationInactive',
          )
    }),
  ),

  http.post<ScopeParams & { key: string }>(
    `${api}/annotations/:key`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }

      const body = (await request.json()) as Annotation
      const invalid = invalidAnnotation(body, params.key)
      if (invalid) {
        return invalid
      }
      const parsed = parseAnnotationKey(params.key)!
      return HttpResponse.json(storeAnnotation(db, params, { ...body, Name: parsed.name }))
    }),
  ),

  http.delete<ScopeParams & { key: string }>(
    `${api}/annotations/:key`,
    withDb(
      (db, { params }) => requireRole(db, projectKey(params), 'Contributor') ?? softDelete(db, params, params.key),
    ),
  ),
]
