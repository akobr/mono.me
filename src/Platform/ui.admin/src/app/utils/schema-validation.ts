import Ajv, { type ErrorObject, type ValidateFunction } from 'ajv'
import Ajv2019 from 'ajv/dist/2019'
import Ajv2020 from 'ajv/dist/2020'
import addFormats from 'ajv-formats'
import type { SchemaValidationErrorDetail } from '@42for.net/2splatform.sdk'
import { isBinding } from './bindings'
import { getAt, parsePointer } from './json-pointer'

// Client-side JSON Schema validation (spec sections 9.3 and 10). The server stays authoritative and validates
// again on save; the client only gives early feedback.

type AjvInstance = Ajv | Ajv2019 | Ajv2020

const instances = new Map<string, AjvInstance>()

function create(draft: '07' | '2019' | '2020'): AjvInstance {
  const options = { allErrors: true, strict: false, validateFormats: true }
  const instance = draft === '2020' ? new Ajv2020(options) : draft === '2019' ? new Ajv2019(options) : new Ajv(options)
  addFormats(instance)
  // Annotation keywords used by the UI renderers.
  for (const keyword of ['x-secret', 'x-multiline']) {
    instance.addKeyword(keyword)
  }
  return instance
}

/** Draft-07 by default; 2019-09 or 2020-12 when `$schema` says so. */
export function ajvFor(schema: unknown): AjvInstance {
  const declared = (schema as { $schema?: string } | null)?.$schema ?? ''
  const draft = declared.includes('2020-12') ? '2020' : declared.includes('2019-09') ? '2019' : '07'
  let instance = instances.get(draft)
  if (!instance) {
    instance = create(draft)
    instances.set(draft, instance)
  }
  return instance
}

const compiled = new WeakMap<object, ValidateFunction | Error>()

/** Compiles once per schema object; an invalid schema yields the compile error. */
export function compileSchema(schema: object): ValidateFunction | Error {
  let validate = compiled.get(schema)
  if (!validate) {
    try {
      const { $schema: _ignored, ...rest } = schema as { $schema?: string }
      validate = ajvFor(schema).compile(rest)
    } catch (error) {
      validate = error instanceof Error ? error : new Error(String(error))
    }
    compiled.set(schema, validate)
  }
  return validate
}

export interface ValidationIssue {
  /** JSON Pointer of the failing value ("" for the root). */
  pointer: string
  message: string
  keyword: string
}

const describe = (error: ErrorObject): string => {
  if (error.keyword === 'required') {
    return `${(error.params as { missingProperty: string }).missingProperty} is required`
  }
  if (error.keyword === 'additionalProperties') {
    return `${(error.params as { additionalProperty: string }).additionalProperty} is not allowed`
  }
  return error.message ?? error.keyword
}

/**
 * Validates `data`; errors on binding values (and below them) are dropped, because the server evaluates
 * bindings before it validates.
 */
export function validateAgainst(schema: object | null | undefined, data: unknown): ValidationIssue[] {
  if (!schema) {
    return []
  }
  const validate = compileSchema(schema)
  if (validate instanceof Error || validate(data)) {
    return []
  }

  return (validate.errors ?? [])
    .map((error) => {
      const pointer =
        error.keyword === 'required'
          ? `${error.instancePath}/${(error.params as { missingProperty: string }).missingProperty}`
          : error.instancePath
      return { pointer, message: describe(error), keyword: error.keyword, instancePath: error.instancePath }
    })
    .filter((issue) => !isUnderBinding(data, issue.instancePath))
    .map(({ pointer, message, keyword }) => ({ pointer, message, keyword }))
}

function isUnderBinding(data: unknown, pointer: string): boolean {
  const segments = parsePointer(pointer)
  for (let length = segments.length; length >= 1; length--) {
    if (isBinding(getAt(data, segments.slice(0, length)))) {
      return true
    }
  }
  return false
}

/** "#/endpoints[0].url" (NJsonSchema) → "/endpoints/0/url". */
export function serverPathToPointer(path: string): string {
  const body = path.replace(/^#/, '')
  if (!body || body === '/') {
    return ''
  }
  return body
    .replaceAll(/\[(\d+)\]/g, '/$1')
    .replaceAll('.', '/')
    .replace(/^(?!\/)/, '/')
}

/** "NumberTooBig" → "Number too big". */
export function humanizeKind(kind: string): string {
  const words = kind.replaceAll(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/**
 * Errors of a 409 SchemaValidationErrorResponse ("#/retries: NumberTooBig", NJsonSchema path and kind) mapped to
 * pointers, so they can be attached to form controls (spec section 10).
 */
export function parseServerErrors(
  details: readonly SchemaValidationErrorDetail[] | undefined,
  annotationKey?: string,
): ValidationIssue[] {
  return (details ?? [])
    .filter((detail) => !annotationKey || !detail.AnnotationKey || detail.AnnotationKey === annotationKey)
    .flatMap((detail) => detail.Errors ?? [])
    .map((text) => {
      const match = /^(#[^:]*):\s*(.*)$/.exec(text)
      return match
        ? { pointer: serverPathToPointer(match[1]!), message: humanizeKind(match[2] || text), keyword: 'server' }
        : { pointer: '', message: text, keyword: 'server' }
    })
}

/**
 * Checks a JSON Schema document against its meta-schema (draft-07, or 2019-09 / 2020-12 when `$schema` says so),
 * for the schema editor (spec section 14).
 */
export function validateSchemaDocument(schema: unknown): ValidationIssue[] {
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) {
    return [{ pointer: '', message: 'A schema must be an object.', keyword: 'type' }]
  }
  const ajv = ajvFor(schema)
  const { $schema: _declared, ...rest } = schema as { $schema?: string }
  if (ajv.validateSchema(rest)) {
    return []
  }
  return (ajv.errors ?? []).map((error) => ({
    pointer: error.instancePath,
    message: describe(error),
    keyword: error.keyword,
  }))
}
