import { reactive } from 'vue'
import {
  annotationTypes,
  getAllAncestors,
  getAnnotationTypeInfo,
  isValidAnnotationKey,
  parseAnnotationKey,
  type AnnotationType,
  type AnnotationTypeCode,
} from '@42for.net/2splatform.sdk'

// The key model lives in the SDK (it mirrors AnnotationKey of Abstractions.Annotations); the UI adds labels,
// icons and color tokens per type (spec section 4).
export {
  annotationTypes,
  buildAnnotationKey,
  getAllAncestors,
  getAnnotationTypeInfo,
  getDirectAncestors,
  isValidAnnotationKey,
  isValidName,
  parseAnnotationKey,
} from '@42for.net/2splatform.sdk'
export type {
  AnnotationKeyParts,
  AnnotationType,
  AnnotationTypeCode,
  AnnotationTypeInfo,
  ParsedAnnotationKey,
} from '@42for.net/2splatform.sdk'

export interface AnnotationTypeMeta {
  type: AnnotationType
  code: AnnotationTypeCode
  label: string
  plural: string
  icon: string
  /** Tailwind text class of the type color token (--st-{code}). */
  textClass: string
  /** Tailwind background class with low opacity, for chips. */
  bgClass: string
  /** Merge order: ancestors are applied in this order. */
  mergeOrder: number
  /** Key parts the type needs, in key order. */
  parts: ('subject' | 'responsibility' | 'context' | 'unit' | 'name')[]
}

/** The built-in metadata; project settings and Preferences may override label, plural and icon (type-appearance.ts). */
export const builtInTypeMeta: Readonly<Record<AnnotationTypeCode, Readonly<AnnotationTypeMeta>>> = {
  rst: {
    type: 'Responsibility',
    code: 'rst',
    label: 'Responsibility',
    plural: 'Responsibilities',
    icon: 'i-lucide-target',
    textClass: 'text-st-rst',
    bgClass: 'bg-st-rst/10',
    mergeOrder: 0,
    parts: ['name'],
  },
  unt: {
    type: 'Unit',
    code: 'unt',
    label: 'Unit',
    plural: 'Units',
    icon: 'i-lucide-box',
    textClass: 'text-st-unt',
    bgClass: 'bg-st-unt/10',
    mergeOrder: 1,
    parts: ['responsibility', 'name'],
  },
  sbt: {
    type: 'Subject',
    code: 'sbt',
    label: 'Subject',
    plural: 'Subjects',
    icon: 'i-lucide-sun',
    textClass: 'text-st-sbt',
    bgClass: 'bg-st-sbt/10',
    mergeOrder: 2,
    parts: ['name'],
  },
  usg: {
    type: 'Usage',
    code: 'usg',
    label: 'Usage',
    plural: 'Usages',
    icon: 'i-lucide-link',
    textClass: 'text-st-usg',
    bgClass: 'bg-st-usg/10',
    mergeOrder: 3,
    parts: ['subject', 'responsibility'],
  },
  cnt: {
    type: 'Context',
    code: 'cnt',
    label: 'Context',
    plural: 'Contexts',
    icon: 'i-lucide-layers',
    textClass: 'text-st-cnt',
    bgClass: 'bg-st-cnt/10',
    mergeOrder: 4,
    parts: ['subject', 'name'],
  },
  exe: {
    type: 'Execution',
    code: 'exe',
    label: 'Execution',
    plural: 'Executions',
    icon: 'i-lucide-play',
    textClass: 'text-st-exe',
    bgClass: 'bg-st-exe/10',
    mergeOrder: 5,
    parts: ['subject', 'responsibility', 'context'],
  },
  uxe: {
    type: 'UnitOfExecution',
    code: 'uxe',
    label: 'Unit of execution',
    plural: 'Units of execution',
    icon: 'i-lucide-cpu',
    textClass: 'text-st-uxe',
    bgClass: 'bg-st-uxe/10',
    mergeOrder: 6,
    parts: ['subject', 'responsibility', 'context', 'unit'],
  },
}

/**
 * The metadata the UI shows: reactive, so templates and computeds that read a label, plural or icon follow the
 * overrides that `applyTypeAppearance` writes. The key model fields never change.
 */
export const typeMeta: Record<AnnotationTypeCode, AnnotationTypeMeta> = reactive(structuredClone(builtInTypeMeta))

export const typeCodes = annotationTypes.map((info) => info.code)

/** Type metadata from a type name ("Execution") or a code ("exe"); undefined for anything else. */
export function metaOf(typeOrCode: string | undefined | null): AnnotationTypeMeta | undefined {
  const info = typeOrCode ? getAnnotationTypeInfo(typeOrCode) : undefined
  return info ? typeMeta[info.code] : undefined
}

/** The type code of a key ("exe.a.b.c" → "exe"). */
export function codeOfKey(key: string): AnnotationTypeCode | undefined {
  return metaOf(key.slice(0, key.indexOf('.')))?.code
}

/**
 * The keys the API deletes together with `key` (CosmosAnnotationService.DeleteAnnotationAsync): every key that has
 * `key` among its ancestors, for all seven types. A unit of execution has none.
 */
export function deletedWith(key: string, keys: Iterable<string>): string[] {
  if (!codeOfKey(key)) {
    return []
  }

  const result: string[] = []
  for (const candidate of keys) {
    if (candidate !== key && getAllAncestors(candidate).includes(key)) {
      result.push(candidate)
    }
  }
  return result
}

/** The pairs (ancestor type, descendant type) of AnnotationHierarchy, read from one sample key of every type. */
const descendantTypes = new Set(
  annotationTypes.flatMap(({ code, segments }) => {
    const sample = [code, ...Array.from({ length: segments - 1 }, () => 'x')].join('.')
    return getAllAncestors(sample).map((ancestor) => `${ancestor.slice(0, ancestor.indexOf('.'))}>${code}`)
  }),
)

/**
 * Whether annotations of the descendant type can descend from the ancestor key (AnnotationHierarchy); a
 * descendant-type schema exists only for such a pair (DescendantTypeRules of the service).
 */
export function canDescend(ancestorKey: string, descendantType: string): boolean {
  const ancestorType = ancestorKey.slice(0, ancestorKey.indexOf('.')).toLowerCase()
  return descendantTypes.has(`${ancestorType}>${descendantType.toLowerCase()}`)
}

/** The type codes that can descend from the key, in `typeCodes` order; none for an invalid key or a uxe key. */
export function descendantTypeCodes(ancestorKey: string): AnnotationTypeCode[] {
  return isValidAnnotationKey(ancestorKey) ? typeCodes.filter((code) => canDescend(ancestorKey, code)) : []
}

/** How many descendant keys a Relations row shows before "Show all". */
export const descendantPreviewLimit = 5

export interface DescendantGroup {
  code: AnnotationTypeCode
  total: number
  preview: string[]
  /** `ancestor:<root> type:<code>`, quoted like `setQualifier`. */
  query: string
}

export interface GroupedDescendants {
  groups: DescendantGroup[]
  /** Every descendant, across groups. */
  total: number
  /** `ancestor:<root>` for the whole set. */
  query: string
}

/** Quotes a filter value that contains a space or a comma, the same rule as `setQualifier`. */
function quoteFilterValue(value: string): string {
  return /[\s,]/.test(value) ? `"${value}"` : value
}

/**
 * The Annotations-view filter for the descendants of `rootKey`, optionally one type.
 * Kept here so `annotation-key` does not import `filter-parser` (that module already imports this one).
 */
export function descendantFilterQuery(rootKey: string, code?: AnnotationTypeCode): string {
  const ancestor = `ancestor:${quoteFilterValue(rootKey)}`
  return code ? `${ancestor} type:${quoteFilterValue(code)}` : ancestor
}

/**
 * Transitive descendants of `rootKey` grouped by type, from catalog keys and the same ancestry test as `ancestor:`.
 * A key is counted once. Groups follow `descendantTypeCodes`. An invalid root or a unit of execution has none.
 */
export function groupedDescendants(
  rootKey: string,
  keys: Iterable<string>,
  ancestorsOf: (key: string) => readonly string[],
  limit = descendantPreviewLimit,
): GroupedDescendants {
  const query = descendantFilterQuery(rootKey)
  const applicable = descendantTypeCodes(rootKey)
  if (applicable.length === 0) {
    return { groups: [], total: 0, query }
  }

  const wanted = new Set(applicable)
  const needle = rootKey.toLowerCase()
  const members = new Map<AnnotationTypeCode, string[]>(applicable.map((code) => [code, []]))
  const seen = new Set<string>()

  for (const key of keys) {
    if (seen.has(key)) {
      continue
    }
    seen.add(key)

    const code = parseAnnotationKey(key)?.code
    const bucket = code ? members.get(code) : undefined
    if (!code || !wanted.has(code) || !bucket) {
      continue
    }
    if (!ancestorsOf(key).some((ancestor) => ancestor.toLowerCase() === needle)) {
      continue
    }
    bucket.push(key)
  }

  const groups: DescendantGroup[] = []
  let total = 0
  for (const code of applicable) {
    const found = members.get(code) ?? []
    if (found.length === 0) {
      continue
    }
    found.sort((left, right) => left.localeCompare(right, 'en'))
    total += found.length
    groups.push({
      code,
      total: found.length,
      preview: found.slice(0, Math.max(0, limit)),
      query: descendantFilterQuery(rootKey, code),
    })
  }

  return { groups, total, query }
}

/** The API's message for a pair that cannot descend (DescendantTypeRules.GetCannotDescendMessage). */
export function cannotDescendMessage(ancestorKey: string, descendantType: string): string {
  const type = descendantType.toLowerCase()
  return `The type ${type} cannot descend from ${ancestorKey}, so it has no descendant-type schema there.`
}
