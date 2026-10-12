import type { ConfigurationSummary } from '@42for.net/2splatform.sdk'
import { codeOfKey, getAllAncestors, typeMeta, type AnnotationTypeCode } from './annotation-key'

/**
 * Where the configuration of an annotation comes from (spec 4.2): its own stored content, only inheritance (from
 * ancestors with content and from type templates), nothing, or not known yet (summaries still loading).
 */
export type ConfigurationOrigin =
  | { kind: 'own'; version: number; inheritsFrom: string[]; templates: AnnotationTypeCode[] }
  | { kind: 'inherited'; inheritsFrom: string[]; templates: AnnotationTypeCode[] }
  | { kind: 'none' }
  | { kind: 'unknown' }

const mergeOrder = (key: string) => typeMeta[codeOfKey(key) ?? 'rst'].mergeOrder

export function configurationOrigin(
  key: string,
  configurations: ReadonlyMap<string, ConfigurationSummary> | null,
  templateTypes: ReadonlySet<AnnotationTypeCode> | undefined,
): ConfigurationOrigin {
  if (!configurations) {
    return { kind: 'unknown' }
  }

  const hasContent = (candidate: string) => configurations.get(candidate)?.HasContent === true
  const ancestors = getAllAncestors(key).toSorted((a, b) => mergeOrder(a) - mergeOrder(b) || a.localeCompare(b))
  const inheritsFrom = ancestors.filter(hasContent)

  // A template merges into every annotation of its type, and through the calculated ancestors into their descendants.
  const templates = [...new Set([...ancestors, key].map((candidate) => codeOfKey(candidate)!))]
    .filter((code) => templateTypes?.has(code))
    .toSorted((a, b) => typeMeta[a].mergeOrder - typeMeta[b].mergeOrder)

  const own = configurations.get(key)
  if (own?.HasContent) {
    return { kind: 'own', version: own.Version ?? 0, inheritsFrom, templates }
  }

  return inheritsFrom.length || templates.length ? { kind: 'inherited', inheritsFrom, templates } : { kind: 'none' }
}

/** "rst.invoicing and the Usage template", for the tooltips of the configuration link. */
export function describeInheritance(origin: Extract<ConfigurationOrigin, { inheritsFrom: string[] }>): string {
  const parts = [...origin.inheritsFrom, ...origin.templates.map((code) => `the ${typeMeta[code].label} template`)]
  if (parts.length <= 1) {
    return parts.join('')
  }
  return `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}`
}
