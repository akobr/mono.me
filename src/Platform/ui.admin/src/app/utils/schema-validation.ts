import { Validator, type OutputUnit, type Schema, type SchemaDraft } from '@cfworker/json-schema'
import draft07 from 'ajv/dist/refs/json-schema-draft-07.json'
import draft2019 from 'ajv/dist/refs/json-schema-2019-09/schema.json'
import applicator2019 from 'ajv/dist/refs/json-schema-2019-09/meta/applicator.json'
import content2019 from 'ajv/dist/refs/json-schema-2019-09/meta/content.json'
import core2019 from 'ajv/dist/refs/json-schema-2019-09/meta/core.json'
import format2019 from 'ajv/dist/refs/json-schema-2019-09/meta/format.json'
import metaData2019 from 'ajv/dist/refs/json-schema-2019-09/meta/meta-data.json'
import validation2019 from 'ajv/dist/refs/json-schema-2019-09/meta/validation.json'
import draft2020 from 'ajv/dist/refs/json-schema-2020-12/schema.json'
import applicator2020 from 'ajv/dist/refs/json-schema-2020-12/meta/applicator.json'
import content2020 from 'ajv/dist/refs/json-schema-2020-12/meta/content.json'
import core2020 from 'ajv/dist/refs/json-schema-2020-12/meta/core.json'
import format2020 from 'ajv/dist/refs/json-schema-2020-12/meta/format-annotation.json'
import metaData2020 from 'ajv/dist/refs/json-schema-2020-12/meta/meta-data.json'
import unevaluated2020 from 'ajv/dist/refs/json-schema-2020-12/meta/unevaluated.json'
import validation2020 from 'ajv/dist/refs/json-schema-2020-12/meta/validation.json'
import type { SchemaValidationErrorDetail } from '@42for.net/2splatform.sdk'
import { isBinding } from './bindings'
import { getAt, parsePointer } from './json-pointer'

// Client-side JSON Schema validation (spec sections 9.3 and 10). The server stays authoritative and validates
// again on save; the client only gives early feedback.
//
// @cfworker/json-schema interprets the schema instead of generating code: Ajv compiles every schema with
// `new Function`, which the production CSP (`script-src 'self'`, spec section 16) blocks. The meta-schemas are
// the JSON documents shipped with Ajv. Messages follow Ajv's wording ("must be <= 10").

type Draft = Extract<SchemaDraft, '7' | '2019-09' | '2020-12'>

const metaSchemas: Record<Draft, { root: object; parts: object[] }> = {
  '7': { root: draft07, parts: [] },
  '2019-09': {
    root: draft2019,
    parts: [core2019, applicator2019, validation2019, metaData2019, format2019, content2019],
  },
  '2020-12': {
    root: draft2020,
    parts: [core2020, applicator2020, unevaluated2020, validation2020, metaData2020, format2020, content2020],
  },
}

/** Draft-07 by default; 2019-09 or 2020-12 when `$schema` says so. */
function draftOf(schema: unknown): Draft {
  const declared = (schema as { $schema?: string } | null)?.$schema ?? ''
  return declared.includes('2020-12') ? '2020-12' : declared.includes('2019-09') ? '2019-09' : '7'
}

const metaValidators = new Map<Draft, Validator>()

function metaValidator(draft: Draft): Validator {
  let validator = metaValidators.get(draft)
  if (!validator) {
    const { root, parts } = metaSchemas[draft]
    validator = new Validator(root as Schema, draft, false)
    for (const part of parts) {
      validator.addSchema(part as Schema)
    }
    metaValidators.set(draft, validator)
  }
  return validator
}

function withoutDeclaredDraft(schema: object): object {
  const { $schema: _ignored, ...rest } = schema as { $schema?: string }
  return rest
}

const compiled = new WeakMap<object, Validator | Error>()

/** Prepares a validator once per schema object; a schema that fails its meta-schema yields an error instead. */
function compileSchema(schema: object): Validator | Error {
  let validator = compiled.get(schema)
  if (!validator) {
    try {
      const draft = draftOf(schema)
      const rest = withoutDeclaredDraft(schema)
      validator = metaValidator(draft).validate(rest).valid
        ? new Validator(rest as Schema, draft, false)
        : new Error('The schema does not match its meta-schema.')
    } catch (error) {
      validator = error instanceof Error ? error : new Error(String(error))
    }
    compiled.set(schema, validator)
  }
  return validator
}

export interface ValidationIssue {
  /** JSON Pointer of the failing value ("" for the root). */
  pointer: string
  message: string
  keyword: string
}

/** Keywords whose errors only say that something below them failed; the errors below carry the details. */
const wrappers = new Set([
  'properties',
  'patternProperties',
  'items',
  'prefixItems',
  'additionalItems',
  'unevaluatedItems',
  'unevaluatedProperties',
  'dependentSchemas',
  'propertyNames',
  '$ref',
  '$recursiveRef',
  '$dynamicRef',
  'allOf',
  'if',
  'then',
  'else',
])
/** Keywords that report one error for their alternatives; the errors of each alternative are noise. */
const combinators = new Set(['anyOf', 'oneOf', 'not'])

const pointerOf = (location: string) => location.replace(/^#/, '')
const quoted = (text: string) => /"([^"]*)"/.exec(text)?.[1]

const limits: Record<string, (value: string) => string> = {
  maximum: (value) => `must be <= ${value}`,
  minimum: (value) => `must be >= ${value}`,
  exclusiveMaximum: (value) => `must be < ${value}`,
  exclusiveMinimum: (value) => `must be > ${value}`,
  multipleOf: (value) => `must be multiple of ${value}`,
  maxLength: (value) => `must NOT have more than ${value} characters`,
  minLength: (value) => `must NOT have fewer than ${value} characters`,
  maxItems: (value) => `must NOT have more than ${value} items`,
  minItems: (value) => `must NOT have fewer than ${value} items`,
  maxProperties: (value) => `must NOT have more than ${value} properties`,
  minProperties: (value) => `must NOT have fewer than ${value} properties`,
  pattern: (value) => `must match pattern "${value}"`,
  format: (value) => `must match format "${value}"`,
  type: (value) => `must be ${value}`,
}

const fixed: Record<string, string> = {
  enum: 'must be equal to one of the allowed values',
  const: 'must be equal to constant',
  uniqueItems: 'must NOT have duplicate items',
  anyOf: 'must match a schema in anyOf',
  oneOf: 'must match exactly one schema in oneOf',
  not: 'must NOT be valid',
}

function messageOf(unit: OutputUnit, schema: object): string {
  const limit = limits[unit.keyword]
  if (limit) {
    const value = getAt(schema, parsePointer(pointerOf(unit.keywordLocation)))
    return value === undefined ? unit.error : limit(Array.isArray(value) ? value.join(',') : String(value))
  }
  return fixed[unit.keyword] ?? unit.error
}

/** True when additionalProperties at `location` does not cover `property` (declared or matching a pattern). */
function isDeclared(schema: object, location: string, property: string): boolean {
  const parent = getAt(schema, parsePointer(pointerOf(location)).slice(0, -1)) as
    { properties?: Record<string, unknown>; patternProperties?: Record<string, unknown> } | undefined
  return (
    Object.hasOwn(parent?.properties ?? {}, property) ||
    Object.keys(parent?.patternProperties ?? {}).some((pattern) => new RegExp(pattern, 'u').test(property))
  )
}

/**
 * Turns the validator's output into one issue per problem: wrapper errors and the alternatives of anyOf / oneOf
 * are dropped, a missing property points at the property, and a property that `additionalProperties: false`
 * forbids is reported as "x is not allowed". (The validator also reports declared properties whose value failed
 * under additionalProperties; those are skipped.)
 */
function toIssues(units: readonly OutputUnit[], schema: object): ValidationIssue[] {
  const alternatives = units.filter((unit) => combinators.has(unit.keyword)).map((unit) => `${unit.keywordLocation}/`)
  const forbidden = new Set<string>()
  const issues: ValidationIssue[] = []

  for (const unit of units) {
    if (alternatives.some((location) => unit.keywordLocation.startsWith(location))) {
      continue
    }
    const instance = pointerOf(unit.instanceLocation)
    if (unit.keyword === 'additionalProperties') {
      const property = quoted(unit.error)
      if (property !== undefined) {
        forbidden.add(`${instance}/${property}`)
        const closed = getAt(schema, parsePointer(pointerOf(unit.keywordLocation))) === false
        if (closed && !isDeclared(schema, unit.keywordLocation, property)) {
          issues.push({
            pointer: `${instance}/${property}`,
            message: `${property} is not allowed`,
            keyword: unit.keyword,
          })
        }
      }
      continue
    }
    if ((unit.keyword === 'false' && forbidden.has(instance)) || wrappers.has(unit.keyword)) {
      continue
    }
    if (unit.keyword === 'required') {
      const property = quoted(unit.error) ?? ''
      issues.push({ pointer: `${instance}/${property}`, message: `${property} is required`, keyword: 'required' })
      continue
    }
    issues.push({ pointer: instance, message: messageOf(unit, schema), keyword: unit.keyword })
  }

  const seen = new Set<string>()
  return issues.filter((issue) => {
    const id = `${issue.pointer}|${issue.keyword}|${issue.message}`
    if (seen.has(id)) {
      return false
    }
    seen.add(id)
    return true
  })
}

/**
 * Validates `data`; errors on binding values (and below them) are dropped, because the server evaluates
 * bindings before it validates. A schema that fails its meta-schema validates nothing (the schema editor
 * reports it).
 */
export function validateAgainst(schema: object | null | undefined, data: unknown): ValidationIssue[] {
  if (!schema || data === undefined) {
    return []
  }
  const validator = compileSchema(schema)
  if (validator instanceof Error) {
    return []
  }
  let result: ReturnType<Validator['validate']>
  try {
    result = validator.validate(data)
  } catch {
    // Values JSON cannot hold (undefined, functions) are not validated.
    return []
  }
  if (result.valid) {
    return []
  }
  return toIssues(result.errors, withoutDeclaredDraft(schema)).filter((issue) => {
    // A missing property is reported on the property; whether a binding hides it depends on its parent.
    const pointer =
      issue.keyword === 'required' ? issue.pointer.slice(0, issue.pointer.lastIndexOf('/')) : issue.pointer
    return !isUnderBinding(data, pointer)
  })
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
  const draft = draftOf(schema)
  const result = metaValidator(draft).validate(withoutDeclaredDraft(schema))
  return result.valid ? [] : toIssues(result.errors, metaSchemas[draft].root)
}
