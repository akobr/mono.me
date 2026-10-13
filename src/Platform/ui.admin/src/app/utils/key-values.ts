import {
  detectKeyValueFormat,
  flattenJson,
  isStorytellerError,
  isValidKeyValueKey,
  parseDotEnv,
  type KeyValueDocument,
  type KeyValueDocumentItem,
  type KeyValueFormat,
  type KeyValueProtection,
} from '@42for.net/2splatform.sdk'

// The protection levels of key/value pairs (key-values.md): what each means for the UI, and the small helpers the
// screens share (bindings, masking, import parsing, the ProtectionInUse counts).

export const levels: readonly KeyValueProtection[] = ['Public', 'Standard', 'Hsm', 'Byok', 'Hyok']

export const secretLevels: readonly KeyValueProtection[] = ['Standard', 'Hsm', 'Byok', 'Hyok']

export interface LevelMeta {
  label: string
  icon: string
  description: string
  /** What a project needs before the level can be allowed. */
  requirement?: string
  /** The cost hint of the Secrets settings. */
  cost: string
}

export const levelMeta: Record<KeyValueProtection, LevelMeta> = {
  Public: {
    label: 'Public',
    icon: 'i-lucide-globe',
    description: 'Stored as plain text; every reader of the project sees the value.',
    cost: 'Included',
  },
  Standard: {
    label: 'Standard',
    icon: 'i-lucide-lock',
    description: 'Encrypted in the database with a software key of the project in Azure Key Vault.',
    cost: 'Cheapest secret level',
  },
  Hsm: {
    label: 'HSM',
    icon: 'i-lucide-lock-keyhole',
    description: 'Encrypted with a hardware key of the project (Key Vault Premium or a Managed HSM), rotated by the platform.',
    requirement: 'An HSM provider is configured in the deployment.',
    cost: 'Per key and month, plus the HSM key',
  },
  Byok: {
    label: 'BYOK',
    icon: 'i-lucide-key-square',
    description: 'Encrypted with your own key in your vault and tenant; revoking the key makes every value unreadable.',
    requirement: 'A verified customer key.',
    cost: 'Per key and month; wrap and unwrap operations are billed to your vault',
  },
  Hyok: {
    label: 'HYOK',
    icon: 'i-lucide-eye-off',
    description: 'Encrypted in your browser or application for the registered recipients; the platform never reads it.',
    requirement: 'At least one enabled recipient.',
    cost: 'Per key and month',
  },
}

export const isSecretLevel = (level: KeyValueProtection | string | undefined): boolean =>
  !!level && level !== 'Public'

export const isClientEncrypted = (level: KeyValueProtection | string | undefined): boolean => level === 'Hyok'

/** The levels protect can reach on the server; Public and Hyok need a new value from the client. */
export const serverSideLevels: readonly KeyValueProtection[] = ['Standard', 'Hsm', 'Byok']

export const maskedValue = '••••••••'

/** The binding a configuration uses for a pair (binding.md): the default source, or the explicit one. */
export function bindingFor(key: string, source: 'default' | 'public' | 'secret' = 'default'): string {
  return source === 'default' ? `@${key}` : `@${source}(${key})`
}

/** The first line of a public value, shortened for a table cell. */
export function valuePreview(value: string | undefined, max = 60): string {
  if (!value) {
    return ''
  }
  const line = value.split(/\r?\n/, 1)[0] ?? ''
  return line.length > max ? `${line.slice(0, max)}…` : line
}

export const secretReferenceContentType = 'application/vnd.2s.secret-reference'

/** Content type choices of the form; "Secret reference" marks a public pointer to a secret in your own vault. */
export const contentTypes: { label: string; value: string }[] = [
  { label: 'Text', value: 'text/plain' },
  { label: 'JSON', value: 'application/json' },
  { label: 'Secret reference', value: secretReferenceContentType },
]

export const maxValueBytes = 24_576

export function utf8Length(value: string): number {
  return new TextEncoder().encode(value).length
}

/** Days until a deleted key is removed for good. */
export function daysUntil(time: string | null | undefined, now = new Date()): number | undefined {
  if (!time) {
    return undefined
  }
  return Math.max(0, Math.ceil((new Date(time).getTime() - now.getTime()) / 86_400_000))
}

export interface ProtectionInUse {
  level?: string
  counts: { view: string; count: number }[]
}

/** The counts per view of a 409 ProtectionInUse ("… ({default: 3, staging: 1})"). */
export function parseProtectionInUse(error: unknown): ProtectionInUse | undefined {
  if (!isStorytellerError(error) || error.errorCode !== 'ProtectionInUse') {
    return undefined
  }
  const level = /level '([A-Za-z]+)'/.exec(error.message)?.[1]
  const inside = /\(([^)]*)\)/.exec(error.message)?.[1] ?? ''
  const counts = inside
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const [view, count] = part.split(':').map((text) => text.trim())
      return { view: view ?? '', count: Number(count) || 0 }
    })
  return { level, counts }
}

// --- import ---

export interface ImportItem {
  key: string
  value: string
  protection: KeyValueProtection
  contentType?: string
  /** A key the API would not accept. */
  invalid?: boolean
}

export interface ParsedImport {
  format: KeyValueFormat
  items: ImportItem[]
}

/** Parses pasted text or a file into import rows; flat JSON and .env rows start as Public. */
export function parseImportText(text: string, format?: KeyValueFormat): ParsedImport {
  const detected = format ?? detectKeyValueFormat(text)

  switch (detected) {
    case 'document': {
      const document = JSON.parse(text) as KeyValueDocument
      return {
        format: 'document',
        items: (document.Items ?? []).map((item) => ({
          key: item.Key ?? '',
          value: item.Value ?? '',
          protection: (item.Protection ?? 'Public') as KeyValueProtection,
          contentType: item.ContentType ?? undefined,
          invalid: !isValidKeyValueKey(item.Key ?? ''),
        })),
      }
    }
    case 'flat-json':
    case 'json': {
      const flat = flattenJson(JSON.parse(text))
      return {
        format: detected,
        items: Object.entries(flat).map(([key, value]) => ({
          key,
          value,
          protection: 'Public',
          invalid: !isValidKeyValueKey(key),
        })),
      }
    }
    case 'dotenv':
      return {
        format: 'dotenv',
        items: parseDotEnv(text).map((entry) => ({
          key: entry.key,
          value: entry.value,
          protection: 'Public',
          invalid: !isValidKeyValueKey(entry.key),
        })),
      }
    default:
      throw new Error('The text is neither a key/value document, a JSON object nor .env lines.')
  }
}

/** Shell-style glob over keys: * is any run of characters, ? one character; case-insensitive. */
export function keyMatchesGlob(key: string, glob: string): boolean {
  const pattern = `^${glob.replace(/[.+^${}()|[\]\\]/g, '\\$&').replaceAll('*', '.*').replaceAll('?', '.')}$`
  return new RegExp(pattern, 'i').test(key)
}

/** Gives the level to every row whose key matches the glob. */
export function markMatching(items: ImportItem[], glob: string, protection: KeyValueProtection): ImportItem[] {
  return items.map((item) => (keyMatchesGlob(item.key, glob) ? { ...item, protection } : item))
}

export function toDocument(items: ImportItem[]): KeyValueDocument {
  return {
    Format: '2s.key-values.v1',
    Items: items.map(
      (item): KeyValueDocumentItem => ({
        Key: item.key,
        Protection: item.protection,
        Value: item.value,
        ContentType: item.contentType,
      }),
    ),
  }
}

export const formatLabels: Record<KeyValueFormat, string> = {
  document: 'Key/value document',
  'flat-json': 'Flat JSON',
  json: 'Nested JSON',
  dotenv: '.env',
  unknown: 'Unknown',
}
