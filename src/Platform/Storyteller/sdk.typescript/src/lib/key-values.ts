// Key/value helpers: the key rules of the API, .env and flat JSON conversion to and from the 2s.key-values.v1 document,
// format detection of pasted text, and secret references (the Reference level).
import type { KeyValueDocument, KeyValueDocumentItem } from '../generated/model'

export const keyValueDocumentFormat = '2s.key-values.v1'

/** A protection level of a pair, as the API spells it. */
export type KeyValueProtectionName = 'Public' | 'Standard' | 'Hsm' | 'Byok' | 'Hyok'

export const keyValueProtections: readonly KeyValueProtectionName[] = ['Public', 'Standard', 'Hsm', 'Byok', 'Hyok']

/** Every level but Public is a secret. */
export const isSecretProtection = (protection: string | null | undefined): boolean =>
  protection !== undefined && protection !== null && protection !== 'Public'

// KeyValueRules of Backend.Core: dot-separated segments of letters, digits and underscores, not starting with a digit.
const keyPattern = /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/

export const maxKeyValueKeyLength = 128

/** True for a key the API accepts; keys are case-insensitive and stored lower-cased. */
export const isValidKeyValueKey = (key: string | null | undefined): boolean =>
  typeof key === 'string' && key.length > 0 && key.length <= maxKeyValueKeyLength && keyPattern.test(key)

/** The stored form of a key: trimmed and lower-cased. */
export const normalizeKeyValueKey = (key: string): string => key.trim().toLowerCase()

// --- .env ---------------------------------------------------------------------------------------------------------

export interface DotEnvEntry {
  key: string
  value: string
}

export interface DotEnvParseOptions {
  /** Turn `DB__PASSWORD` into `db.password` (default true): `__` becomes `.` and the key is lower-cased. */
  normalizeKeys?: boolean
}

export interface DotEnvSerializeOptions {
  /** Turn `db.password` into `DB__PASSWORD` (default true): `.` becomes `__` and the key is upper-cased. */
  envKeys?: boolean
}

/**
 * Parses `.env` text: `KEY=VALUE` lines, `#` comments and empty lines, an optional `export ` prefix, double-quoted values
 * with `\n`, `\r`, `\t`, `\"` and `\\` escapes, single-quoted literal values, and unquoted values trimmed up to a ` #` comment.
 * A line without `=` throws.
 */
export function parseDotEnv(text: string, options: DotEnvParseOptions = {}): DotEnvEntry[] {
  const normalize = options.normalizeKeys ?? true
  const entries: DotEnvEntry[] = []
  const lines = text.split(/\r?\n/)

  for (let index = 0; index < lines.length; index++) {
    const raw = lines[index]!
    const line = raw.trim()

    if (line.length === 0 || line.startsWith('#')) {
      continue
    }

    const separator = line.indexOf('=')

    if (separator <= 0) {
      throw new Error(`Line ${index + 1} is not KEY=VALUE: ${raw}`)
    }

    let key = line.slice(0, separator).trim()

    if (key.startsWith('export ')) {
      key = key.slice('export '.length).trim()
    }

    let rest = line.slice(separator + 1).trim()
    let value: string

    if (rest.startsWith('"')) {
      // a double-quoted value may span lines
      while (!closesDoubleQuote(rest) && index + 1 < lines.length) {
        index++
        rest += `\n${lines[index]}`
      }

      const end = findClosingQuote(rest, '"')

      if (end < 0) {
        throw new Error(`Line ${index + 1} has an unterminated quoted value.`)
      }

      value = unescapeDoubleQuoted(rest.slice(1, end))
    } else if (rest.startsWith("'")) {
      const end = rest.indexOf("'", 1)

      if (end < 0) {
        throw new Error(`Line ${index + 1} has an unterminated quoted value.`)
      }

      value = rest.slice(1, end)
    } else {
      const comment = rest.search(/\s#/)
      value = (comment >= 0 ? rest.slice(0, comment) : rest).trim()
    }

    entries.push({ key: normalize ? fromEnvKey(key) : key, value })
  }

  return entries
}

/** Serializes entries as `.env` lines; values with spaces, quotes, `#`, `=` or line breaks are double-quoted and escaped. */
export function serializeDotEnv(entries: readonly DotEnvEntry[], options: DotEnvSerializeOptions = {}): string {
  const envKeys = options.envKeys ?? true

  return entries
    .map((entry) => `${envKeys ? toEnvKey(entry.key) : entry.key}=${quoteIfNeeded(entry.value)}`)
    .join('\n')
}

/** `db.password` → `DB__PASSWORD`. */
export const toEnvKey = (key: string): string => key.replaceAll('.', '__').toUpperCase()

/** `DB__PASSWORD` → `db.password`. */
export const fromEnvKey = (key: string): string => key.replaceAll('__', '.').toLowerCase()

function closesDoubleQuote(text: string): boolean {
  return findClosingQuote(text, '"') >= 0
}

function findClosingQuote(text: string, quote: string): number {
  for (let index = 1; index < text.length; index++) {
    const character = text[index]

    if (character === '\\') {
      index++
      continue
    }

    if (character === quote) {
      return index
    }
  }

  return -1
}

function unescapeDoubleQuoted(text: string): string {
  return text.replace(/\\([nrt"\\])/g, (_, escaped: string) => {
    switch (escaped) {
      case 'n':
        return '\n'
      case 'r':
        return '\r'
      case 't':
        return '\t'
      default:
        return escaped
    }
  })
}

function quoteIfNeeded(value: string): string {
  if (value.length > 0 && !/[\s"'#=\\]/.test(value)) {
    return value
  }

  const escaped = value
    .replaceAll('\\', '\\\\')
    .replaceAll('"', '\\"')
    .replaceAll('\n', '\\n')
    .replaceAll('\r', '\\r')
    .replaceAll('\t', '\\t')

  return `"${escaped}"`
}

// --- JSON ---------------------------------------------------------------------------------------------------------

/** A flat object of key → string value, the shape of a JSON export without levels. */
export type FlatKeyValues = Record<string, string>

/**
 * Flattens nested JSON to dotted keys: `{ db: { host: 'x', port: 5432 } }` → `{ 'db.host': 'x', 'db.port': '5432' }`.
 * Strings stay as they are, other primitives become their JSON text, arrays become one JSON value, null becomes ''.
 */
export function flattenJson(value: unknown, prefix = ''): FlatKeyValues {
  const flat: FlatKeyValues = {}

  if (isPlainObject(value)) {
    for (const [name, child] of Object.entries(value)) {
      const key = prefix ? `${prefix}.${name}` : name
      Object.assign(flat, flattenJson(child, key))
    }

    return flat
  }

  if (prefix === '') {
    throw new Error('The JSON to flatten must be an object.')
  }

  flat[prefix] = toText(value)
  return flat
}

/** The reverse of flattenJson: dotted keys become nested objects; a key that is both a value and a prefix throws. */
export function unflattenJson(flat: FlatKeyValues): Record<string, unknown> {
  const root: Record<string, unknown> = {}

  for (const [key, value] of Object.entries(flat)) {
    const segments = key.split('.')
    let node = root

    for (const segment of segments.slice(0, -1)) {
      const existing = node[segment]

      if (existing === undefined) {
        const child: Record<string, unknown> = {}
        node[segment] = child
        node = child
      } else if (isPlainObject(existing)) {
        node = existing
      } else {
        throw new Error(`The key '${key}' conflicts with the value '${segments.slice(0, segments.indexOf(segment) + 1).join('.')}'.`)
      }
    }

    const leaf = segments.at(-1)!

    if (isPlainObject(node[leaf])) {
      throw new Error(`The key '${key}' conflicts with the keys below it.`)
    }

    node[leaf] = value
  }

  return root
}

/** The items of a document (or an export) as a flat object; secret items without a value are left out. */
export function toFlatJson(document: KeyValueDocument | readonly KeyValueDocumentItem[]): FlatKeyValues {
  const items = Array.isArray(document) ? document : ((document as KeyValueDocument).Items ?? [])
  const flat: FlatKeyValues = {}

  for (const item of items) {
    if (item.Key && item.Value !== undefined && item.Value !== null) {
      flat[item.Key] = item.Value
    }
  }

  return flat
}

export interface ToDocumentOptions {
  /** The level of every item (default Public). */
  protection?: KeyValueProtectionName
  contentType?: string | null
}

/** Builds a 2s.key-values.v1 document from a flat object or .env entries. */
export function toKeyValueDocument(source: FlatKeyValues | readonly DotEnvEntry[], options: ToDocumentOptions = {}): KeyValueDocument {
  const entries: readonly DotEnvEntry[] = Array.isArray(source)
    ? source
    : Object.entries(source as FlatKeyValues).map(([key, value]) => ({ key, value }))

  return {
    Format: keyValueDocumentFormat,
    Items: entries.map((entry) => ({
      Key: entry.key,
      Protection: (options.protection ?? 'Public') as KeyValueDocumentItem['Protection'],
      Value: entry.value,
      ContentType: options.contentType ?? undefined,
    })),
  }
}

export type KeyValueFormat = 'document' | 'flat-json' | 'json' | 'dotenv' | 'unknown'

/**
 * Guesses what pasted text is: a 2s.key-values.v1 document, a flat JSON object of strings, nested JSON, `.env` lines,
 * or nothing recognizable.
 */
export function detectKeyValueFormat(text: string): KeyValueFormat {
  const trimmed = text.trim()

  if (trimmed.length === 0) {
    return 'unknown'
  }

  if (trimmed.startsWith('{')) {
    let parsed: unknown

    try {
      parsed = JSON.parse(trimmed)
    } catch {
      return 'unknown'
    }

    if (!isPlainObject(parsed)) {
      return 'unknown'
    }

    if (parsed.Format === keyValueDocumentFormat || Array.isArray(parsed.Items)) {
      return 'document'
    }

    return Object.values(parsed).every((value) => value === null || typeof value !== 'object') ? 'flat-json' : 'json'
  }

  const lines = trimmed.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0 && !line.startsWith('#'))
  return lines.length > 0 && lines.every((line) => /^(export\s+)?[^=\s]+\s*=/.test(line)) ? 'dotenv' : 'unknown'
}

// --- References ---------------------------------------------------------------------------------------------------

/** A public pair whose value points to a secret in the customer's own Key Vault; the platform never resolves it. */
export const secretReferenceContentType = 'application/vnd.2s.secret-reference'

/** Azure App Configuration's Key Vault reference, accepted as it is. */
export const appConfigurationReferenceContentType = 'application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8'

export const isSecretReference = (contentType: string | null | undefined): boolean =>
  contentType === secretReferenceContentType || contentType === appConfigurationReferenceContentType

export interface SecretReference {
  vault: string
  name: string
  version?: string
  /** The full https URI of the secret. */
  uri: string
}

const vaultUriPattern = /^https:\/\/([a-z0-9-]+)\.vault\.azure\.net\/secrets\/([A-Za-z0-9-]+)(?:\/([a-f0-9]{32}))?\/?$/i
const shorthandPattern = /^kv:\/\/([a-z0-9-]+)\/([A-Za-z0-9-]+)(?:\/([a-f0-9]{32}))?\/?$/i

/** Parses a reference value: `kv://{vault}/{name}[/{version}]`, the vault URI, or App Configuration's `{ "uri": … }`. */
export function parseSecretReference(value: string, contentType?: string | null): SecretReference | undefined {
  let text = value.trim()

  if (contentType === appConfigurationReferenceContentType || text.startsWith('{')) {
    try {
      const parsed = JSON.parse(text) as { uri?: unknown }
      text = typeof parsed.uri === 'string' ? parsed.uri.trim() : ''
    } catch {
      return undefined
    }
  }

  const match = vaultUriPattern.exec(text) ?? shorthandPattern.exec(text)

  if (!match) {
    return undefined
  }

  const [, vault, name, version] = match
  return {
    vault: vault!.toLowerCase(),
    name: name!,
    version: version?.toLowerCase(),
    uri: formatSecretReference({ vault: vault!, name: name!, version }),
  }
}

/** The https URI of a reference. */
export const formatSecretReference = (reference: Pick<SecretReference, 'vault' | 'name' | 'version'>): string =>
  `https://${reference.vault.toLowerCase()}.vault.azure.net/secrets/${reference.name}${reference.version ? `/${reference.version.toLowerCase()}` : ''}`

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function toText(value: unknown): string {
  if (value === null || value === undefined) {
    return ''
  }

  return typeof value === 'string' ? value : JSON.stringify(value)
}
