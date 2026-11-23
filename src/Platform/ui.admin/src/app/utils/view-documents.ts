import {
  getAnnotation,
  getConfiguration,
  getConfigurationVersion,
  hasErrorCode,
  isNotFound,
  type Annotation,
  type StorytellerRequestInit,
} from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'
import { isUnitAnnotation } from '@/utils/unit-types'

// The documents that are compared and copied between two views of a project (spec "Copy and Compare Annotations and
// Configurations Between Views", section 1).

/** What a comparison or a copy is about. */
export type ViewItemKind = 'annotations' | 'configurations'

/** The document of a configuration that is compared: the stored own one, or the merged effective one. */
export type ConfigurationContent = 'own' | 'effective'

/** One annotation, or one configuration: "1 annotation", "3 configurations". */
export function countOf(kind: ViewItemKind, count: number): string {
  const noun = kind === 'annotations' ? 'annotation' : 'configuration'
  return `${new Intl.NumberFormat().format(count)} ${noun}${count === 1 ? '' : 's'}`
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value)

/** Null, an empty text, an empty array and an empty object are how an annotation says "not set". */
function isUnset(value: unknown): boolean {
  if (value === null || value === undefined || value === '') {
    return true
  }
  return Array.isArray(value) ? value.length === 0 : isObject(value) && Object.keys(value).length === 0
}

/** The instant of a date in UTC; a text that is no date stays as it is. */
function instant(value: unknown): unknown {
  if (typeof value !== 'string') {
    return value
  }
  const time = Date.parse(value)
  return Number.isNaN(time) ? value : new Date(time).toISOString()
}

/**
 * The editable members of an annotation, in the order of an annotation file of the view export, as the API compares
 * two annotations (AnnotationAuditDiff): a top-level member that is not set is left out (a disabled flag that is
 * false too), the labels are a set, and the dates are instants. Inside Values nothing is left out: a name with null
 * or an empty text is a value. The key, the names, the view, the stamps and the relation arrays are no part of it.
 */
export function annotationDocument(annotation: Annotation): Record<string, unknown> {
  const unit = annotation as Annotation & { UnitType?: string | null; UnitDefinition?: string | null }
  const members: [string, unknown][] = [
    ['Title', annotation.Title],
    ['Description', annotation.Description],
    ['DocumentationLink', annotation.DocumentationLink],
    ['Labels', annotation.Labels?.length ? [...new Set(annotation.Labels)].toSorted() : undefined],
    ['Values', annotation.Values],
    ['IsDisabled', annotation.IsDisabled ? true : undefined],
    ['ValidFrom', instant(annotation.ValidFrom)],
    ['ExpiresAt', instant(annotation.ExpiresAt)],
    ['TimeZone', annotation.TimeZone],
    ...(isUnitAnnotation(annotation)
      ? ([
          ['UnitType', unit.UnitType],
          ['UnitDefinition', unit.UnitDefinition],
        ] satisfies [string, unknown][])
      : []),
  ]

  const document: Record<string, unknown> = {}
  for (const [name, value] of members) {
    if (!isUnset(value)) {
      document[name] = value
    }
  }
  return document
}

const indentation = '  '

/** Writes a value as JSON with two spaces; `sorted` puts the members of every object in the order of their names. */
function write(value: unknown, depth: number, sorted: boolean): string {
  if (Array.isArray(value)) {
    if (value.length === 0) {
      return '[]'
    }
    const inner = indentation.repeat(depth + 1)
    // JSON has no undefined: an item of an array is null, as JSON.stringify writes it.
    const items = value.map((item) => `${inner}${write(item === undefined ? null : item, depth + 1, sorted)}`)
    return `[\n${items.join(',\n')}\n${indentation.repeat(depth)}]`
  }

  if (isObject(value)) {
    // Object.keys, never a copy into a new object: an assignment would lose a member named __proto__ and would put
    // names that look like numbers first.
    const names = Object.keys(value).filter((name) => value[name] !== undefined)
    if (names.length === 0) {
      return '{}'
    }
    if (sorted) {
      names.sort()
    }
    const inner = indentation.repeat(depth + 1)
    const members = names.map((name) => `${inner}${JSON.stringify(name)}: ${write(value[name], depth + 1, sorted)}`)
    return `{\n${members.join(',\n')}\n${indentation.repeat(depth)}}`
  }

  return JSON.stringify(value ?? null)
}

/**
 * JSON with two spaces whose object members are in the order of their names at every level; arrays keep their
 * order. Two documents with the same content have the same text, whatever the order of their members.
 */
export function canonicalJson(value: unknown): string {
  return write(value, 0, true)
}

/** The text of `annotationDocument`: its members in their fixed order, everything below them in the order of names. */
export function annotationText(annotation: Annotation): string {
  const document = annotationDocument(annotation)
  const names = Object.keys(document)
  if (names.length === 0) {
    return '{}'
  }
  const members = names.map((name) => `${indentation}${JSON.stringify(name)}: ${write(document[name], 1, true)}`)
  return `{\n${members.join(',\n')}\n}`
}

/**
 * Whether the value holds an integer that the browser cannot hold exactly (beyond 2^53). JSON.parse has rounded it,
 * while the server keeps it exactly, so such a document can neither be compared nor copied truthfully.
 */
export function hasInexactNumber(value: unknown): boolean {
  if (typeof value === 'number') {
    // A number too large for a double (1e400) reads as Infinity, which JSON writes as null.
    return !Number.isFinite(value) || (Number.isInteger(value) && !Number.isSafeInteger(value))
  }
  if (Array.isArray(value)) {
    return value.some(hasInexactNumber)
  }
  return isObject(value) && Object.values(value).some(hasInexactNumber)
}

/**
 * The own document of a configuration, or undefined for none. An object without members counts as none, as in the
 * API (HasContent, the view import): the editor's "Create empty" stores one.
 */
export function ownDocument(content: unknown): Record<string, unknown> | undefined {
  return isObject(content) && Object.keys(content).length > 0 ? content : undefined
}

export interface AnnotationSide {
  /** The annotation, whatever its status; undefined when the view does not have the key, or has it deleted. */
  annotation?: Annotation
  /** The key is deleted in the view; only a read with `includeDeleted` tells. */
  deleted: boolean
}

/**
 * The point read of an annotation: the lists (and so the catalog) answer the numbers and dates of Values in another
 * form. An inactive annotation is read too. A key that the view does not have is no error.
 */
export async function readAnnotation(
  scope: Scope,
  key: string,
  options: StorytellerRequestInit & { includeDeleted?: boolean } = {},
): Promise<AnnotationSide> {
  const { includeDeleted, ...init } = options
  try {
    const annotation = await getAnnotation(
      scope.org,
      scope.project,
      scope.view,
      key,
      includeDeleted ? { includeInactive: true, includeDeleted: true } : { includeInactive: true },
      init,
    )
    return annotation.Deletion ? { deleted: true } : { annotation, deleted: false }
  } catch (error) {
    if (isNotFound(error)) {
      return { deleted: false }
    }
    throw error
  }
}

export interface ConfigurationSide {
  /** The effective document could be read: the annotation is in the view and is not deleted. */
  readable: boolean
  /** The annotation is deleted in the view. */
  deleted: boolean
  /** The version of the own document; 0 when nothing was ever written. */
  version: number
  effective?: unknown
  /** The own document; undefined when nothing, or a document without members, is stored. */
  own?: Record<string, unknown>
}

/**
 * The effective document of a configuration and its own one, as `useConfiguration` reads them: GetConfiguration, then
 * GetConfigurationVersion of its Version (also of version 0), whose 404 means that nothing is stored. A key without
 * an annotation in the view is no error.
 */
export async function readConfiguration(
  scope: Scope,
  key: string,
  options: StorytellerRequestInit = {},
): Promise<ConfigurationSide> {
  let effective
  try {
    effective = await getConfiguration(scope.org, scope.project, scope.view, key, options)
  } catch (error) {
    if (isNotFound(error)) {
      return { readable: false, deleted: hasErrorCode(error, 'AnnotationDeleted'), version: 0 }
    }
    throw error
  }

  const version = effective.Version ?? 0
  let own: Record<string, unknown> | undefined
  try {
    own = ownDocument((await getConfigurationVersion(scope.org, scope.project, scope.view, key, version, options)).Content)
  } catch (error) {
    if (!isNotFound(error)) {
      throw error
    }
  }
  return { readable: true, deleted: false, version, effective: effective.Content ?? {}, own }
}
