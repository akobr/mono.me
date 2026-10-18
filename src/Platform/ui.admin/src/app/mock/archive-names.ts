import type { ViewArchiveContent, ViewExportFormat, ViewImportItemKind } from '@42for.net/2splatform.sdk'
import {
  canDescend,
  cannotDescendMessage,
  getAnnotationTypeInfo,
  typeCodes,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'

// File names of the documents in an archive, a port of ViewArchiveNames (Backend.Core, export-import.md). The name
// tells the kind, so folders play no part:
//
//   <key>.json                       annotation (annotations have no versions)
//   <key>.conf[.<n>].json            configuration
//   <type>.template[.<n>].json       template
//   <type>.schema[.<n>].json         type schema
//   <key>.schema[.<n>].json          annotation schema
//   <key>.<type>.schema[.<n>].json   descendant-type schema below the key
//
// <n> is an earlier version. Each segment of a key percent-encodes the characters that file systems reject; names
// never contain '.', so the dots stay separators. A key whose second segment is "template" or "schema" (in any
// case) gets the first character of that segment encoded, so it never reads as a file of its type. Which types
// descend from a key comes from annotation-key.ts, as ViewArchiveNames takes it from DescendantTypeRules.

/** One document of an archive (ViewDocumentKind of the API). */
export type ViewDocumentKind = ViewImportItemKind

export const manifestFileName = 'export.json'

const extension = '.json'
const configurationSuffix = 'conf'
const templateSuffix = 'template'
const schemaSuffix = 'schema'

const folders: Record<ViewArchiveContent, string> = {
  Annotations: 'annotations',
  Schemas: 'schemas',
  Templates: 'templates',
  Configurations: 'configurations',
}

/** The document a file name stands for. */
export interface ArchiveName {
  kind: ViewDocumentKind
  /** The lower-case type code of a template, a type schema or a descendant-type schema. */
  type?: AnnotationTypeCode
  /** The key of an annotation, a configuration or an annotation schema, or the ancestor of a descendant-type schema. */
  key?: string
  /** An earlier version; undefined for the current document. */
  version?: number
  /** Set for a document that cannot be imported whatever its content (InvalidKey). */
  errorCode?: string
  message?: string
}

const isWord = (segment: string, word: string) => segment.toLowerCase() === word

const codeOf = (segment: string) =>
  typeCodes.find((code) => code === segment.toLowerCase()) as AnnotationTypeCode | undefined

// re-exported, so the importers of the port keep finding it here
export { canDescend }

const reserved = new Set(['%', '<', '>', ':', '"', '/', '\\', '|', '?', '*'])

const mustEncode = (character: string) => {
  const code = character.codePointAt(0)!
  return reserved.has(character) || code < 0x20 || code === 0x7f
}

const percent = (byte: number) => `%${byte.toString(16).toUpperCase().padStart(2, '0')}`

const encodeSegment = (segment: string) =>
  [...segment]
    .map((character) =>
      mustEncode(character) ? [...new TextEncoder().encode(character)].map(percent).join('') : character,
    )
    .join('')

/** The key with each segment encoded; the reserved second segment gets its first character (a letter) encoded. */
function keyName(key: string): string {
  return key
    .split('.')
    .map((segment, index) => {
      const encoded = encodeSegment(segment)
      return index === 1 && (isWord(segment, templateSuffix) || isWord(segment, schemaSuffix))
        ? `${percent(encoded.charCodeAt(0))}${encoded.slice(1)}`
        : encoded
    })
    .join('.')
}

function normalizeType(type: string): AnnotationTypeCode {
  const code = codeOf(type)
  if (!code) {
    throw new RangeError(`Unknown annotation type code '${type}'.`)
  }
  return code
}

const withVersion = (name: string, version?: number) =>
  `${name}${version === undefined ? '' : `.${version}`}${extension}`

export function getAnnotationFileName(key: string): string {
  return `${keyName(key)}${extension}`
}

export function getConfigurationFileName(key: string, version?: number): string {
  return withVersion(`${keyName(key)}.${configurationSuffix}`, version)
}

export function getTemplateFileName(type: string, version?: number): string {
  return withVersion(`${normalizeType(type)}.${templateSuffix}`, version)
}

export function getTypeSchemaFileName(type: string, version?: number): string {
  return withVersion(`${normalizeType(type)}.${schemaSuffix}`, version)
}

export function getAnnotationSchemaFileName(key: string, version?: number): string {
  return withVersion(`${keyName(key)}.${schemaSuffix}`, version)
}

export function getDescendantTypeSchemaFileName(ancestorKey: string, type: string, version?: number): string {
  return withVersion(`${keyName(ancestorKey)}.${normalizeType(type)}.${schemaSuffix}`, version)
}

/** The file name of a document of any kind; annotations have no versions. */
export function getFileName(
  kind: ViewDocumentKind,
  type: string | undefined,
  key: string | undefined,
  version?: number,
) {
  const needsKey = kind !== 'Template' && kind !== 'TypeSchema'
  const needsType = kind === 'Template' || kind === 'TypeSchema' || kind === 'DescendantTypeSchema'
  if (needsKey && !key) {
    throw new TypeError(`A document of the kind ${kind} needs its annotation key.`)
  }
  if (needsType && !type) {
    throw new TypeError(`A document of the kind ${kind} needs its type code.`)
  }

  switch (kind) {
    case 'Annotation':
      if (version !== undefined) {
        throw new RangeError('Annotations have no versions.')
      }
      return getAnnotationFileName(key!)
    case 'Configuration':
      return getConfigurationFileName(key!, version)
    case 'Template':
      return getTemplateFileName(type!, version)
    case 'TypeSchema':
      return getTypeSchemaFileName(type!, version)
    case 'AnnotationSchema':
      return getAnnotationSchemaFileName(key!, version)
    case 'DescendantTypeSchema':
      return getDescendantTypeSchemaFileName(key!, type!, version)
  }
}

/** The kind of archive content a document belongs to. */
export function getContent(kind: ViewDocumentKind): ViewArchiveContent {
  switch (kind) {
    case 'Annotation':
      return 'Annotations'
    case 'Template':
      return 'Templates'
    case 'Configuration':
      return 'Configurations'
    default:
      return 'Schemas'
  }
}

/** The folder of a kind in an export; the import does not look at it. */
export function getFolder(content: ViewArchiveContent): string {
  return folders[content]
}

/** The path of a document in an export: the folder of its kind and its file name. */
export function getPath(kind: ViewDocumentKind, type: string | undefined, key: string | undefined, version?: number) {
  return `${getFolder(getContent(kind))}/${getFileName(kind, type, key, version)}`
}

/**
 * A document in words, for messages: "annotation rst.payments", "type schema exe", "exe schema below sbt.northwind",
 * "schema of rst.payments", "template exe", "configuration of rst.payments".
 */
export function describe(kind: ViewDocumentKind, type: string | undefined, key: string | undefined): string {
  switch (kind) {
    case 'Annotation':
      return `annotation ${key}`
    case 'TypeSchema':
      return `type schema ${type}`
    case 'DescendantTypeSchema':
      return `${type} schema below ${key}`
    case 'AnnotationSchema':
      return `schema of ${key}`
    case 'Template':
      return `template ${type}`
    case 'Configuration':
      return `configuration of ${key}`
  }
}

/** A non-negative integer of at most 64 bits, without a sign or spaces (ulong.TryParse with NumberStyles.None). */
function parseVersion(segment: string): number | undefined {
  return /^\d+$/.test(segment) && BigInt(segment) <= 0xffff_ffff_ffff_ffffn ? Number(segment) : undefined
}

/** Decodes the %XX sequences of a segment as UTF-8; undefined for a broken sequence. */
function decodeSegment(segment: string): string | undefined {
  if (!segment.includes('%')) {
    return segment
  }

  const bytes: number[] = []
  let text = ''
  const flush = () => {
    if (bytes.length) {
      text += new TextDecoder().decode(new Uint8Array(bytes))
      bytes.length = 0
    }
  }

  for (let index = 0; index < segment.length; index++) {
    if (segment[index] === '%') {
      const hex = segment.slice(index + 1, index + 3)
      if (index + 2 >= segment.length || !/^[0-9a-fA-F]{2}$/.test(hex)) {
        return undefined
      }
      bytes.push(parseInt(hex, 16))
      index += 2
      continue
    }
    flush()
    text += segment[index]
  }

  flush()
  return text
}

function descendant(key: string, type: AnnotationTypeCode, version: number | undefined): ArchiveName {
  const name: ArchiveName = { kind: 'DescendantTypeSchema', type, key, version }
  // still a document, so a misplaced schema is reported instead of silently ignored
  return canDescend(key, type) ? name : { ...name, errorCode: 'InvalidKey', message: cannotDescendMessage(key, type) }
}

/** The document a file name (without folders) stands for, or undefined when it is not a document. */
export function parseArchiveName(fileName: string): ArchiveName | undefined {
  if (!fileName.toLowerCase().endsWith(extension)) {
    return undefined
  }

  const segments = fileName.slice(0, -extension.length).split('.')
  const type = codeOf(segments[0]!)

  if (segments.length < 2 || segments.some((segment) => !segment) || !type) {
    return undefined
  }

  // a literal (not encoded) second segment "template" or "schema" is a file of the type itself
  if (isWord(segments[1]!, templateSuffix) || isWord(segments[1]!, schemaSuffix)) {
    const version = segments.length === 3 ? parseVersion(segments[2]!) : undefined
    if (segments.length > 3 || (segments.length === 3 && version === undefined)) {
      return undefined
    }
    return { kind: isWord(segments[1]!, templateSuffix) ? 'Template' : 'TypeSchema', type, version }
  }

  const count = getAnnotationTypeInfo(type)!.segments
  if (segments.length < count) {
    return undefined
  }

  const names = segments.slice(1, count).map(decodeSegment)
  if (names.some((name) => !name || name.includes('.'))) {
    return undefined
  }

  const key = [type, ...names].join('.')
  const suffix = segments.slice(count)
  const [first, second, third] = suffix

  if (suffix.length === 0) {
    return { kind: 'Annotation', key }
  }
  if (suffix.length <= 2 && (isWord(first!, configurationSuffix) || isWord(first!, schemaSuffix))) {
    const version = second === undefined ? undefined : parseVersion(second)
    if (second !== undefined && version === undefined) {
      return undefined
    }
    return { kind: isWord(first!, configurationSuffix) ? 'Configuration' : 'AnnotationSchema', key, version }
  }
  if (suffix.length <= 3 && second !== undefined && isWord(second, schemaSuffix) && codeOf(first!)) {
    const version = third === undefined ? undefined : parseVersion(third)
    return third !== undefined && version === undefined ? undefined : descendant(key, codeOf(first!)!, version)
  }

  return undefined
}

/**
 * The same document in two files has the same identity, whatever the folders, the version and the spelling of the
 * key (encoded or plain).
 */
export const identityOf = (name: ArchiveName) => `${name.kind}:${name.type ?? ''}:${name.key ?? ''}`

/**
 * A path (with '/' separators) that an archive's reader passes over without counting it: any hidden folder or file (a
 * segment starting with '.': .git, .github, .vscode, .DS_Store, ._*) and the __MACOSX folder of a zip. A "./" prefix
 * is the current folder, not a hidden one.
 */
export function isSkippedPath(path: string): boolean {
  const segments = path.split('/').filter(Boolean)
  return (
    segments.length === 0 ||
    segments.some((segment) => segment.startsWith('.') && segment !== '.') ||
    segments[0]!.toLowerCase() === '__macosx'
  )
}

/** The name of an export: export-{organization}-{project}-{view}[-git]-{yyyyMMdd-HHmmss}Z.zip, in UTC. */
export function getArchiveFileName(
  org: string,
  project: string,
  view: string,
  format: ViewExportFormat,
  createdAt: Date,
): string {
  const stamp = createdAt.toISOString().replaceAll('-', '').replace('T', '-').replaceAll(':', '').slice(0, 15)
  return `export-${org}-${project}-${view}${format === 'Git' ? '-git' : ''}-${stamp}Z.zip`
}
