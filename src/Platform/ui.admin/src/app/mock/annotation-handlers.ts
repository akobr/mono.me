import { http, HttpResponse, type HttpHandler } from 'msw'
import type { Annotation, ConfigurationSummary } from '@42for.net/2splatform.sdk'
import {
  codeOfKey,
  getAllAncestors,
  isValidAnnotationKey,
  parseAnnotationKey,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { withRelations } from './annotations'
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

function invalidAnnotation(annotation: Annotation, key?: string): Response | undefined {
  if (!isValidAnnotationKey(annotation.AnnotationKey ?? '')) {
    return errorResponse(400, `The annotation key '${annotation.AnnotationKey}' is not valid.`, 'InvalidInput')
  }
  if (key && annotation.AnnotationKey !== key) {
    return errorResponse(400, 'The annotation key in the body does not match the route.', 'InvalidInput')
  }
  return undefined
}

/** Deterministic configuration summaries: about a third of the annotations has a configuration. */
export function configurationSummaries(db: MockDb, params: ScopeParams): ConfigurationSummary[] {
  return listOf(db, params)
    .filter((annotation) => {
      let value = 0
      for (const char of annotation.AnnotationKey) {
        value = (value * 31 + char.charCodeAt(0)) >>> 0
      }
      return value % 3 === 0
    })
    .map((annotation, index) => ({
      AnnotationKey: annotation.AnnotationKey,
      AnnotationType: annotation.AnnotationType,
      Version: (index % 12) + 1,
      Author: index % 2 ? 'Grace Hopper' : 'Ada Lovelace',
      UpdatedAt: new Date(Date.now() - (index % 40) * 86_400_000).toISOString(),
      HasContent: true,
    }))
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
      body.forEach((annotation) => store(db, params, annotation))
      return HttpResponse.text(`${body.length} annotations stored.`)
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

  http.get<ScopeParams>(
    `${api}/configurations`,
    withDb((db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      return (
        denied ??
        page(configurationSummaries(db, params), request, (items, token) => ({
          Configurations: items,
          ContinuationToken: token,
          Count: items.length,
        }))
      )
    }),
  ),
]
