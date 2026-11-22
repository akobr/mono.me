import { applyOperation, validate, type Operation } from 'fast-json-patch'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { codeOfKey, type AnnotationTypeCode } from '@/utils/annotation-key'

// The PATCH of an annotation as the API applies it (Backend.Core/src/Annotating/AnnotationPatch.cs and
// AnnotationPatchDocument.cs): a JSON Merge Patch or a JSON Patch over the editable document, then the checks of the
// result. The handler in annotation-handlers.ts runs the API's pipeline around it.

export type AnnotationPatchFormat = 'merge' | 'json-patch'

export const mergePatchMediaType = 'application/merge-patch+json'
export const jsonPatchMediaType = 'application/json-patch+json'
/** The Accept-Patch header of a 415 (RFC 5789). */
export const acceptPatch = `${mergePatchMediaType}, ${jsonPatchMediaType}`

/** The answer of a failed patch: the status and ErrorCode the API maps it to, and its message. */
export interface AnnotationPatchFailure {
  status: 400 | 412
  code: 'InvalidAnnotation' | 'PatchInvalid' | 'PatchTestFailed'
  message: string
}

type Document = Record<string, unknown>

const isObject = (value: unknown): value is Document =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** The members every annotation has in its editable document, in the API's order. */
const commonMembers = [
  'IsDisabled',
  'ValidFrom',
  'ExpiresAt',
  'TimeZone',
  'Title',
  'Description',
  'DocumentationLink',
  'Labels',
  'Values',
] as const
const unitMembers = ['UnitType', 'UnitDefinition'] as const

const isUnit = (annotation: Annotation) => codeOfKey(annotation.AnnotationKey) === 'unt'
const membersOf = (annotation: Annotation): readonly string[] =>
  isUnit(annotation) ? [...commonMembers, ...unitMembers] : commonMembers

/**
 * The members of the stored runtime type that a merge patch ignores (Abstractions.Annotations/src/Model/*.cs): the
 * identity, the stamps, and per type the keys and names of the ancestors and the relation arrays. Any other member that
 * is not editable is unknown.
 */
const ignoredMembers = [
  'ProjectName',
  'ViewName',
  'AnnotationKey',
  'Name',
  'AnnotationType',
  'Created',
  'Modified',
  'Deletion',
]
const ignoredByType: Record<AnnotationTypeCode, string[]> = {
  rst: ['UnitNames'],
  sbt: ['ContextNames', 'ResponsibilityNames'],
  usg: ['SubjectKey', 'ResponsibilityKey', 'SubjectName', 'ResponsibilityName', 'ContextNames'],
  cnt: ['SubjectKey', 'SubjectName', 'ResponsibilityNames'],
  exe: [
    'ResponsibilityKey',
    'SubjectKey',
    'ContextKey',
    'UsageKey',
    'SubjectName',
    'ResponsibilityName',
    'ContextName',
    'UnitNames',
  ],
  unt: ['ResponsibilityKey', 'ResponsibilityName'],
  uxe: [
    'ResponsibilityKey',
    'UnitKey',
    'SubjectKey',
    'ContextKey',
    'UsageKey',
    'ExecutionKey',
    'ResponsibilityName',
    'UnitName',
    'SubjectName',
    'ContextName',
  ],
}

/** ASCII case folding, as the API matches member names (OrdinalIgnoreCase). */
const fold = (name: string) => name.toLowerCase()

/**
 * The patch format by Content-Type (case-insensitive, parameters ignored) and by the body: application/json or none
 * decides by the body (an object is a merge patch, an array a JSON Patch). Another media type is unsupported (415);
 * a body that does not fit its format is a PatchInvalid message.
 */
export function patchFormatOf(
  contentType: string | null,
  body: unknown,
): AnnotationPatchFormat | { unsupported: true } | { invalid: string } {
  const mediaType = (contentType ?? '').split(';')[0]!.trim().toLowerCase()
  if (mediaType === mergePatchMediaType) {
    return isObject(body) ? 'merge' : { invalid: 'A merge patch (RFC 7396) of an annotation is a JSON object.' }
  }
  if (mediaType === jsonPatchMediaType) {
    return Array.isArray(body) ? 'json-patch' : { invalid: 'A JSON Patch (RFC 6902) is a JSON array of operations.' }
  }
  if (mediaType === '' || mediaType === 'application/json') {
    return isObject(body)
      ? 'merge'
      : Array.isArray(body)
        ? 'json-patch'
        : { invalid: 'A patch of an annotation is a JSON object (a merge patch) or a JSON array (a JSON Patch).' }
  }
  return { unsupported: true }
}

/** Whether the media type (parameters ignored) is one a PATCH of an annotation takes; none counts as JSON. */
export function isPatchMediaType(contentType: string | null): boolean {
  const mediaType = (contentType ?? '').split(';')[0]!.trim().toLowerCase()
  return ['', 'application/json', mergePatchMediaType, jsonPatchMediaType].includes(mediaType)
}

const operations = new Set(['add', 'remove', 'replace', 'move', 'copy', 'test'])

/**
 * The checks of a JSON Patch before anything is read (AnnotationPatch.cs l.73-92): every operation an object with one
 * of the six operations, a JSON Pointer path, a value where it needs one and a from where it needs one.
 */
export function jsonPatchProblem(patch: readonly unknown[]): string | undefined {
  const unknown = patch.findIndex((operation) => !isObject(operation) || !operations.has(String(operation.op)))
  if (unknown >= 0) {
    return `Invalid JSON Patch document: the operation ${unknown} is not an operation of RFC 6902.`
  }
  const error = validate(patch as Operation[])
  return error ? `Invalid JSON Patch document: ${error.message.split('\n')[0]}` : undefined
}

/**
 * The editable document of the stored annotation (AnnotationPatchDocument.cs): every member present, null when unset,
 * Labels [] and Values {} when there are none; UnitType and UnitDefinition for units only.
 */
export function editableDocumentOf(annotation: Annotation): Document {
  const record = annotation as unknown as Document
  const document: Document = {}
  for (const member of membersOf(annotation)) {
    document[member] = record[member] ?? null
  }
  // a copy: neither format may change the stored annotation (the API builds the document anew, AnnotationPatch.Apply)
  document.Labels = Array.isArray(annotation.Labels)
    ? annotation.Labels.filter((label: unknown) => typeof label === 'string')
    : []
  document.Values = isObject(annotation.Values) ? structuredClone(annotation.Values) : {}
  return document
}

/** Sets an own member, also one named __proto__ (an assignment would set the prototype instead). */
const setOwn = (target: Document, name: string, value: unknown) =>
  Object.defineProperty(target, name, { value, enumerable: true, writable: true, configurable: true })

/** RFC 7396 with ordinal names, as the API merges Values (AnnotationPatch.MergeInto): null removes a name. */
export function mergeValues(target: unknown, patch: unknown): unknown {
  if (!isObject(patch)) {
    return structuredClone(patch)
  }
  const result: Document = isObject(target) ? structuredClone(target) : {}
  for (const [name, value] of Object.entries(patch)) {
    if (value === null) {
      delete result[name]
    } else {
      setOwn(result, name, mergeValues(Object.hasOwn(result, name) ? result[name] : undefined, value))
    }
  }
  return result
}

const invalid = (message: string): AnnotationPatchFailure => ({ status: 400, code: 'InvalidAnnotation', message })

function applyMerge(current: Annotation, document: Document, patch: Document): AnnotationPatchFailure | undefined {
  const editable = new Map(membersOf(current).map((member) => [fold(member), member]))
  const ignored = new Set(
    [...ignoredMembers, ...(ignoredByType[codeOfKey(current.AnnotationKey) ?? 'rst'] ?? [])].map(fold),
  )
  const seen = new Set<string>()

  for (const [name, value] of Object.entries(patch)) {
    if (seen.has(fold(name))) {
      return invalid(`The annotation patch has the member '${name}' more than once.`)
    }
    seen.add(fold(name))

    const member = editable.get(fold(name))
    if (member === 'Values' && isObject(value)) {
      document.Values = mergeValues(document.Values, value)
    } else if (member) {
      document[member] = structuredClone(value)
    } else if (!ignored.has(fold(name))) {
      return invalid(`The annotation patch has an unknown member '${name}'.`)
    }
  }
  return undefined
}

/**
 * The operations one by one (fast-json-patch, validating each against the document as it is then), so a failure names
 * its own operation: a test that does not hold, or whose path does not exist, is a precondition (412); any other
 * failure is 400, as a move into its own child is (fast-json-patch would apply it).
 */
function applyJsonPatch(document: Document, patch: Operation[]): { document: Document } | AnnotationPatchFailure {
  let result: unknown = structuredClone(document)
  for (const [index, operation] of patch.entries()) {
    const failed = (reason: string): AnnotationPatchFailure => ({
      status: operation.op === 'test' ? 412 : 400,
      code: operation.op === 'test' ? 'PatchTestFailed' : 'PatchInvalid',
      message: `JSON Patch operation ${index} failed: ${reason}`,
    })
    if (operation.op === 'move' && operation.path.startsWith(`${operation.from}/`)) {
      return failed(`The value at '${operation.from}' cannot be moved into itself ('${operation.path}').`)
    }
    try {
      result = applyOperation(result, operation, true, true, true, index).newDocument
    } catch (error) {
      return failed(((error as Error).message ?? '').split('\n')[0]!)
    }
  }
  if (!isObject(result)) {
    return { status: 400, code: 'PatchInvalid', message: 'The patched annotation is not a JSON object.' }
  }
  return { document: result }
}

/** An ISO 8601 date, or date and time with an optional offset, as System.Text.Json reads a DateTimeOffset. */
const isoDate = /^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})?)?$/i
const isDate = (value: unknown) => typeof value === 'string' && isoDate.test(value) && !Number.isNaN(Date.parse(value))

/** The type checks of the patched document (AnnotationPatchDocument.cs l.125-257); the first failure answers. */
function documentProblem(current: Annotation, document: Document): AnnotationPatchFailure | undefined {
  const members = new Set(membersOf(current))
  const unknown = Object.keys(document).find((name) => !members.has(name))
  if (unknown !== undefined) {
    return invalid(`The patched annotation has an unknown member '${unknown}'.`)
  }

  const value = (member: string) => document[member] ?? null
  if (value('IsDisabled') !== null && typeof value('IsDisabled') !== 'boolean') {
    return invalid("The member 'IsDisabled' must be a boolean or null.")
  }
  for (const member of ['ValidFrom', 'ExpiresAt']) {
    if (value(member) !== null && !isDate(value(member))) {
      return invalid(`The member '${member}' must be an ISO 8601 date and time or null.`)
    }
  }
  for (const member of ['TimeZone', 'Title', 'Description', 'DocumentationLink']) {
    if (value(member) !== null && typeof value(member) !== 'string') {
      return invalid(`The member '${member}' must be a string or null.`)
    }
  }
  const labels = value('Labels')
  if (labels !== null && !Array.isArray(labels)) {
    return invalid("The member 'Labels' must be an array of strings or null.")
  }
  const notString = Array.isArray(labels) ? labels.findIndex((label) => typeof label !== 'string') : -1
  if (notString >= 0) {
    return invalid(`The member 'Labels' must be an array of strings: the item ${notString} is not a string.`)
  }
  if (value('Values') !== null && !isObject(value('Values'))) {
    return invalid("The member 'Values' must be an object or null.")
  }
  if (isUnit(current)) {
    const stored = current as unknown as Document
    for (const member of unitMembers) {
      // a legacy unit stored with a null member keeps it until a patch sets it
      if (value(member) === null && stored[member] != null) {
        return invalid(`A unit always has a ${member}: the member '${member}' cannot be removed or null.`)
      }
      if (value(member) !== null && typeof value(member) !== 'string') {
        return invalid(`The member '${member}' must be a string.`)
      }
    }
  }
  return undefined
}

/** Labels keep their first occurrence, compared ordinally ("eu" and "EU" both stay). */
const firstOccurrences = (labels: readonly string[]) => labels.filter((label, index) => labels.indexOf(label) === index)

/**
 * Applies a patch to the stored annotation as the API does: the annotation as it would be stored (not yet checked by
 * the rules of a full write, nor stamped), or the failure the API answers.
 */
export function applyAnnotationPatch(
  current: Annotation,
  format: AnnotationPatchFormat,
  patch: unknown,
): { annotation: Annotation } | AnnotationPatchFailure {
  let document = editableDocumentOf(current)

  if (format === 'merge') {
    const failure = applyMerge(current, document, patch as Document)
    if (failure) {
      return failure
    }
  } else {
    const applied = applyJsonPatch(document, patch as Operation[])
    if (!('document' in applied)) {
      return applied
    }
    document = applied.document
  }

  const problem = documentProblem(current, document)
  if (problem) {
    return problem
  }

  const next: Document = { ...(current as unknown as Document) }
  for (const member of membersOf(current)) {
    const value = document[member] ?? null
    if (value === null) {
      delete next[member]
    } else {
      next[member] = member === 'Labels' ? firstOccurrences(value as string[]) : value
    }
  }
  return { annotation: next as unknown as Annotation }
}
