import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue'
import { useQueries, type QueryFunctionContext, type QueryObserverResult } from '@tanstack/vue-query'
import type { Scope } from '@/stores/context'
import { createLimiter } from '@/utils/limiter'
import { compareTexts, type CompareFilter, type CompareResult } from '@/utils/view-compare'
import { annotationStamp } from '@/utils/view-copy'
import {
  annotationDocument,
  annotationText,
  canonicalJson,
  hasInexactNumber,
  readAnnotation,
  readConfiguration,
  type ConfigurationContent,
  type ViewItemKind,
} from '@/utils/view-documents'

/** One key of a comparison of two views. */
export interface CompareFile {
  key: string
  loading: boolean
  error?: unknown
  /** The result of the shown documents: the annotations, or the own or the effective configurations. */
  shown?: CompareResult
  /** What a copy would move: the annotations, or the own configurations whatever is shown. */
  own?: CompareResult
  /** Own configurations are shown, and the effective ones differ although the own ones do not. */
  effectiveDiffers: boolean
  /** The key is deleted in the target. */
  deleted: boolean
  /** A side holds a number beyond 2^53: the file is never identical. */
  inexact: boolean
  /** What a side lacks, for example "The annotation is not in staging". */
  notes: string[]
  /** What the target held when it was read: the stamp of the annotation or the version of the configuration. */
  expected: string | number | null
  retry: () => void
}

export interface ViewCompareOptions {
  /** The view of the route, where the keys were selected. */
  source: MaybeRefOrGetter<Scope | undefined>
  /** The other view of the project; nothing is read without one, or when it names the source. */
  target: MaybeRefOrGetter<string | undefined>
  kind: MaybeRefOrGetter<ViewItemKind>
  /** Distinct keys, in the order of the files. */
  keys: MaybeRefOrGetter<readonly string[]>
  content: MaybeRefOrGetter<ConfigurationContent>
  /** False holds every read back, for example until a navigation has landed. */
  enabled?: MaybeRefOrGetter<boolean>
}

/** The reads that run at once: as many as the lists the catalog loads at once. */
const concurrency = 4

const inheritedNote =
  'The own configurations are the same. The difference is inherited (ancestors, the template, key values), and a copy does not change it.'
const inexactNote = 'It holds a number beyond 2^53, which the browser cannot compare exactly.'

/** What a view holds for a key, as its query keeps it: the texts that are compared, built once per read. */
interface ViewRead {
  /** The annotation of the key is in the view and is not deleted; only then its configuration can be read. */
  present: boolean
  /** The key is deleted in the view. An annotation tells it only when it is read with its deleted state. */
  deleted: boolean
  /** The text of the annotation, or of the own configuration; undefined for none. */
  own?: string
  /** The text of the effective configuration; undefined when it cannot be read. */
  effective?: string
  /** `own` holds a number beyond 2^53. */
  inexact: boolean
  /** `effective` holds one. */
  effectiveInexact: boolean
  /** The stamp of the annotation, or the version of the configuration. */
  expected: string | number | null
}

type ReadResult = QueryObserverResult<ViewRead>

interface Plan {
  source: Scope
  target: Scope
  kind: ViewItemKind
  keys: readonly string[]
}

/** A comparison of two texts, kept for as long as the texts stay the same. */
interface Compared {
  source?: string
  target?: string
  inexact: boolean
  result: CompareResult
}

/** What is kept per key between two notifications of the queries. */
interface Kept {
  own?: Compared
  effective?: Compared
  file?: CompareFile
  retry: () => void
}

async function annotationRead(
  scope: Scope,
  key: string,
  includeDeleted: boolean,
  signal: AbortSignal,
): Promise<ViewRead> {
  const { annotation, deleted } = await readAnnotation(scope, key, { signal, includeDeleted })
  return {
    present: !!annotation,
    deleted,
    own: annotation && annotationText(annotation),
    inexact: !!annotation && hasInexactNumber(annotationDocument(annotation)),
    effectiveInexact: false,
    expected: annotationStamp(annotation),
  }
}

async function configurationRead(scope: Scope, key: string, signal: AbortSignal): Promise<ViewRead> {
  const side = await readConfiguration(scope, key, { signal })
  return {
    present: side.readable,
    deleted: side.deleted,
    own: side.own && canonicalJson(side.own),
    effective: side.readable ? canonicalJson(side.effective) : undefined,
    inexact: hasInexactNumber(side.own),
    effectiveInexact: hasInexactNumber(side.effective),
    expected: side.version,
  }
}

/** `compareTexts`, computed again only for other texts. A side with an inexact number is never identical. */
function compared(kept: Compared | undefined, source: ViewRead, target: ViewRead, effective: boolean): Compared {
  const texts = effective ? [source.effective, target.effective] : [source.own, target.own]
  const inexact = effective ? source.effectiveInexact || target.effectiveInexact : source.inexact || target.inexact
  if (kept && kept.source === texts[0] && kept.target === texts[1] && kept.inexact === inexact) {
    return kept
  }
  const result = compareTexts(texts[0], texts[1])
  return {
    source: texts[0],
    target: texts[1],
    inexact,
    result: inexact && result.status === 'identical' ? { ...result, status: 'different' } : result,
  }
}

/** Whether two files of a key say the same, so the earlier one can stay and its section is not rendered again. */
function sameFile(a: CompareFile, b: CompareFile): boolean {
  return (
    a.loading === b.loading &&
    a.error === b.error &&
    a.shown === b.shown &&
    a.own === b.own &&
    a.effectiveDiffers === b.effectiveDiffers &&
    a.deleted === b.deleted &&
    a.inexact === b.inexact &&
    a.expected === b.expected &&
    a.notes.length === b.notes.length &&
    a.notes.every((note, index) => note === b.notes[index])
  )
}

/**
 * The chips of the summary that a file counts for: a failed file only Failed, a key that is deleted in the target
 * only Deleted, any other file its shown result, and Effective differs beside it. A loading file counts for none.
 */
export function filtersOf(file: CompareFile): CompareFilter[] {
  if (file.error !== undefined) {
    return ['failed']
  }
  if (file.deleted) {
    return ['deleted']
  }
  if (!file.shown) {
    return []
  }
  return file.effectiveDiffers ? [file.shown.status, 'effectiveDiffers'] : [file.shown.status]
}

/** The one state that the badge of a file shows, by the precedence of `filtersOf`. */
export function stateOf(file: CompareFile): CompareFilter | 'loading' {
  if (file.error !== undefined) {
    return 'failed'
  }
  if (!file.shown) {
    return 'loading'
  }
  return file.deleted ? 'deleted' : file.shown.status
}

/**
 * Both sides of a comparison of a selection across two views: one file per key (spec "Copy and Compare Annotations
 * and Configurations Between Views", section 4). Every key is read once per view, at most four reads at a time. A
 * configuration is read once for its own and its effective document, so a change of `content` reads nothing. There
 * are no files, and no reads, without a source and another view as the target, or while `enabled` is false.
 */
export function useViewCompare(options: ViewCompareOptions): {
  files: ComputedRef<CompareFile[]>
  loaded: ComputedRef<number>
} {
  // The timer of a read starts when the read is called, so a hundred keys must not be sent at once.
  const limit = createLimiter(concurrency)

  const plan = computed<Plan | undefined>(() => {
    const source = toValue(options.source)
    const target = toValue(options.target)
    if (!toValue(options.enabled ?? true) || !source || !target || target === source.view) {
      return undefined
    }
    return { source, target: { ...source, view: target }, kind: toValue(options.kind), keys: toValue(options.keys) }
  })

  /**
   * The read of a key in a view. Its key starts with the path segments of the request, so `invalidateAnnotation`,
   * `invalidateConfiguration` and `invalidateAllConfigurations` of the view refresh it after a save. Only the
   * annotation of the target is read with its deleted state, which tells a deleted key from one that was never
   * there; that read has a key of its own, since a view is the target of one comparison and the source of the next.
   */
  function read(scope: Scope, kind: ViewItemKind, key: string, includeDeleted: boolean) {
    return {
      queryKey: [
        'v1',
        scope.org,
        scope.project,
        scope.view,
        kind === 'annotations' ? 'annotations' : 'configuration',
        key,
        'view-compare',
        ...(includeDeleted ? [{ includeDeleted: true }] : []),
      ],
      queryFn: ({ signal }: QueryFunctionContext) =>
        limit(
          () =>
            kind === 'annotations'
              ? annotationRead(scope, key, includeDeleted, signal)
              : configurationRead(scope, key, signal),
          signal,
        ),
      // A failed document shows its error with Retry in its own section, not a toast per document.
      meta: { silent: true },
      // A comparison reads both views whenever it is opened: a key that was restored, deleted or changed in a view
      // since the last comparison must not show what that one read.
      staleTime: 0,
    }
  }

  // Two results per key, in the order of the keys: the source, then the target.
  const results = useQueries({
    queries: () => {
      const running = plan.value
      return running
        ? running.keys.flatMap((key) => [
            read(running.source, running.kind, key, false),
            read(running.target, running.kind, key, running.kind === 'annotations'),
          ])
        : []
    },
    shallow: true,
  })

  function retry(key: string): void {
    const index = plan.value?.keys.indexOf(key) ?? -1
    const sides: readonly ReadResult[] = index < 0 ? [] : results.value.slice(2 * index, 2 * index + 2)
    for (const side of sides) {
      if (side.isError) {
        void side.refetch()
      }
    }
  }

  function fileOf(
    key: string,
    source: ReadResult | undefined,
    target: ReadResult | undefined,
    running: Plan,
    content: ConfigurationContent,
    kept: Kept,
  ): CompareFile {
    const file: CompareFile = {
      key,
      loading: false,
      effectiveDiffers: false,
      deleted: false,
      inexact: false,
      notes: [],
      expected: null,
      retry: kept.retry,
    }

    const failed = [target, source].find((side) => side?.isError)
    if (failed) {
      return { ...file, error: failed.error }
    }
    if (!source?.data || !target?.data) {
      return { ...file, loading: true }
    }

    kept.own = compared(kept.own, source.data, target.data, false)
    const own = kept.own.result
    if (running.kind === 'annotations') {
      return {
        ...file,
        shown: own,
        own,
        deleted: target.data.deleted,
        inexact: kept.own.inexact,
        notes: kept.own.inexact ? [inexactNote] : [],
        expected: target.data.expected,
      }
    }

    kept.effective = compared(kept.effective, source.data, target.data, true)
    const effective = kept.effective.result
    const showsEffective = content === 'effective'
    // The effective documents differ, and the own ones are not the reason.
    const inherited = effective.status === 'different' && (own.status === 'identical' || own.status === 'absent')
    const inexact = kept.own.inexact || (showsEffective && kept.effective.inexact)

    const notes: string[] = []
    for (const [scope, side] of [
      [running.target, target.data],
      [running.source, source.data],
    ] as const) {
      // A key that is deleted in a view is another matter: the target says it with the state of the file.
      if (!side.present && !side.deleted) {
        notes.push(`The annotation is not in ${scope.view}`)
      }
    }
    if (showsEffective && inherited) {
      notes.push(inheritedNote)
    }
    if (inexact) {
      notes.push(inexactNote)
    }

    return {
      ...file,
      shown: showsEffective ? effective : own,
      own,
      effectiveDiffers: !showsEffective && inherited,
      deleted: target.data.deleted,
      inexact,
      notes,
      expected: target.data.expected,
    }
  }

  // The results of every query are new objects on every notification of any of them, a few hundred times while a
  // hundred keys load. What is kept per key leaves the comparison of its texts, and its file, as they were.
  let keptByKey = new Map<string, Kept>()

  const files = computed<CompareFile[]>(() => {
    const running = plan.value
    if (!running) {
      return []
    }
    const content = toValue(options.content)
    const sides: readonly ReadResult[] = results.value
    const next = new Map<string, Kept>()
    const list = running.keys.map((key, index) => {
      const kept = keptByKey.get(key) ?? { retry: () => retry(key) }
      next.set(key, kept)
      const file = fileOf(key, sides[2 * index], sides[2 * index + 1], running, content, kept)
      kept.file = kept.file && sameFile(kept.file, file) ? kept.file : file
      return kept.file
    })
    keptByKey = next
    return list
  })

  /** The files whose both sides were read, or failed. */
  const loaded = computed(() => files.value.filter((file) => !file.loading).length)

  return { files, loaded }
}
