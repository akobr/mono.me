import type {
  ConfigurationComposition,
  ConfigurationCompositionOrigin,
  ConfigurationCompositionSourceKind,
  ConfigurationCompositionSourcePresence,
} from '@42for.net/2splatform.sdk'
import { metaOf, type AnnotationTypeCode } from './annotation-key'
import { escapeSegment } from './json-pointer'

// The blame of the effective document (spec 8.3 of the Composition of the Effective Configuration): the JSON text,
// written here line by line so every line knows its value, and the steps of the composition as the view lists them.

/** One line of the JSON text: the RFC 6901 pointer of its value and the step that set it (none for the root). */
export interface CompositionLine {
  text: string
  pointer: string
  step?: number
}

const isContainer = (value: unknown): value is Record<string, unknown> | unknown[] =>
  value !== null && typeof value === 'object'

// What JSON.stringify leaves out of an object, and writes as null in an array.
const isSkipped = (value: unknown) => value === undefined || typeof value === 'function' || typeof value === 'symbol'

/**
 * The lines of `JSON.stringify(content, null, 2)`, the text of `toJson`, with the step of each. A line that opens or
 * closes a container, or holds a scalar, takes the origin of that value; the root braces have none. Steps are looked
 * up by pointer, never by position: `JSON.parse` moves integer-like keys first, so the lines can come in another order
 * than the server's Origins.
 */
export function compositionLines(
  content: unknown,
  origins: readonly ConfigurationCompositionOrigin[] | undefined,
): CompositionLine[] {
  const steps = new Map<string, number>()
  for (const origin of origins ?? []) {
    if (origin.Path !== undefined && origin.Step !== undefined) {
      steps.set(origin.Path, origin.Step)
    }
  }

  const lines: CompositionLine[] = []
  const push = (text: string, pointer: string) =>
    lines.push(pointer ? { text, pointer, step: steps.get(pointer) } : { text, pointer })

  const write = (value: unknown, pointer: string, depth: number, name: string, comma: string) => {
    const indent = '  '.repeat(depth)
    if (!isContainer(value)) {
      push(`${indent}${name}${JSON.stringify(isSkipped(value) ? null : value)}${comma}`, pointer)
      return
    }

    const [open, close] = Array.isArray(value) ? ['[', ']'] : ['{', '}']
    const entries: [string, unknown][] = Array.isArray(value)
      ? value.map((item, index) => [String(index), isSkipped(item) ? null : item])
      : Object.entries(value).filter(([, item]) => !isSkipped(item))
    if (!entries.length) {
      push(`${indent}${name}${open}${close}${comma}`, pointer)
      return
    }

    push(`${indent}${name}${open}`, pointer)
    entries.forEach(([segment, item], index) =>
      write(
        item,
        `${pointer}/${escapeSegment(segment)}`,
        depth + 1,
        Array.isArray(value) ? '' : `${JSON.stringify(segment)}: `,
        index < entries.length - 1 ? ',' : '',
      ),
    )
    push(`${indent}${close}${comma}`, pointer)
  }

  write(content ?? null, '', 0, '', '')
  return lines
}

/** A step of the composition as the view shows it: its source, the node that applied it and the paths it set. */
export interface CompositionStepView {
  order: number
  sourceId: string
  kind: ConfigurationCompositionSourceKind
  /** The type of the configuration's annotation, or the type the template is for. */
  code?: AnnotationTypeCode
  /** The configuration's key; undefined for a template. */
  annotationKey?: string
  presence: ConfigurationCompositionSourcePresence
  /** 0 when Missing. */
  version: number
  /** The key of the node whose calculation applies the source, and its depth (0 for the annotation itself). */
  node: string
  depth: number
  /** The key of the node the result is merged into; undefined for the annotation itself. */
  mergedInto?: string
  /** The node is merged into another ancestor, not into the annotation: "via <key>". */
  via?: string
  /** The annotation's own configuration. */
  own: boolean
  /** An earlier step applied the same source already. */
  again: boolean
  /** The pointers of the values the step set, in the server's order. */
  paths: string[]
}

/** The steps in the order of application, with their sources, nodes and the values they set. */
export function compositionSteps(composition: ConfigurationComposition | undefined): CompositionStepView[] {
  if (!composition) {
    return []
  }

  const sources = new Map((composition.Sources ?? []).map((source) => [source.Id, source]))
  const nodes = composition.Nodes ?? []
  const target = nodes.at(-1)?.Index
  const paths = new Map<number, string[]>()
  for (const origin of composition.Origins ?? []) {
    if (origin.Step !== undefined && origin.Path !== undefined) {
      paths.set(origin.Step, [...(paths.get(origin.Step) ?? []), origin.Path])
    }
  }

  const applied = new Set<string>()
  return (composition.Steps ?? []).map((step) => {
    const sourceId = step.SourceId ?? ''
    const source = sources.get(sourceId)
    const node = nodes.find((entry) => entry.Index === step.Node)
    const parent = nodes.find((entry) => entry.Index === node?.MergedInto)
    const again = applied.has(sourceId)
    applied.add(sourceId)
    const kind = source?.Kind ?? (sourceId.startsWith('template:') ? 'Template' : 'Configuration')
    return {
      order: step.Order ?? 0,
      sourceId,
      kind,
      code: metaOf(source?.AnnotationType)?.code,
      annotationKey:
        kind === 'Configuration' ? (source?.AnnotationKey ?? sourceId.slice('configuration:'.length)) : undefined,
      presence: source?.Presence ?? 'Missing',
      version: source?.Version ?? 0,
      node: node?.AnnotationKey ?? '',
      depth: node?.Depth ?? 0,
      mergedInto: parent?.AnnotationKey,
      via: parent && parent.Index !== target ? parent.AnnotationKey : undefined,
      own: kind === 'Configuration' && node?.Index === target,
      again,
      paths: paths.get(step.Order ?? 0) ?? [],
    }
  })
}

/** "Template · Execution" or the configuration's key. */
export function stepSource(step: Pick<CompositionStepView, 'kind' | 'code' | 'annotationKey'>): string {
  return step.kind === 'Template'
    ? `Template · ${(step.code && metaOf(step.code)?.label) ?? 'unknown type'}`
    : (step.annotationKey ?? '')
}
