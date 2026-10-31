import { http, HttpResponse, type HttpHandler } from 'msw'
import { applyPatch, type Operation } from 'fast-json-patch'
import type {
  ConfigurationComposition,
  ConfigurationCompositionOrigin,
  ConfigurationCompositionSource,
  ConfigurationCompositionStep,
  ConfigurationSchema,
  ConfigurationSummary,
} from '@42for.net/2splatform.sdk'
import { codeOfKey, getAllAncestors, metaOf, typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'
import { ancestorKeyOf } from '@/utils/binding-language'
import { escapeSegment, getAt, parsePointer } from '@/utils/json-pointer'
import { validateAgainst } from '@/utils/schema-validation'
import { appendAudit, newEntry } from './audit'
import { annotationsOf, overridesOf, type MockDb } from './db'
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
const inMergeOrder = (keys: string[]) => keys.toSorted((a, b) => mergeOrder(a) - mergeOrder(b) || a.localeCompare(b))

const isDeleted = (db: MockDb, params: ScopeParams, key: string) =>
  !!overridesOf(db, params.org, params.project, params.view).trash?.[key]

/** Like the API: a deleted annotation reads as missing with AnnotationDeleted, and its configuration can't change. */
function missingOrDeleted(db: MockDb, params: KeyParams, status: 404 | 409): Response | undefined {
  if (isDeleted(db, params, params.key)) {
    return errorResponse(
      status,
      `The annotation '${params.key}' is deleted; restore it to see or change its configuration.`,
      'AnnotationDeleted',
    )
  }
  return annotationsOf(db, params.org, params.project, params.view).has(params.key)
    ? undefined
    : errorResponse(404, `The annotation ${params.key} does not exist.`, 'NotFound')
}

export function ownContent(db: MockDb, scope: ScopeRef, key: string): Record<string, unknown> | undefined {
  return latest(getDocument(db, scope, `configuration:${key}`))?.Content
}

/** Ancestors in merge order, then the type template, then the own content (inheritance.md, templating.md). */
export function effectiveContent(db: MockDb, scope: ScopeRef, key: string): Record<string, unknown> {
  const layers = inMergeOrder(getAllAncestors(key)).map((ancestor) => ownContent(db, scope, ancestor))
  layers.push(latest(getDocument(db, scope, `template:${codeOfKey(key)}`))?.Content, ownContent(db, scope, key))
  return layers.reduce<Record<string, unknown>>(
    (merged, layer) => (layer ? (deepMerge(merged, layer) as Record<string, unknown>) : merged),
    {},
  )
}

/** The Hash of the mock Effective read; the mock composition answers the same one. */
const mockHash = (content: Record<string, unknown>) =>
  ((JSON.stringify(content).length * 2654435761) >>> 0).toString(16)

const isObject = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value)

function compositionSource(db: MockDb, scope: ScopeRef, id: DocumentId): ConfigurationCompositionSource {
  const version = latest(getDocument(db, scope, id))
  const content = version?.Content
  const template = id.startsWith('template:')
  const key = id.slice(id.indexOf(':') + 1)
  const code = (template ? key : codeOfKey(key)) as AnnotationTypeCode
  return {
    Id: id,
    Kind: template ? 'Template' : 'Configuration',
    AnnotationType: typeMeta[code].type,
    ...(template ? {} : { AnnotationKey: key }),
    Presence: !content ? 'Missing' : Object.keys(content).length ? 'Present' : 'Empty',
    Version: content ? version!.Version : 0,
    ...(content ? { Modified: version!.Modified, Content: content } : {}),
  }
}

/**
 * The composition of the mock Effective read (spec 8.6 of the Composition of the Effective Configuration). It is as
 * flat as `effectiveContent`: every ancestor is a node at depth 1 merged into the annotation and applies only its own
 * document, then the annotation applies the template of its type and its own document, so no source is applied twice.
 * The origins follow `deepMerge`, which replaces arrays: a write takes the step of its value, unless it leaves an
 * equal scalar in place, which keeps the step that set it first, as on the server.
 */
export function compositionOf(db: MockDb, scope: ScopeRef, key: string): ConfigurationComposition {
  const ancestors = inMergeOrder(getAllAncestors(key))
  const target = ancestors.length
  const template: DocumentId = `template:${codeOfKey(key)}`
  const applied: [DocumentId, number][] = [
    ...ancestors.map((ancestor, index): [DocumentId, number] => [`configuration:${ancestor}`, index]),
    ...(latest(getDocument(db, scope, template))?.Content ? [[template, target] as [DocumentId, number]] : []),
    [`configuration:${key}`, target],
  ]
  const sources = applied.map(([id]) => compositionSource(db, scope, id))

  const origins = new Map<string, number>()
  const mark = (pointer: string, value: unknown, step: number) => {
    origins.set(pointer, step)
    if (value && typeof value === 'object') {
      for (const [name, item] of Object.entries(value)) {
        mark(`${pointer}/${escapeSegment(name)}`, item, step)
      }
    }
  }
  // The walk of deepMerge(base, overlay) at `pointer`.
  const attribute = (base: unknown, overlay: unknown, pointer: string, step: number) => {
    if (isObject(base) && isObject(overlay)) {
      for (const [name, value] of Object.entries(overlay)) {
        const child = `${pointer}/${escapeSegment(name)}`
        if (name in base) {
          attribute(base[name], value, child, step)
        } else {
          mark(child, value, step)
        }
      }
      return
    }
    const scalar = (value: unknown) => value === null || typeof value !== 'object'
    if (scalar(base) && scalar(overlay) && base === overlay) {
      return
    }
    for (const path of [...origins.keys()].filter((entry) => entry.startsWith(`${pointer}/`))) {
      origins.delete(path)
    }
    mark(pointer, overlay, step)
  }

  let merged: Record<string, unknown> = {}
  sources.forEach((source, index) => {
    if (source.Content) {
      attribute(merged, source.Content, '', index + 1)
      merged = deepMerge(merged, source.Content) as Record<string, unknown>
    }
  })

  // In document order, a container before its children; the root has no origin.
  const content = effectiveContent(db, scope, key)
  const listed: ConfigurationCompositionOrigin[] = []
  const list = (value: unknown, pointer: string) => {
    if (pointer && origins.has(pointer)) {
      listed.push({ Path: pointer, Step: origins.get(pointer) })
    }
    if (value && typeof value === 'object') {
      for (const [name, item] of Object.entries(value)) {
        list(item, `${pointer}/${escapeSegment(name)}`)
      }
    }
  }
  list(content, '')

  return {
    AnnotationKey: key,
    Content: content,
    Hash: mockHash(content),
    Sources: sources,
    Nodes: [
      ...ancestors.map((ancestor, index) => ({ Index: index, AnnotationKey: ancestor, MergedInto: target, Depth: 1 })),
      { Index: target, AnnotationKey: key, MergedInto: null, Depth: 0 },
    ],
    Steps: applied.map(([id, node], index): ConfigurationCompositionStep => ({
      Order: index + 1,
      SourceId: id,
      Node: node,
    })),
    Origins: listed,
  }
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
        Modified: version.Modified,
        Content: version.Content,
      })
      merged = deepMerge(merged ?? {}, version.Content) as Record<string, unknown>
    }
  }
  return merged ? { View: scope.view, AnnotationKey: key, MergedContent: merged, AppliedSchemas: applied } : undefined
}

class BindingError extends Error {}

const configCall = /^@config\(\s*"([^"]*)"\s*\)$/
const annotationCall = /^@annotation\(\s*"([^"]*)"\s*(?:,\s*"([^"]*)"\s*)?\)$/

/** The segments of a JSON Pointer ("/a/b") or of a dotted JSONPath ("$.a.b"), the two forms the functions take. */
function segmentsOf(expression: string): string[] {
  return expression.startsWith('$')
    ? expression
        .replace(/^\$\.?/, '')
        .split('.')
        .filter(Boolean)
    : parsePointer(expression)
}

/**
 * The value of one binding of the configuration of `key`: `@config("…")` reads its effective document before the
 * evaluation, `@annotation("…"[, "<Type>"])` the Values of the annotation or of its ancestor of that type, and
 * "@<key>/<path>" the effective document of another annotation.
 */
function bindingValue(value: string, db: MockDb, scope: ScopeRef, key: string): unknown {
  const config = configCall.exec(value)
  if (config) {
    return getAt(effectiveContent(db, scope, key), segmentsOf(config[1]!))
  }
  const annotation = annotationCall.exec(value)
  if (annotation) {
    const [, expression, type] = annotation
    const target = type ? ancestorKeyOf(key, type) : key
    const values = target ? annotationsOf(db, scope.org, scope.project, scope.view).get(target)?.Values : undefined
    return getAt(values, segmentsOf(expression!))
  }
  const [reference, ...path] = value.slice(1).split('/')
  return getAt(effectiveContent(db, scope, reference!), path)
}

/** Sourced values (secrets), interpolation and math need the server; they stay as written. */
function resolveBinding(value: string, db: MockDb, scope: ScopeRef, key: string): unknown {
  if (/^@[([{]/.test(value)) {
    return value
  }
  let resolved: unknown
  try {
    resolved = bindingValue(value, db, scope, key)
  } catch (error) {
    // A pointer with a stray "%" is not a binding the mock can read.
    if (!(error instanceof URIError)) {
      throw error
    }
  }
  if (resolved === undefined) {
    throw new BindingError(`The binding ${value} could not be resolved.`)
  }
  return resolved
}

/** Evaluates the bindings of the configuration of `key` against the effective documents (no JSON Logic in the mock). */
function resolveBindings(value: unknown, db: MockDb, scope: ScopeRef, key: string): unknown {
  if (typeof value === 'string' && value.startsWith('@')) {
    return resolveBinding(value, db, scope, key)
  }
  if (Array.isArray(value)) {
    return value.map((entry) => resolveBindings(entry, db, scope, key))
  }
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([name, entry]) => [name, resolveBindings(entry, db, scope, key)]),
    )
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
        Modified: version.Modified,
        HasContent: true,
      }
    : undefined
}

function configurationResponse(db: MockDb, scope: ScopeRef, key: string, content: Record<string, unknown>) {
  const version = latest(getDocument(db, scope, `configuration:${key}`))
  return {
    AnnotationKey: key,
    Version: version?.Version ?? 0,
    Modified: version?.Modified ?? { At: new Date().toISOString(), By: { Kind: 'System' as const, Id: 'system' } },
    Content: content,
    Hash: mockHash(content),
  }
}

const pageSize = 1000

/** A write (POST or PUT): merges the body into the stored document and stores the next version. */
const writeConfiguration = withDb<KeyParams>(async (db, { params, request }) => {
  const denied = requireRole(db, projectKey(params), 'Contributor')
  if (denied) {
    return denied
  }
  const scope = scopeOf(params)
  const missing = missingOrDeleted(db, params, 409)
  if (missing) {
    return missing
  }
  const body = (await request.json()) as Record<string, unknown>
  const content = deepMerge(ownContent(db, scope, params.key) ?? {}, body) as Record<string, unknown>
  // Like the API: a document with values is checked against the combined schema unless force=true.
  const force = new URL(request.url).searchParams.get('force') === 'true'
  const errors =
    force || !Object.keys(content).length
      ? []
      : schemaErrors(combinedSchema(db, scope, params.key)?.MergedContent, content)
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
  const saved = putVersion(db, scope, `configuration:${params.key}`, content)
  appendAudit(db, scope, params.key, newEntry(db, 'ConfigurationChanged', { ConfigurationVersion: saved.Version }))
  return HttpResponse.json(configurationResponse(db, scope, params.key, effectiveContent(db, scope, params.key)))
})

export const configurationHandlers: HttpHandler[] = [
  http.get<ScopeParams>(
    `${api}/configurations`,
    withDb((db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      const annotations = annotationsOf(db, params.org, params.project, params.view)
      const all = documentIds(db, scope, 'configuration:')
        .map((id) => id.slice('configuration:'.length))
        .filter((key) => annotations.has(key))
        .map((key) => summary(db, scope, key))
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
      // Like GetRawConfigurationAsync: 404 only for an unknown or deleted annotation; without a stored entity the
      // inherited content comes back with Version 0.
      const missing = missingOrDeleted(db, params, 404)
      if (missing) {
        return missing
      }
      return HttpResponse.json(configurationResponse(db, scope, params.key, effectiveContent(db, scope, params.key)))
    }),
  ),

  // The composition of the mock Effective read, with the same rules for the role and a missing or deleted annotation.
  http.get<KeyParams>(
    `${api}/configuration/:key/composition`,
    withDb((db, { params }) => {
      const denied = requireRole(db, projectKey(params), 'Reader')
      if (denied) {
        return denied
      }
      const missing = missingOrDeleted(db, params, 404)
      return missing ?? HttpResponse.json(compositionOf(db, scopeOf(params), params.key))
    }),
  ),

  // The API takes both methods for a write, a merge into the stored document.
  http.post<KeyParams>(`${api}/configuration/:key`, writeConfiguration),
  http.put<KeyParams>(`${api}/configuration/:key`, writeConfiguration),

  http.patch<KeyParams>(
    `${api}/configuration/:key`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, projectKey(params), 'Contributor')
      if (denied) {
        return denied
      }
      const scope = scopeOf(params)
      const deleted = missingOrDeleted(db, params, 409)
      if (deleted) {
        return deleted
      }
      // Like the API: without a stored version there is nothing to patch (the first content is created); a stored {} is
      // patched.
      const stored = ownContent(db, scope, params.key)
      if (stored === undefined) {
        return errorResponse(
          404,
          `Configuration for '${params.key}' has no stored version to patch: it was never written, or it was deleted.`,
          undefined,
          'A PUT or POST writes a first document.',
        )
      }
      const operations = (await request.json()) as Operation[]
      const document = structuredClone(stored)
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

      const saved = putVersion(db, scope, `configuration:${params.key}`, patched)
      appendAudit(db, scope, params.key, newEntry(db, 'ConfigurationChanged', { ConfigurationVersion: saved.Version }))
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
      if (isDeleted(db, params, params.key)) {
        return missingOrDeleted(db, params, 409)!
      }
      const scope = scopeOf(params)
      const version = latest(getDocument(db, scope, `configuration:${params.key}`))?.Version
      deleteDocument(db, scope, `configuration:${params.key}`)
      if (version) {
        appendAudit(db, scope, params.key, newEntry(db, 'ConfigurationDeleted', { ConfigurationVersion: version }))
      }
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
      // Like the raw read: 404 only for an unknown or deleted annotation; without an own document the inherited
      // content is resolved.
      const missing = missingOrDeleted(db, params, 404)
      if (missing) {
        return missing
      }
      try {
        const resolved = resolveBindings(effectiveContent(db, scope, params.key), db, scope, params.key) as Record<
          string,
          unknown
        >
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
          (document?.versions ?? []).map(({ Version, Modified, ExpirationTime }) => ({
            Version,
            Modified,
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
            Modified: version.Modified,
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
]
