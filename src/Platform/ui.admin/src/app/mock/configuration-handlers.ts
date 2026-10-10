import { http, HttpResponse, type HttpHandler } from 'msw'
import { applyPatch, type Operation } from 'fast-json-patch'
import type { ConfigurationSchema, ConfigurationSummary } from '@42for.net/2splatform.sdk'
import { codeOfKey, getAllAncestors, metaOf, typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'
import { getAt, parsePointer } from '@/utils/json-pointer'
import { validateAgainst } from '@/utils/schema-validation'
import { annotationsOf, type MockDb } from './db'
import { diffDocuments } from './diff'
import {
  deepMerge,
  deleteDocument,
  documentIds,
  getDocument,
  latest,
  putVersion,
  type DocumentId,
  type ScopeRef,
} from './documents'
import { errorResponse, requireRole, withDb } from './handlers'

const api = '*/api/v1/:org/:project/:view'

type ScopeParams = { org: string; project: string; view: string }
type KeyParams = ScopeParams & { key: string }

const projectKey = (params: ScopeParams) => `${params.org}.${params.project}`
const scopeOf = (params: ScopeParams): ScopeRef => ({ org: params.org, project: params.project, view: params.view })
const mergeOrder = (key: string) => typeMeta[codeOfKey(key) ?? 'rst'].mergeOrder

export function ownContent(db: MockDb, scope: ScopeRef, key: string): Record<string, unknown> | undefined {
  return latest(getDocument(db, scope, `configuration:${key}`))?.Content
}

/** Ancestors in merge order, then the type template, then the own content (inheritance.md, templating.md). */
export function effectiveContent(db: MockDb, scope: ScopeRef, key: string): Record<string, unknown> {
  const layers = getAllAncestors(key)
    .toSorted((a, b) => mergeOrder(a) - mergeOrder(b) || a.localeCompare(b))
    .map((ancestor) => ownContent(db, scope, ancestor))
  layers.push(latest(getDocument(db, scope, `template:${codeOfKey(key)}`))?.Content, ownContent(db, scope, key))
  return layers.reduce<Record<string, unknown>>(
    (merged, layer) => (layer ? (deepMerge(merged, layer) as Record<string, unknown>) : merged),
    {},
  )
}

/** Type schema, descendant-type schemas of the ancestors, then the annotation's own schema. */
export function combinedSchema(db: MockDb, scope: ScopeRef, key: string) {
  const code = codeOfKey(key) as AnnotationTypeCode
  const typeName = typeMeta[code].type
  const candidates: [DocumentId, Partial<ConfigurationSchema>][] = [
    [`schema:type:${code}`, { AnnotationType: typeName }],
    ...getAllAncestors(key).map((ancestor): [DocumentId, Partial<ConfigurationSchema>] => [
      `schema:descendant:${ancestor}:${code}`,
      { AnnotationKey: ancestor, AnnotationType: typeName },
    ]),
    [`schema:annotation:${key}`, { AnnotationKey: key }],
  ]

  const applied: ConfigurationSchema[] = []
  let merged: Record<string, unknown> | undefined
  for (const [id, identity] of candidates) {
    const version = latest(getDocument(db, scope, id))
    if (version?.Content) {
      applied.push({
        View: scope.view,
        ...identity,
        Version: version.Version,
        Author: version.Author,
        Content: version.Content,
      })
      merged = deepMerge(merged ?? {}, version.Content) as Record<string, unknown>
    }
  }
  return merged ? { View: scope.view, AnnotationKey: key, MergedContent: merged, AppliedSchemas: applied } : undefined
}

class BindingError extends Error {}

/** Evaluates "@key/path" references against the effective documents (no JSON Logic in the mock). */
function resolveBindings(value: unknown, db: MockDb, scope: ScopeRef): unknown {
  if (typeof value === 'string' && value.startsWith('@')) {
    const [reference, ...path] = value.slice(1).split('/')
    const resolved = getAt(effectiveContent(db, scope, reference!), path)
    if (resolved === undefined) {
      throw new BindingError(`The binding ${value} could not be resolved.`)
    }
    return resolved
  }
  if (Array.isArray(value)) {
    return value.map((entry) => resolveBindings(entry, db, scope))
  }
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, entry]) => [key, resolveBindings(entry, db, scope)]))
  }
  return value
}

const kinds: Record<string, string> = {
  maximum: 'NumberTooBig',
  minimum: 'NumberTooSmall',
  required: 'PropertyRequired',
  enum: 'NotInEnumeration',
  pattern: 'PatternMismatch',
  format: 'FormatMismatch',
  type: 'TypeMismatch',
  additionalProperties: 'NoAdditionalPropertiesAllowed',
}

/** Schema errors in the API's format: "#/retries: NumberTooBig" (NJsonSchema path and kind). */
export function schemaErrors(schema: object | undefined, content: unknown): string[] {
  return validateAgainst(schema, content).map(
    (issue) => `#${parsePointer(issue.pointer).length ? issue.pointer : ''}: ${kinds[issue.keyword] ?? issue.keyword}`,
  )
}

function summary(db: MockDb, scope: ScopeRef, key: string): ConfigurationSummary | undefined {
  const version = latest(getDocument(db, scope, `configuration:${key}`))
  return version
    ? {
        AnnotationKey: key,
        AnnotationType: metaOf(key.slice(0, 3))?.type,
        Version: version.Version,
        Author: version.Author,
        UpdatedAt: version.CreationTime,
        HasContent: true,
      }
    : undefined
}

function configurationResponse(db: MockDb, scope: ScopeRef, key: string, content: Record<string, unknown>) {
  const version = latest(getDocument(db, scope, `configuration:${key}`))
  return {
    AnnotationKey: key,
    Version: version?.Version,
    Author: version?.Author,
    Content: content,
    Hash: ((JSON.stringify(content).length * 2654435761) >>> 0).toString(16),
  }
}

const pageSize = 1000

export const configurationHandlers: HttpHandler[] = [
  http.get<ScopeParams>(
    `${api}/configurations`,
    withDb((db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      const all = documentIds(db, scope, 'configuration:')
        .map((id) => summary(db, scope, id.slice('configuration:'.length)))
        .filter((entry): entry is ConfigurationSummary => !!entry)
      const offset = Number(new URL(request.url).searchParams.get('continuationToken') ?? 0) || 0
      const items = all.slice(offset, offset + pageSize)
      return HttpResponse.json({
        Configurations: items,
        ContinuationToken: offset + pageSize < all.length ? String(offset + pageSize) : undefined,
        Count: items.length,
      })
    }),
  ),

  http.get<KeyParams>(
    `${api}/configuration/:key`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      if (!ownContent(db, scope, params.key) && !latest(getDocument(db, scope, `configuration:${params.key}`))) {
        return errorResponse(404, `There is no configuration for ${params.key}.`, 'NotFound')
      }
      return HttpResponse.json(configurationResponse(db, scope, params.key, effectiveContent(db, scope, params.key)))
    }),
  ),

  http.post<KeyParams>(
    `${api}/configuration/:key`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      if (!annotationsOf(db, params.org, params.project, params.view).has(params.key)) {
        return errorResponse(404, `The annotation ${params.key} does not exist.`, 'NotFound')
      }
      const body = (await request.json()) as Record<string, unknown>
      const content = deepMerge(ownContent(db, scope, params.key) ?? {}, body) as Record<string, unknown>
      putVersion(db, scope, `configuration:${params.key}`, content)
      return HttpResponse.json(configurationResponse(db, scope, params.key, effectiveContent(db, scope, params.key)))
    }),
  ),

  http.patch<KeyParams>(
    `${api}/configuration/:key`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      const operations = (await request.json()) as Operation[]
      const document = structuredClone(ownContent(db, scope, params.key) ?? {})
      let patched: Record<string, unknown>
      try {
        patched = applyPatch(document, operations, true).newDocument
      } catch (error) {
        const name = (error as { name?: string }).name
        return name === 'TEST_OPERATION_FAILED'
          ? errorResponse(
              412,
              'A test operation failed: the configuration changed since it was read.',
              'PatchTestFailed',
              'Reload the configuration and apply the change again.',
            )
          : errorResponse(400, `The JSON Patch is not valid: ${(error as Error).message}`, 'PatchInvalid')
      }

      const force = new URL(request.url).searchParams.get('force') === 'true'
      const errors = force ? [] : schemaErrors(combinedSchema(db, scope, params.key)?.MergedContent, patched)
      if (errors.length) {
        return HttpResponse.json(
          {
            Message: `The configuration of ${params.key} does not comply with its schema.`,
            Hint: 'Fix the values or save with force=true.',
            ErrorCode: 'SchemaValidationFailed',
            Errors: [{ AnnotationKey: params.key, ViewName: params.view, Errors: errors }],
          },
          { status: 409 },
        )
      }

      putVersion(db, scope, `configuration:${params.key}`, patched)
      return HttpResponse.json(configurationResponse(db, scope, params.key, effectiveContent(db, scope, params.key)))
    }),
  ),

  http.delete<KeyParams>(
    `${api}/configuration/:key`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }
      deleteDocument(db, scopeOf(params), `configuration:${params.key}`)
      return new HttpResponse(null, { status: 204 })
    }),
  ),

  http.get<KeyParams>(
    `${api}/configuration/:key/resolved`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      if (!latest(getDocument(db, scope, `configuration:${params.key}`))) {
        return errorResponse(404, `There is no configuration for ${params.key}.`, 'NotFound')
      }
      try {
        const resolved = resolveBindings(effectiveContent(db, scope, params.key), db, scope) as Record<string, unknown>
        return HttpResponse.json(configurationResponse(db, scope, params.key, resolved))
      } catch (error) {
        if (error instanceof BindingError) {
          return errorResponse(422, error.message, 'BindingFailed', 'Check the referenced annotation key and path.')
        }
        throw error
      }
    }),
  ),

  http.get<KeyParams>(
    `${api}/configuration/:key/versions`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      const document = getDocument(db, scopeOf(params), `configuration:${params.key}`)
      return (
        denied ??
        HttpResponse.json(
          (document?.versions ?? []).map(({ Version, Author, CreationTime, ExpirationTime }) => ({
            Version,
            Author,
            CreationTime,
            ExpirationTime,
          })),
        )
      )
    }),
  ),

  http.get<KeyParams & { version: string }>(
    `${api}/configuration/:key/versions/:version`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const version = getDocument(db, scopeOf(params), `configuration:${params.key}`)?.versions.find(
        (entry) => entry.Version === Number(params.version),
      )
      return version?.Content
        ? HttpResponse.json({
            AnnotationKey: params.key,
            Version: version.Version,
            Author: version.Author,
            Content: version.Content,
          })
        : errorResponse(404, `Version ${params.version} of ${params.key} is not available.`, 'NotFound')
    }),
  ),

  http.get<KeyParams & { version: string; from: string }>(
    `${api}/configuration/:key/versions/:version/diff/:from`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const versions = getDocument(db, scopeOf(params), `configuration:${params.key}`)?.versions ?? []
      const find = (value: string) => versions.find((entry) => entry.Version === Number(value))
      const [to, from] = [find(params.version), find(params.from)]
      if (!to || !from) {
        return errorResponse(404, 'The version does not exist.', 'NotFound')
      }
      // Expired documents still have their recorded changes on the server; the mock diffs what it has.
      return HttpResponse.json(diffDocuments(from.Content ?? {}, to.Content ?? {}))
    }),
  ),

  http.get<KeyParams & { viewTo: string }>(
    `${api}/configuration/:key/diff/:viewTo`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      return HttpResponse.json(
        diffDocuments(
          effectiveContent(db, scope, params.key),
          effectiveContent(db, { ...scope, view: params.viewTo }, params.key),
        ),
      )
    }),
  ),

  http.get<KeyParams>(
    `${api}/configuration-schema/:key/definition`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const combined = combinedSchema(db, scopeOf(params), params.key)
      return combined
        ? HttpResponse.json(combined)
        : errorResponse(404, `No schema applies to ${params.key}.`, 'NotFound')
    }),
  ),

  http.get<ScopeParams & { type: string }>(
    `${api}/template/:type`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const code = metaOf(params.type)?.code
      const version = code ? latest(getDocument(db, scopeOf(params), `template:${code}`)) : undefined
      return version?.Content
        ? HttpResponse.json({
            AnnotationType: metaOf(code)?.type,
            Version: version.Version,
            Author: version.Author,
            Content: version.Content,
          })
        : errorResponse(404, `There is no template for ${params.type}.`, 'NotFound')
    }),
  ),
]
