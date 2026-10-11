import { http, HttpResponse, type HttpHandler } from 'msw'
import type { Annotation } from '@42for.net/2splatform.sdk'
import {
  codeOfKey,
  getAllAncestors,
  isValidAnnotationKey,
  parseAnnotationKey,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { annotationsOf, overridesOf, type MockDb } from './db'
import { errorResponse, requireRole, withDb } from './handlers'

const api = '*/api/v1/:org/:project/:view'
const pageSize = 1000

type ScopeParams = { org: string; project: string; view: string }

const projectKey = (params: ScopeParams) => `${params.org}.${params.project}`

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

/** Stores an annotation and, like the API, creates its missing ancestors. */
function store(db: MockDb, params: ScopeParams, annotation: Annotation): Annotation {
  const stored = withRelations({ ...annotation, ProjectName: params.project, ViewName: params.view })
  const overrides = overridesOf(db, params.org, params.project, params.view)
  const existing = annotationsOf(db, params.org, params.project, params.view)

  for (const ancestor of getAllAncestors(annotation.AnnotationKey)) {
    if (!existing.has(ancestor) && !overrides.upserts[ancestor]) {
      const parsed = parseAnnotationKey(ancestor)!
      overrides.upserts[ancestor] = withRelations({
        ProjectName: params.project,
        ViewName: params.view,
        AnnotationKey: ancestor,
        Name: parsed.name,
        AnnotationType: typeNames[parsed.code],
        TimeZone: annotation.TimeZone,
        Labels: annotation.Labels,
      })
      overrides.deleted = overrides.deleted.filter((key) => key !== ancestor)
    }
  }

  overrides.upserts[annotation.AnnotationKey] = stored
  overrides.deleted = overrides.deleted.filter((key) => key !== annotation.AnnotationKey)
  return stored
}

/** The members the API's runtime types declare as required (Usage.cs, Execution.cs, …), besides the base fields. */
const requiredMembers: Record<AnnotationTypeCode, string[]> = {
  rst: [],
  sbt: [],
  unt: ['ResponsibilityKey', 'ResponsibilityName', 'UnitType', 'UnitDefinition'],
  cnt: ['SubjectKey', 'SubjectName'],
  usg: ['SubjectKey', 'ResponsibilityKey', 'SubjectName', 'ResponsibilityName'],
  exe: ['ResponsibilityKey', 'SubjectKey', 'ContextKey', 'UsageKey', 'SubjectName', 'ResponsibilityName', 'ContextName'],
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

function invalidAnnotation(annotation: Annotation, key?: string): Response | undefined {
  if (!isValidAnnotationKey(annotation.AnnotationKey ?? '')) {
    return errorResponse(400, `The annotation key '${annotation.AnnotationKey}' is not valid.`, 'InvalidInput')
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
  return undefined
}

export const annotationHandlers: HttpHandler[] = [
  ...perType.map(([segment, code]) =>
    http.get<ScopeParams>(
      `${api}/${segment}`,
      withDb((db, { params, request }) => {
        const denied = requireRole(db, projectKey(params), 'Reader')
        return (
          denied ??
          page(listOf(db, params, code), request, (items, token) => ({
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
        page(listOf(db, params), request, (items, token) => ({
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
        .map((annotation) => store(db, params, annotation))
      return HttpResponse.json(created)
    }),
  ),

  http.get<ScopeParams & { key: string }>(
    `${api}/annotations/:key`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const annotation = annotationsOf(db, params.org, params.project, params.view).get(params.key)
      return annotation
        ? HttpResponse.json(annotation)
        : errorResponse(404, `The annotation ${params.key} does not exist.`, 'NotFound')
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
      return HttpResponse.json(store(db, params, { ...body, Name: parsed.name }))
    }),
  ),

  http.delete<ScopeParams & { key: string }>(
    `${api}/annotations/:key`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }
      if (!annotationsOf(db, params.org, params.project, params.view).has(params.key)) {
        return errorResponse(404, `The annotation ${params.key} does not exist.`, 'NotFound')
      }

      const overrides = overridesOf(db, params.org, params.project, params.view)
      delete overrides.upserts[params.key]
      overrides.deleted.push(params.key)
      return new HttpResponse(null, { status: 204 })
    }),
  ),
]
