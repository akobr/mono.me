import {
  annotationTypes,
  getAllAncestors,
  getAnnotationTypeInfo,
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

export const typeMeta: Record<AnnotationTypeCode, AnnotationTypeMeta> = {
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
