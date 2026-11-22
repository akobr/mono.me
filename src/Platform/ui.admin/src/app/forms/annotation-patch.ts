import type { AnnotationPatchBody, JsonPatchOperation } from '@42for.net/2splatform.sdk'
import { isUnitAnnotation } from '@/utils/unit-types'
import type { AnnotationForm } from './annotation.schema'

/**
 * The editable members of an annotation as the API stores them (the document a PATCH applies to,
 * annotation-lifecycle.md "Merge writes (PATCH)"): trimmed strings or null, IsDisabled true or null, the dates or null,
 * the labels and the values; UnitType and UnitDefinition for units only.
 */
export interface EditableAnnotation {
  IsDisabled: true | null
  ValidFrom: string | null
  ExpiresAt: string | null
  TimeZone: string | null
  Title: string | null
  Description: string | null
  DocumentationLink: string | null
  Labels: string[]
  Values: Record<string, unknown>
  UnitType?: string
  UnitDefinition?: string
}

type Scalar = Exclude<keyof EditableAnnotation, 'Labels' | 'Values'>

const commonScalars: Scalar[] = [
  'IsDisabled',
  'ValidFrom',
  'ExpiresAt',
  'TimeZone',
  'Title',
  'Description',
  'DocumentationLink',
]
const unitScalars: Scalar[] = ['UnitType', 'UnitDefinition']

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

const blank = (value: string | null | undefined) => (value && value.trim() ? value.trim() : null)

/** The editable members of a draft (or of the draft of the loaded annotation), normalized as a full write stored them. */
export function editableOf(draft: AnnotationForm): EditableAnnotation {
  const editable: EditableAnnotation = {
    IsDisabled: draft.IsDisabled ? true : null,
    ValidFrom: draft.ValidFrom || null,
    ExpiresAt: draft.ExpiresAt || null,
    TimeZone: blank(draft.TimeZone),
    Title: blank(draft.Title),
    Description: blank(draft.Description),
    DocumentationLink: blank(draft.DocumentationLink),
    // as the API's document has them, strings only (a stored null is dropped), and trimmed as the form's schema trims
    Labels: (draft.Labels ?? []).filter((label) => typeof label === 'string').map((label) => label.trim()),
    Values: isObject(draft.Values) ? draft.Values : {},
  }
  if (isUnitAnnotation(draft)) {
    editable.UnitType = draft.UnitType?.trim() ?? ''
    editable.UnitDefinition = draft.UnitDefinition?.trim() ?? ''
  }
  return editable
}

/** Deep equality of JSON values; the order of an object's names does not matter. */
export function sameValue(first: unknown, second: unknown): boolean {
  if (first === second) {
    return true
  }
  if (Array.isArray(first) || Array.isArray(second)) {
    return (
      Array.isArray(first) &&
      Array.isArray(second) &&
      first.length === second.length &&
      first.every((item, index) => sameValue(item, second[index]))
    )
  }
  if (isObject(first) && isObject(second)) {
    const names = Object.keys(first)
    return (
      names.length === Object.keys(second).length &&
      names.every((name) => Object.hasOwn(second, name) && sameValue(first[name], second[name]))
    )
  }
  return false
}

/** Two dates are the same when they are the same instant, whatever their offset (the API compares them so). */
function sameScalar(member: Scalar, first: unknown, second: unknown): boolean {
  if ((member === 'ValidFrom' || member === 'ExpiresAt') && typeof first === 'string' && typeof second === 'string') {
    const [a, b] = [Date.parse(first), Date.parse(second)]
    return first === second || (!Number.isNaN(a) && a === b)
  }
  return first === second
}

/** Sets an own member, also one named __proto__ (an assignment would set the prototype instead). */
const setOwn = (target: Record<string, unknown>, name: string, value: unknown) =>
  Object.defineProperty(target, name, { value, enumerable: true, writable: true, configurable: true })

/** A value a merge patch drops: null itself, or a null member of an object (which the merge removes); arrays stay whole. */
const holdsNull = (value: unknown): boolean =>
  value === null || (isObject(value) && Object.values(value).some(holdsNull))

/**
 * The RFC 7396 diff of two value records: a changed or added name with its value, a removed name as null, and an
 * object over an object as its own nested diff, so the server keeps what else changed meanwhile. `expressible` is false
 * when the change sets a JSON null somewhere, which a merge patch cannot say (null removes).
 */
function mergeDiff(
  base: Record<string, unknown>,
  next: Record<string, unknown>,
): { patch: Record<string, unknown>; expressible: boolean } {
  const patch: Record<string, unknown> = {}
  let expressible = true
  for (const [name, value] of Object.entries(next)) {
    const known = Object.hasOwn(base, name)
    const previous = known ? base[name] : undefined
    if (known && sameValue(previous, value)) {
      continue
    }
    if (isObject(previous) && isObject(value)) {
      const nested = mergeDiff(previous, value)
      setOwn(patch, name, nested.patch)
      expressible &&= nested.expressible
    } else {
      setOwn(patch, name, structuredClone(value))
      expressible &&= !holdsNull(value)
    }
  }
  for (const name of Object.keys(base)) {
    if (!Object.hasOwn(next, name)) {
      setOwn(patch, name, null)
    }
  }
  return { patch, expressible }
}

/** A name of Values as a segment of a JSON Pointer (RFC 6901). */
const segment = (name: string) => name.replaceAll('~', '~0').replaceAll('/', '~1')

/**
 * The patch of the members the user changed from `base` (the draft the form started from) to `draft`, or undefined
 * when nothing changed once both are normalized. Members the user did not change are left out, so a concurrent change
 * of them is kept. A JSON Merge Patch (cleared members as null, Labels whole, Values as a recursive diff) unless the
 * change sets a value to JSON null: then a JSON Patch with the same changes (replace per member, Labels whole, add per
 * changed value name, add null and remove per removed name, so a name removed meanwhile does not fail the patch).
 */
export function toAnnotationPatch(draft: AnnotationForm, base: AnnotationForm): AnnotationPatchBody | undefined {
  const next = editableOf(draft)
  const previous = editableOf(base)
  const scalars = [...commonScalars, ...(next.UnitType !== undefined ? unitScalars : [])].filter(
    (member) => !sameScalar(member, previous[member] ?? null, next[member] ?? null),
  )
  const labels = !sameValue(previous.Labels, next.Labels)
  const values = mergeDiff(previous.Values, next.Values)
  const valueNames = Object.keys(values.patch)

  if (!scalars.length && !labels && !valueNames.length) {
    return undefined
  }

  if (values.expressible) {
    const patch: Record<string, unknown> = {}
    for (const member of scalars) {
      patch[member] = next[member] ?? null
    }
    if (labels) {
      patch.Labels = next.Labels.length ? next.Labels : null
    }
    if (valueNames.length) {
      patch.Values = values.patch
    }
    return patch
  }

  const operations: JsonPatchOperation[] = scalars.map((member) => ({
    op: 'replace',
    path: `/${member}`,
    value: next[member] ?? null,
  }))
  if (labels) {
    operations.push({ op: 'replace', path: '/Labels', value: next.Labels })
  }
  for (const name of valueNames) {
    const path = `/Values/${segment(name)}`
    if (Object.hasOwn(next.Values, name)) {
      operations.push({ op: 'add', path, value: structuredClone(next.Values[name]) })
    } else {
      operations.push({ op: 'add', path, value: null }, { op: 'remove', path })
    }
  }
  return operations
}
