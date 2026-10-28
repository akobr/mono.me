// Configuration formats: the Content of a configuration as the flat keys of a target and as the text of the flat and env
// formats, with the rules of the .NET library ConfigurationFormats (DocumentFlattener, JsonPaths, the renderers). The
// conformance suite, src/Platform/Storyteller/conformance, pins them for every language; test/conformance runs it here.
// A different contract from the key/value helpers of key-values.ts, which keep dotted keys, arrays as JSON and null as ''.
import type { ConfigurationFormat } from './conditional'

/** The flattening targets: flat (the `:` keys of .NET IConfiguration), properties (Spring Boot) and env. */
export type ConfigurationTarget = Extract<ConfigurationFormat, 'flat' | 'properties' | 'env'>

/** The rules of InvalidDocumentError, as the conformance vectors spell them. */
export type DocumentRule =
  | 'not-an-object'
  | 'empty-name'
  | 'colon-in-name'
  | 'case-collision'
  | 'bracket-in-name'
  | 'array-gap'
  | 'invalid-env-name'

/** A document that a target or a format cannot represent. The message names the JSON path, never a value. */
export class InvalidDocumentError extends Error {
  readonly rule: DocumentRule

  /** The JSON path of the first error: `$` for the root, `.name`, `['name']` and `[0]`. */
  readonly path: string

  constructor(rule: DocumentRule, path: string, message: string) {
    super(message)
    this.name = 'InvalidDocumentError'
    this.rule = rule
    this.path = path
  }
}

/**
 * The keys of a configuration's Content for a target, with their raw values, in document order: `db:hosts:0` (flat),
 * `db.hosts[0]` (properties) or `DB__HOSTS__0` (env). A string is its value, a number or a boolean its JSON text as .NET
 * writes it; null and empty containers make no key. Throws InvalidDocumentError for a document the target cannot hold.
 * content is JSON data, as JSON.parse returns it; null and undefined have no keys.
 */
export function flattenConfiguration(content: unknown, target: ConfigurationTarget = 'flat'): Record<string, string> {
  const keys = Object.create(null) as Record<string, string>

  for (const { key, value } of toEntries(content, target)) {
    keys[key] = value
  }

  return keys
}

/**
 * The exact text of the flat or the env format, as the API's ?format= writes it, with LF line ends: flat is a JSON
 * object of the IConfiguration keys, indented by two spaces; env a .env file, one NAME=value line per value. An unpaired
 * surrogate becomes U+FFFD first, as UTF-8 text requires. The API also renders properties and yaml
 * (getConfigurationFormattedConditional).
 */
export function formatConfiguration(content: unknown, format: Extract<ConfigurationFormat, 'flat' | 'env'>): string {
  const entries = toEntries(canonicalize(content), format)

  switch (format) {
    case 'flat':
      return entries.length === 0
        ? '{}\n'
        : `{\n${entries.map(({ key, value }) => `  ${toJsonString(key)}: ${toJsonString(value)}`).join(',\n')}\n}\n`

    case 'env':
      return entries.map(({ key, value }) => `${key}=${quoteEnvValue(value)}\n`).join('')

    default:
      throw new TypeError(`Not a format of formatConfiguration: ${String(format)}.`)
  }
}

type Segment = string | number

interface Leaf {
  segments: Segment[]
  value: string
  path: string
}

interface Entry {
  key: string
  value: string
}

// The base rules of every target, then the name and value rules of the target, each over the whole document; the
// first error throws.
function toEntries(content: unknown, target: ConfigurationTarget): Entry[] {
  const leaves = readLeaves(content)

  switch (target) {
    case 'flat':
      return leaves.map(leaf => ({ key: toFlatKey(leaf.segments), value: leaf.value }))

    case 'properties':
      checkNames(content, (name, path) => {
        if (name.includes('[') || name.includes(']')) {
          throw new InvalidDocumentError(
            'bracket-in-name',
            path,
            `The name of the property at ${path} contains '[' or ']', which a properties key cannot hold.`,
          )
        }
      })

      if (isPlainObject(content)) {
        checkGaps(content, rootPath)
      }

      return leaves.map(leaf => ({ key: toPropertiesKey(leaf.segments), value: leaf.value }))

    case 'env':
      checkNames(content, (name, path, isRoot) => {
        if (!isValidEnvName(name, isRoot)) {
          throw new InvalidDocumentError(
            'invalid-env-name',
            path,
            `The name of the property at ${path} cannot be part of an environment variable name: use ASCII letters, digits, '.' and '-' with single '_' between them, and start the names of the root with a letter.`,
          )
        }
      })

      return leaves.map(leaf => ({
        key: leaf.segments.map(segment => typeof segment === 'number' ? String(segment) : segment.toUpperCase()).join('__'),
        value: leaf.value,
      }))

    default:
      throw new TypeError(`Not a configuration target: ${String(target)}.`)
  }
}

// --- the base rules (DocumentFlattener) ----------------------------------------------------------------------------

const rootPath = '$'

// The leaves in document order. Every name is checked before its value, also of a null or an empty container, so a
// document is valid or not whatever its values are; keys that differ only in case collide, as IConfiguration compares
// them case-insensitively, and the later leaf is reported.
function readLeaves(content: unknown): Leaf[] {
  const leaves: Leaf[] = []

  if (content === null || content === undefined) {
    return leaves
  }

  if (!isPlainObject(content)) {
    throw new InvalidDocumentError(
      'not-an-object',
      rootPath,
      `The content of the configuration, ${rootPath}, must be a JSON object, not ${describeType(content)}.`,
    )
  }

  const paths = new Map<string, string>()

  const visit = (token: unknown, segments: Segment[], path: string): void => {
    if (isPlainObject(token)) {
      for (const [name, value] of Object.entries(token)) {
        const propertyPath = toPropertyPath(path, name)

        if (name.length === 0) {
          throw new InvalidDocumentError('empty-name', propertyPath, `The property at ${propertyPath} has an empty name.`)
        }

        if (name.includes(':')) {
          throw new InvalidDocumentError(
            'colon-in-name',
            propertyPath,
            `The name of the property at ${propertyPath} contains ':', the separator of IConfiguration keys.`,
          )
        }

        visit(value, [...segments, name], propertyPath)
      }

      return
    }

    if (Array.isArray(token)) {
      token.forEach((item, index) => visit(item, [...segments, index], toItemPath(path, index)))
      return
    }

    const text = toText(token)

    if (text === undefined) {
      return
    }

    const folded = foldCase(toFlatKey(segments))
    const otherPath = paths.get(folded)

    if (otherPath !== undefined) {
      throw new InvalidDocumentError(
        'case-collision',
        path,
        `The keys of ${otherPath} and ${path} differ only in case; IConfiguration compares keys case-insensitively.`,
      )
    }

    paths.set(folded, path)
    leaves.push({ segments, value: text, path })
  }

  visit(content, [], rootPath)
  return leaves
}

// undefined for null, which makes no key.
function toText(value: unknown): string | undefined {
  switch (typeof value) {
    case 'string':
      return value
    case 'boolean':
      return value ? 'true' : 'false'
    case 'number':
      return toNumberText(value)
    case 'bigint':
      return value.toString()
    case 'undefined':
      return undefined
    default:
      if (value === null) {
        return undefined
      }

      throw new TypeError('The content of a configuration must be JSON data.')
  }
}

/**
 * A number's text as .NET writes it after reading the document with double-precision floats: a safe integer as its
 * digits; any other number as Newtonsoft writes a double, with the shortest round-trip digits, in fixed notation while
 * the decimal point falls between 3 places before the first digit and 17 after it (an integral one with .0), and in
 * scientific notation otherwise: 1E+20, 1.5E-07. JavaScript cannot tell 1.0 from 1, nor hold an integer beyond 2^53, so
 * those differ from .NET; the conformance suite keeps them out.
 */
function toNumberText(value: number): string {
  if (Number.isSafeInteger(value) || !Number.isFinite(value)) {
    return String(value)
  }

  const [mantissa = '', exponentText = '0'] = value.toExponential().split('e')
  const sign = value < 0 ? '-' : ''
  const digits = mantissa.replace('-', '').replace('.', '')
  const exponent = Number(exponentText)
  const position = exponent + 1

  if (position > 17 || position < -3) {
    const fraction = digits.length > 1 ? `.${digits.slice(1)}` : ''
    return `${sign}${digits[0]}${fraction}E${exponent < 0 ? '-' : '+'}${String(Math.abs(exponent)).padStart(2, '0')}`
  }

  if (position <= 0) {
    return `${sign}0.${'0'.repeat(-position)}${digits}`
  }

  if (position >= digits.length) {
    return `${sign}${digits}${'0'.repeat(position - digits.length)}.0`
  }

  return `${sign}${digits.slice(0, position)}.${digits.slice(position)}`
}

// As .NET's OrdinalIgnoreCase: each UTF-16 unit upper-cased when that gives one unit, so ß stays ß.
function foldCase(key: string): string {
  let folded = ''

  for (const unit of key.split('')) {
    const upper = unit.toUpperCase()
    folded += upper.length === 1 ? upper : unit
  }

  return folded
}

function toFlatKey(segments: readonly Segment[]): string {
  return segments.map(String).join(':')
}

function describeType(value: unknown): string {
  if (Array.isArray(value)) {
    return 'an array'
  }

  switch (typeof value) {
    case 'string':
      return 'a string'
    case 'number':
    case 'bigint':
      return 'a number'
    case 'boolean':
      return 'a boolean'
    default:
      return typeof value
  }
}

// --- JSON paths (JsonPaths) ----------------------------------------------------------------------------------------

const plainName = /^[A-Za-z_][A-Za-z0-9_-]*$/

// .name for a plain name, ['name'] for any other: \ and ' escaped, C0 controls and DEL as \n, \r, \t or \uXXXX, and #
// as #, so a path never holds a line break or a #.
function toPropertyPath(parent: string, name: string): string {
  if (plainName.test(name)) {
    return `${parent}.${name}`
  }

  let escaped = ''

  for (const unit of name.split('')) {
    const code = unit.charCodeAt(0)

    if (unit === '\\' || unit === '\'') {
      escaped += `\\${unit}`
    } else if (unit === '\n') {
      escaped += '\\n'
    } else if (unit === '\r') {
      escaped += '\\r'
    } else if (unit === '\t') {
      escaped += '\\t'
    } else if (code < 0x20 || code === 0x7f || unit === '#') {
      escaped += `\\u${code.toString(16).toUpperCase().padStart(4, '0')}`
    } else {
      escaped += unit
    }
  }

  return `${parent}['${escaped}']`
}

function toItemPath(parent: string, index: number): string {
  return `${parent}[${index}]`
}

// --- the rules and keys of properties and env ------------------------------------------------------------------------

// Every property name in document order, also of a null or an empty container, with its path and whether it is a name
// of the root object.
function checkNames(content: unknown, check: (name: string, path: string, isRoot: boolean) => void): void {
  const visit = (token: unknown, path: string, isRoot: boolean): void => {
    if (isPlainObject(token)) {
      for (const [name, value] of Object.entries(token)) {
        const propertyPath = toPropertyPath(path, name)
        check(name, propertyPath, isRoot)
        visit(value, propertyPath, false)
      }
    } else if (Array.isArray(token)) {
      token.forEach((item, index) => visit(item, toItemPath(path, index), false))
    }
  }

  if (isPlainObject(content)) {
    visit(content, rootPath, true)
  }
}

// Spring Boot does not bind a list with a missing index: an item without a value followed by one with a value is
// invalid; a trailing one is left out.
function checkGaps(token: unknown, path: string): void {
  if (isPlainObject(token)) {
    for (const [name, value] of Object.entries(token)) {
      checkGaps(value, toPropertyPath(path, name))
    }
  } else if (Array.isArray(token)) {
    let last = token.length - 1

    while (last >= 0 && !hasValue(token[last])) {
      last--
    }

    for (let index = 0; index <= last; index++) {
      const itemPath = toItemPath(path, index)

      if (!hasValue(token[index])) {
        throw new InvalidDocumentError(
          'array-gap',
          itemPath,
          `The array item at ${itemPath} has no value but a later item has one; a properties list cannot skip an index.`,
        )
      }

      checkGaps(token[index], itemPath)
    }
  }
}

// Whether the token makes a key.
function hasValue(token: unknown): boolean {
  if (isPlainObject(token)) {
    return Object.values(token).some(hasValue)
  }

  if (Array.isArray(token)) {
    return token.some(hasValue)
  }

  return token !== null && token !== undefined
}

const plainPropertyName = /^[A-Za-z0-9_-]+$/

// Logging.LogLevel.[Microsoft.AspNetCore], list[3].name, [c d]: the key a reader returns, before the escaping of the file.
function toPropertiesKey(segments: readonly Segment[]): string {
  let key = ''

  for (const segment of segments) {
    if (typeof segment === 'number') {
      key += `[${segment}]`
      continue
    }

    if (key.length > 0) {
      key += '.'
    }

    key += plainPropertyName.test(segment) ? segment : `[${segment}]`
  }

  return key
}

// [A-Za-z0-9.-]+(_[A-Za-z0-9.-]+)*: a single _ only between other characters, so __ stays the only delimiter; a name of
// the root starts with a letter.
const envName = /^[A-Za-z0-9.-]+(_[A-Za-z0-9.-]+)*$/

function isValidEnvName(name: string, isRoot: boolean): boolean {
  return envName.test(name) && (!isRoot || /^[A-Za-z]/.test(name))
}

// --- text -----------------------------------------------------------------------------------------------------------

// Every string and name of the document with its unpaired surrogates as U+FFFD, as the API's canonical reading makes it.
function canonicalize(content: unknown): unknown {
  if (typeof content === 'string') {
    return toWellFormed(content)
  }

  if (Array.isArray(content)) {
    return content.map(canonicalize)
  }

  if (isPlainObject(content)) {
    const canonical = Object.create(null) as Record<string, unknown>

    for (const [name, value] of Object.entries(content)) {
      canonical[toWellFormed(name)] = canonicalize(value)
    }

    return canonical
  }

  return content
}

const unpairedSurrogate = /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/g

function toWellFormed(text: string): string {
  return text.replace(unpairedSurrogate, '\uFFFD')
}

// Newtonsoft's default escaping: JSON.stringify's, and U+0085, U+2028 and U+2029 escaped too.
function toJsonString(text: string): string {
  return JSON.stringify(text).replace(/[\u0085\u2028\u2029]/g, unit => `\\u${unit.charCodeAt(0).toString(16).padStart(4, '0')}`)
}

// Safe without quotes in every reader, also in those that take values verbatim.
const bareEnvValue = /^[A-Za-z0-9_.,:/@%+-]*$/

// Unquoted when every character is bare, so an empty value is NAME=; single-quoted, which bash, Docker Compose,
// python-dotenv and the key/value import read literally, when nothing in the value needs an escape; otherwise
// double-quoted with the escapes of the key/value export, in its order. $ is never escaped: the text is a dotenv file,
// not a shell script.
function quoteEnvValue(value: string): string {
  if (bareEnvValue.test(value)) {
    return value
  }

  if (!/['\\\r\n]/.test(value)) {
    return `'${value}'`
  }

  const escaped = value
    .replaceAll('\\', '\\\\')
    .replaceAll('"', '\\"')
    .replaceAll('\n', '\\n')
    .replaceAll('\r', '\\r')
    .replaceAll('\t', '\\t')

  return `"${escaped}"`
}

// An object of JSON.parse, or one with a null prototype; a Date or a Map is not JSON data.
function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false
  }

  const prototype = Object.getPrototypeOf(value) as unknown
  return prototype === Object.prototype || prototype === null
}
