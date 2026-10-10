import { http, HttpResponse, type HttpHandler } from 'msw'
import { applyPatch, type Operation } from 'fast-json-patch'
import type {
  ConfigurationSchemaSummary,
  ConfigurationTemplateSummary,
  SchemaValidationErrorDetail,
} from '@42for.net/2splatform.sdk'
import { metaOf, typeCodes, type AnnotationTypeCode } from '@/utils/annotation-key'
import type { MockDb } from './db'
import { combinedSchema, ownContent, schemaErrors } from './configuration-handlers'
import {
  deleteDocument,
  documentIds,
  getDocument,
  latest,
  putVersion,
  type DocumentId,
  type ScopeRef,
} from './documents'
import { diffDocuments } from './diff'
import { errorResponse, requireRole, withDb } from './handlers'

// Templates and the three schema kinds of the mock API, on the versioned document store.

const api = '*/api/v1/:org/:project/:view'

type ScopeParams = { org: string; project: string; view: string }
type Params = ScopeParams & { type?: string; key?: string; version?: string; from?: string }

const projectKey = (params: ScopeParams) => `${params.org}.${params.project}`
const scopeOf = (params: ScopeParams): ScopeRef => ({ org: params.org, project: params.project, view: params.view })
const code = (type: string | undefined) => metaOf(type)?.code as AnnotationTypeCode | undefined

/** The read, write and history endpoints of one kind of versioned document. */
function versionedHandlers(
  route: string,
  idOf: (params: Params) => DocumentId | undefined,
  wrap: (
    params: Params,
    version: { Version: number; Author: string; Content?: Record<string, unknown> },
  ) => Record<string, unknown>,
  options: {
    patch?: boolean
    validate?: (
      db: MockDb,
      scope: ScopeRef,
      params: Params,
      content: Record<string, unknown>,
    ) => SchemaValidationErrorDetail[]
  },
): HttpHandler[] {
  const read = (db: MockDb, params: Params) => {
    const id = idOf(params)
    return id ? getDocument(db, scopeOf(params), id) : undefined
  }

  return [
    http.get<Params>(
      `${api}/${route}`,
      withDb((db, { params }) => {
        const denied = requireRole(db, projectKey(params), 'Reader')
        const version = latest(read(db, params))
        return (
          denied ??
          (version?.Content ? HttpResponse.json(wrap(params, version)) : errorResponse(404, 'Not found.', 'NotFound'))
        )
      }),
    ),

    ...(['post', 'put'] as const).map((method) =>
      http[method]<Params>(
        `${api}/${route}`,
        withDb(async (db, { params, request }) => {
          const denied = requireRole(db, projectKey(params), 'Contributor')
          const id = idOf(params)
          if (denied || !id) {
            return denied ?? errorResponse(400, 'Unknown annotation type.', 'InvalidInput')
          }
          const content = (await request.json()) as Record<string, unknown>
          const force = new URL(request.url).searchParams.get('force') === 'true'
          const errors = force ? [] : (options.validate?.(db, scopeOf(params), params, content) ?? [])
          if (errors.length) {
            return HttpResponse.json(
              {
                Message: 'Existing configurations do not comply with the new schema.',
                ErrorCode: 'SchemaValidationFailed',
                Errors: errors,
              },
              { status: 409 },
            )
          }
          return HttpResponse.json(wrap(params, putVersion(db, scopeOf(params), id, content)))
        }),
      ),
    ),

    ...(options.patch
      ? [
          http.patch<Params>(
            `${api}/${route}`,
            withDb(async (db, { params, request }) => {
              const denied = requireRole(db, projectKey(params), 'Contributor')
              const id = idOf(params)
              if (denied || !id) {
                return denied ?? errorResponse(400, 'Unknown annotation type.', 'InvalidInput')
              }
              const current = structuredClone(latest(read(db, params))?.Content ?? {})
              try {
                const patched = applyPatch(current, (await request.json()) as Operation[], true).newDocument
                return HttpResponse.json(wrap(params, putVersion(db, scopeOf(params), id, patched)))
              } catch (error) {
                return (error as { name?: string }).name === 'TEST_OPERATION_FAILED'
                  ? errorResponse(
                      412,
                      'A test operation failed: the document changed since it was read.',
                      'PatchTestFailed',
                    )
                  : errorResponse(400, `The JSON Patch is not valid: ${(error as Error).message}`, 'PatchInvalid')
              }
            }),
          ),
        ]
      : []),

    http.delete<Params>(
      `${api}/${route}`,
      withDb((db, { params }) => {
        const denied = requireRole(db, projectKey(params), 'Contributor')
        const id = idOf(params)
        if (denied || !id) {
          return denied ?? errorResponse(400, 'Unknown annotation type.', 'InvalidInput')
        }
        deleteDocument(db, scopeOf(params), id)
        return new HttpResponse(null, { status: 204 })
      }),
    ),

    http.get<Params>(
      `${api}/${route}/versions`,
      withDb((db, { params }) => {
        const denied = requireRole(db, projectKey(params), 'Reader')
        return (
          denied ??
          HttpResponse.json(
            (read(db, params)?.versions ?? []).map(({ Version, Author, CreationTime, ExpirationTime }) => ({
              Version,
              Author,
              CreationTime,
              ExpirationTime,
            })),
          )
        )
      }),
    ),

    http.get<Params>(
      `${api}/${route}/versions/:version`,
      withDb((db, { params }) => {
        const denied = requireRole(db, projectKey(params), 'Reader')
        const version = read(db, params)?.versions.find((entry) => entry.Version === Number(params.version))
        return (
          denied ??
          (version?.Content
            ? HttpResponse.json(wrap(params, version))
            : errorResponse(404, 'The version is not available.', 'NotFound'))
        )
      }),
    ),

    http.get<Params>(
      `${api}/${route}/versions/:version/diff/:from`,
      withDb((db, { params }) => {
        const denied = requireRole(db, projectKey(params), 'Reader')
        const versions = read(db, params)?.versions ?? []
        const find = (value?: string) => versions.find((entry) => entry.Version === Number(value))
        const [to, from] = [find(params.version), find(params.from)]
        return (
          denied ??
          (to && from
            ? HttpResponse.json(diffDocuments(from.Content ?? {}, to.Content ?? {}))
            : errorResponse(404, 'Not found.', 'NotFound'))
        )
      }),
    ),
  ]
}

/** 409 details: the configurations of the scope that would fail the schema change. */
function nonCompliant(
  db: MockDb,
  scope: ScopeRef,
  id: DocumentId,
  content: Record<string, unknown>,
): SchemaValidationErrorDetail[] {
  const keys = documentIds(db, scope, 'configuration:').map((entry) => entry.slice('configuration:'.length))
  const details: SchemaValidationErrorDetail[] = []
  for (const key of keys) {
    // Evaluate the combined schema as if the new content were stored.
    const store = ((db.documents ??= {})[`${scope.org}/${scope.project}/${scope.view}`] ??= {}) as Record<
      string,
      unknown
    >
    const previous = store[id]
    store[id] = { versions: [{ Version: 0, Author: '', CreationTime: '', Content: content }] }
    const combined = combinedSchema(db, scope, key)
    if (previous === undefined) {
      delete store[id]
    } else {
      store[id] = previous
    }
    const errors = schemaErrors(combined?.MergedContent, ownContent(db, scope, key) ?? {})
    if (errors.length) {
      details.push({ AnnotationKey: key, ViewName: scope.view, Errors: errors })
    }
  }
  return details
}

const schemaWrap =
  (identity: (params: Params) => Record<string, unknown>) =>
  (params: Params, version: { Version: number; Author: string; Content?: Record<string, unknown> }) => ({
    View: params.view,
    ...identity(params),
    Version: version.Version,
    Author: version.Author,
    Content: version.Content,
  })

export const templateSchemaHandlers: HttpHandler[] = [
  http.get<ScopeParams>(
    `${api}/templates`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      const scope = scopeOf(params)
      const summaries: ConfigurationTemplateSummary[] = typeCodes.flatMap((type) => {
        const version = latest(getDocument(db, scope, `template:${type}`))
        return version
          ? [
              {
                AnnotationType: type,
                Version: version.Version,
                Author: version.Author,
                UpdatedAt: version.CreationTime,
              },
            ]
          : []
      })
      return denied ?? HttpResponse.json(summaries)
    }),
  ),

  http.get<ScopeParams>(
    `${api}/configuration-schemas`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      const scope = scopeOf(params)
      const summaries: ConfigurationSchemaSummary[] = documentIds(db, scope, 'schema:').flatMap(
        (id): ConfigurationSchemaSummary[] => {
          const version = latest(getDocument(db, scope, id))
          if (!version) {
            return []
          }
          const [, kind, first, second] = id.split(':')
          const base = { Version: version.Version, Author: version.Author, UpdatedAt: version.CreationTime }
          return kind === 'type'
            ? [{ Kind: 'Type' as const, AnnotationType: first, ...base }]
            : kind === 'annotation'
              ? [{ Kind: 'Annotation' as const, AnnotationKey: first, ...base }]
              : [{ Kind: 'DescendantType' as const, AnnotationKey: first, AnnotationType: second, ...base }]
        },
      )
      return denied ?? HttpResponse.json(summaries)
    }),
  ),

  ...versionedHandlers(
    'template/:type',
    (params) => (code(params.type) ? `template:${code(params.type)}` : undefined),
    (params, version) => ({
      AnnotationType: code(params.type),
      Version: version.Version,
      Author: version.Author,
      Content: version.Content,
    }),
    { patch: true },
  ),

  ...versionedHandlers(
    'configuration-schema/type/:type',
    (params) => (code(params.type) ? `schema:type:${code(params.type)}` : undefined),
    schemaWrap((params) => ({ AnnotationType: code(params.type) })),
    { validate: (db, scope, params, content) => nonCompliant(db, scope, `schema:type:${code(params.type)}`, content) },
  ),

  ...versionedHandlers(
    'configuration-schema/:key/type/:type',
    (params) => (code(params.type) ? `schema:descendant:${params.key}:${code(params.type)}` : undefined),
    schemaWrap((params) => ({ AnnotationKey: params.key, AnnotationType: code(params.type) })),
    {
      validate: (db, scope, params, content) =>
        nonCompliant(db, scope, `schema:descendant:${params.key}:${code(params.type)}`, content),
    },
  ),

  ...versionedHandlers(
    'configuration-schema/:key',
    (params) => `schema:annotation:${params.key}`,
    schemaWrap((params) => ({ AnnotationKey: params.key })),
    { validate: (db, scope, params, content) => nonCompliant(db, scope, `schema:annotation:${params.key}`, content) },
  ),
]
