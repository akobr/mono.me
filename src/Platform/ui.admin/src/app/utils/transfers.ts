import type {
  ViewArchiveContent,
  ViewExport,
  ViewImport,
  ViewImportItem,
  ViewImportItemKind,
  ViewImportItemOutcome,
} from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'

/** The kinds of documents an archive holds, in import order (export-import.md). */
export const archiveContents: readonly ViewArchiveContent[] = ['Annotations', 'Schemas', 'Templates', 'Configurations']

/** The kind checkboxes of the Export & Import page: the same four, with what each kind holds. */
export const contentItems: { label: ViewArchiveContent; value: ViewArchiveContent; description: string }[] = [
  { label: 'Annotations', value: 'Annotations', description: 'Titles, labels, values and validity of the keys.' },
  { label: 'Schemas', value: 'Schemas', description: 'Type, annotation and descendant-type schemas.' },
  { label: 'Templates', value: 'Templates', description: 'The defaults per annotation type.' },
  { label: 'Configurations', value: 'Configurations', description: 'The own documents of the annotations.' },
]

/** What an export never holds (spec 4.2). */
export const neverExported =
  'Never included: secrets, effective documents, the audit, the version history of annotations, and deleted annotations.'

export interface DocumentKindMeta {
  label: string
  icon: string
  content: ViewArchiveContent
}

/** One document of an archive: its name in the result, the icon of its section, and the kind it is included by. */
export const documentKinds: Record<ViewImportItemKind, DocumentKindMeta> = {
  Annotation: { label: 'Annotation', icon: 'i-lucide-tags', content: 'Annotations' },
  TypeSchema: { label: 'Type schema', icon: 'i-lucide-braces', content: 'Schemas' },
  DescendantTypeSchema: { label: 'Descendant-type schema', icon: 'i-lucide-braces', content: 'Schemas' },
  AnnotationSchema: { label: 'Annotation schema', icon: 'i-lucide-braces', content: 'Schemas' },
  Template: { label: 'Template', icon: 'i-lucide-layout-template', content: 'Templates' },
  Configuration: { label: 'Configuration', icon: 'i-lucide-settings-2', content: 'Configurations' },
}

/** The kind a document is included by; an item without a kind counts as a configuration, as before PR #88's kinds. */
export const contentOf = (kind: ViewImportItemKind | undefined): ViewArchiveContent =>
  documentKinds[kind ?? 'Configuration'].content

export type OutcomeColor = 'success' | 'info' | 'neutral' | 'warning' | 'error'

/** The outcomes of an import, with the words of a dry run, which writes nothing. */
export const outcomes: Record<ViewImportItemOutcome, { color: OutcomeColor; label: string; dryRun: string }> = {
  Created: { color: 'success', label: 'Created', dryRun: 'To create' },
  Updated: { color: 'info', label: 'Updated', dryRun: 'To update' },
  Unchanged: { color: 'neutral', label: 'Unchanged', dryRun: 'Unchanged' },
  Skipped: { color: 'warning', label: 'Skipped', dryRun: 'To skip' },
  Failed: { color: 'error', label: 'Failed', dryRun: 'Fails' },
}

export const outcomeLabel = (outcome: ViewImportItemOutcome | undefined, isDryRun: boolean | undefined) =>
  outcome ? (isDryRun ? outcomes[outcome].dryRun : outcomes[outcome].label) : ''

export function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`
}

/** "a", "a and b", "a, b and c". */
export function joinList(parts: readonly string[]): string {
  return parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}` : (parts[0] ?? '')
}

/** "2 created, 1 updated, 5 unchanged, 1 failed": the counts that are not zero, unchanged always. */
export function importCounts(state: ViewImport): string {
  const summary = state.Summary ?? {}
  const parts = [
    summary.Created ? `${summary.Created} ${state.IsDryRun ? 'to create' : 'created'}` : '',
    summary.Updated ? `${summary.Updated} ${state.IsDryRun ? 'to update' : 'updated'}` : '',
    `${summary.Unchanged ?? 0} unchanged`,
    summary.Skipped ? `${summary.Skipped} skipped` : '',
    summary.Failed ? `${summary.Failed} failed` : '',
  ]

  return parts.filter(Boolean).join(', ')
}

/** "12 annotations and 2 templates": the counts of the included kinds; the API leaves out those of the others. */
export function exportCounts(state: ViewExport): string {
  const counts: [number | null | undefined, string][] = [
    [state.AnnotationCount, 'annotation'],
    [state.SchemaCount, 'schema'],
    [state.TemplateCount, 'template'],
    [state.ConfigurationCount, 'configuration'],
  ]

  return joinList(counts.filter(([count]) => count != null).map(([count, noun]) => plural(count!, noun)))
}

/** The document of an item as the result names it: the key, the type code, or both for a descendant-type schema. */
export function documentName(item: ViewImportItem): string {
  switch (item.Kind) {
    case 'Template':
    case 'TypeSchema':
      return item.AnnotationType ?? item.Path ?? ''
    case 'DescendantTypeSchema':
      return item.AnnotationKey && item.AnnotationType
        ? `${item.AnnotationType} below ${item.AnnotationKey}`
        : (item.Path ?? '')
    default:
      return item.AnnotationKey ?? item.Path ?? ''
  }
}

/** The page of the document of an item (spec 4.4), or nothing when the item does not name it. */
export function documentPath(scope: Scope, item: ViewImportItem): string | undefined {
  const base = `/${scope.org}/${scope.project}/${scope.view}`
  const { AnnotationKey: key, AnnotationType: type } = item

  switch (item.Kind) {
    case 'Annotation':
      return key ? `${base}/annotations/${key}` : undefined
    case 'Template':
      return type ? `${base}/templates/${type}` : undefined
    case 'TypeSchema':
      return type ? `${base}/schemas/type/${type}` : undefined
    case 'AnnotationSchema':
      return key ? `${base}/schemas/annotation/${key}` : undefined
    case 'DescendantTypeSchema':
      return key && type ? `${base}/schemas/annotation/${key}/type/${type}` : undefined
    default:
      return key ? `${base}/configurations/${key}` : undefined
  }
}

/**
 * Whether the view has the document of an item, so the result links it: written by a real run, kept by Skip, or
 * about to be updated by a dry run. A document to create, and one that failed or was skipped for another reason, may
 * not exist.
 */
export function documentExists(state: ViewImport, item: ViewImportItem): boolean {
  switch (item.Outcome) {
    case 'Created':
      return !state.IsDryRun
    case 'Updated':
      return true
    case 'Skipped':
      return item.ErrorCode === 'DocumentExists'
    default:
      return false
  }
}

const isWritten = (item: ViewImportItem) => item.Outcome === 'Created' || item.Outcome === 'Updated'

/** The kinds a real import wrote documents of: from the summaries per kind, and from the items. */
export function writtenContents(state: ViewImport): Set<ViewArchiveContent> {
  const written = new Set<ViewArchiveContent>()
  if (state.IsDryRun) {
    return written
  }

  for (const summary of state.Summaries ?? []) {
    if (summary.Content && (summary.Created ?? 0) + (summary.Updated ?? 0) > 0) {
      written.add(summary.Content)
    }
  }

  for (const item of state.Items ?? []) {
    if (isWritten(item)) {
      written.add(contentOf(item.Kind))
    }
  }

  return written
}

/** The keys of the configurations a real import wrote, as far as its items list them. */
export function writtenConfigurations(state: ViewImport): string[] {
  return state.IsDryRun
    ? []
    : (state.Items ?? [])
        .filter((item) => isWritten(item) && contentOf(item.Kind) === 'Configurations' && !!item.AnnotationKey)
        .map((item) => item.AnnotationKey!)
}

/** Whether the import re-created deleted annotations, which empties their configurations. */
export const recreatedAnnotations = (state: ViewImport) =>
  !state.IsDryRun &&
  (state.Items ?? []).some(
    (item) => item.Kind === 'Annotation' && item.Warnings?.some((warning) => warning.Code === 'AnnotationRecreated'),
  )

/** The page of the Export & Import of a view. */
export const exportImportPath = (scope: Scope) => `/${scope.org}/${scope.project}/${scope.view}/export-import`
