// The annotation key model, mirroring AnnotationKey and AnnotationKeyExtensions of Abstractions.Annotations.
// Keys are dot-separated: the type code, then the names of the ancestors, then the name.
//   rst.{responsibility}                       sbt.{subject}
//   unt.{responsibility}.{unit}                cnt.{subject}.{context}
//   usg.{subject}.{responsibility}             exe.{subject}.{responsibility}.{context}
//   uxe.{subject}.{responsibility}.{context}.{unit}

export type AnnotationType =
  | 'Responsibility'
  | 'Unit'
  | 'Subject'
  | 'Usage'
  | 'Context'
  | 'Execution'
  | 'UnitOfExecution'

export type AnnotationTypeCode = 'rst' | 'unt' | 'sbt' | 'usg' | 'cnt' | 'exe' | 'uxe'

export interface AnnotationTypeInfo {
  type: AnnotationType
  code: AnnotationTypeCode
  /** Number of key segments including the type code. */
  segments: number
  /** AnnotationType enum value; sorts direct ancestors in configuration merge order. */
  mergeOrder: number
}

export const annotationTypes: readonly AnnotationTypeInfo[] = [
  { type: 'Responsibility', code: 'rst', segments: 2, mergeOrder: 0 },
  { type: 'Unit', code: 'unt', segments: 3, mergeOrder: 1 },
  { type: 'Subject', code: 'sbt', segments: 2, mergeOrder: 2 },
  { type: 'Usage', code: 'usg', segments: 3, mergeOrder: 3 },
  { type: 'Context', code: 'cnt', segments: 3, mergeOrder: 4 },
  { type: 'Execution', code: 'exe', segments: 4, mergeOrder: 5 },
  { type: 'UnitOfExecution', code: 'uxe', segments: 5, mergeOrder: 6 },
]

const byCode = new Map(annotationTypes.map((info) => [info.code, info]))
const byType = new Map(annotationTypes.map((info) => [info.type, info]))

export interface ParsedAnnotationKey {
  key: string
  type: AnnotationType
  code: AnnotationTypeCode
  /** The last segment: the annotation's own name. */
  name: string
  subject?: string
  responsibility?: string
  context?: string
  unit?: string
}

export interface AnnotationKeyParts {
  subject?: string
  responsibility?: string
  context?: string
  unit?: string
  /** The own name of a responsibility, subject, unit or context; the other types derive it from their parts. */
  name?: string
}

export function getAnnotationTypeInfo(typeOrCode: string): AnnotationTypeInfo | undefined {
  return byCode.get(typeOrCode.toLowerCase() as AnnotationTypeCode) ?? byType.get(typeOrCode as AnnotationType)
}

/** Parses a key, or returns undefined when the type code or the number of segments is wrong. */
export function parseAnnotationKey(key: string): ParsedAnnotationKey | undefined {
  const segments = key.split('.')
  const info = byCode.get(segments[0]?.toLowerCase() as AnnotationTypeCode)

  if (!info || segments.length !== info.segments || segments.some((segment) => segment.length === 0)) {
    return undefined
  }

  const parsed: ParsedAnnotationKey = { key, type: info.type, code: info.code, name: segments[segments.length - 1]! }
  const [, first, second, third, fourth] = segments

  switch (info.code) {
    case 'rst':
      return { ...parsed, responsibility: first }
    case 'sbt':
      return { ...parsed, subject: first }
    case 'unt':
      return { ...parsed, responsibility: first, unit: second }
    case 'cnt':
      return { ...parsed, subject: first, context: second }
    case 'usg':
      return { ...parsed, subject: first, responsibility: second }
    case 'exe':
      return { ...parsed, subject: first, responsibility: second, context: third }
    case 'uxe':
      return { ...parsed, subject: first, responsibility: second, context: third, unit: fourth }
  }
}

export function isValidAnnotationKey(key: string): boolean {
  return parseAnnotationKey(key) !== undefined
}

/** A name usable inside a key: not empty, trimmed, and without the '.' separator. */
export function isValidName(name: string): boolean {
  return name.length > 0 && name === name.trim() && !name.includes('.')
}

/** Builds a key from its parts; throws when a part the type needs is missing or not a valid name. */
export function buildAnnotationKey(typeOrCode: AnnotationType | AnnotationTypeCode, parts: AnnotationKeyParts): string {
  const info = getAnnotationTypeInfo(typeOrCode)

  if (!info) {
    throw new Error(`Unknown annotation type '${typeOrCode}'.`)
  }

  const require = (value: string | undefined, part: string): string => {
    if (value === undefined || !isValidName(value)) {
      throw new Error(`A ${info.type} key needs a valid ${part} name.`)
    }

    return value
  }

  switch (info.code) {
    case 'rst':
      return `rst.${require(parts.responsibility ?? parts.name, 'responsibility')}`
    case 'sbt':
      return `sbt.${require(parts.subject ?? parts.name, 'subject')}`
    case 'unt':
      return `unt.${require(parts.responsibility, 'responsibility')}.${require(parts.unit ?? parts.name, 'unit')}`
    case 'cnt':
      return `cnt.${require(parts.subject, 'subject')}.${require(parts.context ?? parts.name, 'context')}`
    case 'usg':
      return `usg.${require(parts.subject, 'subject')}.${require(parts.responsibility, 'responsibility')}`
    case 'exe':
      return `exe.${require(parts.subject, 'subject')}.${require(parts.responsibility, 'responsibility')}.${require(parts.context, 'context')}`
    case 'uxe':
      return `uxe.${require(parts.subject, 'subject')}.${require(parts.responsibility, 'responsibility')}.${require(parts.context, 'context')}.${require(parts.unit, 'unit')}`
  }
}

/** The direct ancestors in configuration merge order (see docs/Platform/Storyteller/inheritance.md). */
export function getDirectAncestors(key: string): string[] {
  const parsed = parseAnnotationKey(key)

  if (!parsed) {
    return []
  }

  const { subject, responsibility, context, unit } = parsed

  switch (parsed.code) {
    case 'rst':
    case 'sbt':
      return []
    case 'unt':
      return [`rst.${responsibility}`]
    case 'cnt':
      return [`sbt.${subject}`]
    case 'usg':
      return [`rst.${responsibility}`, `sbt.${subject}`]
    case 'exe':
      return [`usg.${subject}.${responsibility}`, `cnt.${subject}.${context}`]
    case 'uxe':
      return [`unt.${responsibility}.${unit}`, `exe.${subject}.${responsibility}.${context}`]
  }
}

/** Every ancestor, nearest first in merge order per level, without duplicates. */
export function getAllAncestors(key: string): string[] {
  const result: string[] = []
  const seen = new Set<string>()
  let level = getDirectAncestors(key)

  while (level.length > 0) {
    const next: string[] = []

    for (const ancestor of level) {
      if (!seen.has(ancestor)) {
        seen.add(ancestor)
        result.push(ancestor)
        next.push(...getDirectAncestors(ancestor))
      }
    }

    level = next
  }

  return result
}
