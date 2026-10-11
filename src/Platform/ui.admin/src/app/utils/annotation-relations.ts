import type { Annotation } from '@42for.net/2splatform.sdk'
import { parseAnnotationKey } from './annotation-key'

/**
 * The per-type relation members the API requires in a SetAnnotation body (SubjectKey, ContextName, UnitKey, …),
 * derived from the key the same way as CreateAnnotationsFromStringAsync. A unit also gets the API's defaults for
 * UnitType and UnitDefinition when the body has none. Members already present are kept.
 */
export function withRelations(annotation: Annotation): Annotation {
  const parsed = parseAnnotationKey(annotation.AnnotationKey)
  if (!parsed) {
    return annotation
  }

  const { subject, responsibility, context, unit } = parsed
  const relations: Record<string, unknown> = {}

  if (subject && parsed.code !== 'sbt') {
    Object.assign(relations, { SubjectKey: `sbt.${subject}`, SubjectName: subject })
  }
  if (responsibility && parsed.code !== 'rst') {
    Object.assign(relations, { ResponsibilityKey: `rst.${responsibility}`, ResponsibilityName: responsibility })
  }
  if (context && parsed.code !== 'cnt') {
    Object.assign(relations, { ContextKey: `cnt.${subject}.${context}`, ContextName: context })
  }
  if (parsed.code === 'exe' || parsed.code === 'uxe') {
    relations.UsageKey = `usg.${subject}.${responsibility}`
  }
  if (parsed.code === 'uxe') {
    Object.assign(relations, {
      UnitKey: `unt.${responsibility}.${unit}`,
      UnitName: unit,
      ExecutionKey: `exe.${subject}.${responsibility}.${context}`,
    })
  }
  if (parsed.code === 'unt') {
    const members = annotation as { UnitType?: string; UnitDefinition?: string }
    Object.assign(relations, { UnitType: members.UnitType ?? 'event', UnitDefinition: members.UnitDefinition ?? 'unknown' })
  }

  return { ...annotation, ...relations }
}
