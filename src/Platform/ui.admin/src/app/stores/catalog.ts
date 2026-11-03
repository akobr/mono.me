import { computed, ref, shallowRef, triggerRef } from 'vue'
import { defineStore } from 'pinia'
import { clear as clearSnapshots, createStore, del, get, set } from 'idb-keyval'
import {
  getAnnotations,
  getConfigurations,
  getContexts,
  getExecutions,
  getResponsibilities,
  getSubjects,
  getUnits,
  getUnitsOfExecution,
  getUsages,
  isAborted,
  isTimedOut,
  paginate,
  type Annotation,
  type ConfigurationSummary,
} from '@42for.net/2splatform.sdk'
import {
  getAllAncestors,
  getDirectAncestors,
  parseAnnotationKey,
  type ParsedAnnotationKey,
} from '@/utils/annotation-key'
import type { Scope } from './context'
import { onSignOut, useSessionStore } from './session'

export type CatalogStatus = 'idle' | 'loading' | 'ready' | 'error'

type PageFetcher = (
  scope: Scope,
  token: string | undefined,
  signal: AbortSignal,
) => Promise<{ Annotations?: Annotation[]; ContinuationToken?: string }>

/**
 * The API lists only active annotations by default; the catalog shows every status (badges, `is:` filters), so it
 * asks for the disabled, scheduled and expired ones too. Deleted annotations have their own page.
 */
const pageParams = (token: string | undefined) => ({ continuationToken: token, includeInactive: true })

/** The seven per-type list endpoints, paged concurrently (spec section 6), at most four at once. */
const typeFetchers: PageFetcher[] = [
  (s, token, signal) => getResponsibilities(s.org, s.project, s.view, pageParams(token), { signal }),
  (s, token, signal) => getSubjects(s.org, s.project, s.view, pageParams(token), { signal }),
  (s, token, signal) => getUsages(s.org, s.project, s.view, pageParams(token), { signal }),
  (s, token, signal) => getContexts(s.org, s.project, s.view, pageParams(token), { signal }),
  (s, token, signal) => getExecutions(s.org, s.project, s.view, pageParams(token), { signal }),
  (s, token, signal) => getUnits(s.org, s.project, s.view, pageParams(token), { signal }),
  (s, token, signal) => getUnitsOfExecution(s.org, s.project, s.view, pageParams(token), { signal }),
]

const fallbackFetcher: PageFetcher = (s, token, signal) =>
  getAnnotations(s.org, s.project, s.view, pageParams(token), { signal })

/**
 * The type lists paged at once: a browser has six HTTP/1.1 connections per origin, so the catalog leaves some to the
 * page's own requests (the detail, the history, a save) instead of queueing them behind its lists.
 */
const typeListConcurrency = 4

/** How far the current load is; reset when a load starts. */
export interface CatalogProgress {
  /** The type lists of this load that settled, loaded or failed. */
  lists: number
  /** The type lists of a load. */
  total: number
  /** The annotations this load collected so far, shown or not (a reload collects into its own map). */
  annotations: number
  /** True while GetAnnotations runs, after a type list failed. */
  fallback: boolean
  /** True when the load fills its own map while a shown catalog (a snapshot, the previous load) stays until the end. */
  replacing: boolean
}

const initialProgress = (): CatalogProgress => ({
  lists: 0,
  total: typeFetchers.length,
  annotations: 0,
  fallback: false,
  replacing: false,
})

/**
 * The text of a load in progress: "Loaded 1,234 annotations, 3 of 7 types…" on a first load, "Refreshing: …" while a
 * shown catalog is replaced, and "… all types…" while GetAnnotations runs. The count is the load's own.
 */
export function catalogProgressText(progress: CatalogProgress): string {
  const count = new Intl.NumberFormat().format(progress.annotations)
  const types = progress.fallback ? 'all types' : `${progress.lists} of ${progress.total} types`
  return `${progress.replacing ? 'Refreshing:' : 'Loaded'} ${count} annotations, ${types}…`
}

interface Snapshot {
  savedAt: number
  annotations: Annotation[]
}

const snapshotStore = () => createStore('2s-admin', 'catalog')

/** Removes every catalog snapshot; on sign-out, and when the mock API starts a new dataset. */
export async function clearCatalogSnapshots(): Promise<void> {
  try {
    await clearSnapshots(snapshotStore())
  } catch {
    // IndexedDB unavailable
  }
}
const scopeId = (scope: Scope) => `${scope.org}/${scope.project}/${scope.view}`

/**
 * The annotation catalog of the current view, shared by the grid, tree, graph, search, label suggestions and
 * breadcrumbs. 5,000-20,000 annotations are kept in a raw Map (not deeply reactive) that is mutated in place and
 * published through `revision`. A snapshot in IndexedDB (annotations only) is shown at once on the next visit.
 */
export const useCatalogStore = defineStore('catalog', () => {
  const scope = shallowRef<Scope | null>(null)
  const status = ref<CatalogStatus>('idle')
  const error = shallowRef<unknown>()
  const loadedCount = ref(0)
  const loadedAt = ref<number>()
  const fromSnapshot = ref(false)
  const byKey = shallowRef(new Map<string, Annotation>())
  /** Configuration summaries by annotation key (companion spec E6); null while unknown. */
  const configurations = shallowRef<Map<string, ConfigurationSummary> | null>(null)
  /** Why the last list of the summaries failed; cleared by the next load or refresh. */
  const configurationsError = shallowRef<unknown>()
  const progress = ref<CatalogProgress>(initialProgress())
  /** Bumped on every change of byKey; derived indexes depend on it. */
  const revision = ref(0)

  let controller: AbortController | undefined
  /** The refresh of the summaries alone; aborted by the next refresh, by a load and by a reset. */
  let configurationsController: AbortController | undefined
  /** Local changes made while a load collects into its own map (null = removed); replayed before the swap. */
  let pendingChanges: Map<string, Annotation | null> | undefined
  const partsCache = new Map<string, ParsedAnnotationKey | undefined>()
  const ancestorsCache = new Map<string, string[]>()

  function publish(): void {
    revision.value++
    triggerRef(byKey)
  }

  const annotations = computed(() => (revision.value, [...byKey.value.values()]))

  /** Direct descendants per key, derived from the key structure. */
  const childrenByKey = computed(() => {
    void revision.value
    const children = new Map<string, string[]>()
    for (const key of byKey.value.keys()) {
      for (const parent of getDirectAncestors(key)) {
        const list = children.get(parent)
        if (list) {
          list.push(key)
        } else {
          children.set(parent, [key])
        }
      }
    }
    return children
  })

  /** Every label with its count, most used first. */
  const labelCounts = computed(() => {
    void revision.value
    const counts = new Map<string, number>()
    for (const annotation of byKey.value.values()) {
      for (const label of annotation.Labels ?? []) {
        counts.set(label, (counts.get(label) ?? 0) + 1)
      }
    }
    return new Map([...counts].toSorted((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])))
  })

  const configured = computed<ReadonlySet<string> | null>(() =>
    configurations.value
      ? new Set(
          [...configurations.value.values()]
            .filter((summary) => summary.HasContent !== false)
            .map((summary) => summary.AnnotationKey!),
        )
      : null,
  )

  function partsOf(key: string): ParsedAnnotationKey | undefined {
    if (!partsCache.has(key)) {
      partsCache.set(key, parseAnnotationKey(key))
    }
    return partsCache.get(key)
  }

  function ancestorsOf(key: string): string[] {
    let ancestors = ancestorsCache.get(key)
    if (!ancestors) {
      ancestors = getAllAncestors(key)
      ancestorsCache.set(key, ancestors)
    }
    return ancestors
  }

  const snapshotKey = (target: Scope) => `${useSessionStore().user?.id ?? 'anonymous'}:${scopeId(target)}`

  async function readSnapshot(target: Scope): Promise<Snapshot | undefined> {
    try {
      return await get<Snapshot>(snapshotKey(target), snapshotStore())
    } catch {
      return undefined
    }
  }

  async function writeSnapshot(target: Scope): Promise<void> {
    try {
      await set(
        snapshotKey(target),
        { savedAt: Date.now(), annotations: [...byKey.value.values()] } satisfies Snapshot,
        snapshotStore(),
      )
    } catch {
      // IndexedDB unavailable (private mode): the catalog still works without a snapshot.
    }
  }

  async function pageAll(
    fetcher: PageFetcher,
    target: Scope,
    signal: AbortSignal,
    into: Map<string, Annotation>,
  ): Promise<void> {
    for await (const page of paginate((token, pageSignal) => fetcher(target, token, pageSignal ?? signal), signal)) {
      for (const annotation of page.Annotations ?? []) {
        into.set(annotation.AnnotationKey, annotation)
      }
      // A load that was replaced (or stopped) no longer reports its progress.
      if (!signal.aborted) {
        progress.value.annotations = into.size
      }
      if (into === byKey.value) {
        loadedCount.value = into.size
        publish()
      }
    }
  }

  /**
   * Pages the seven type lists into the map, at most four at once, and returns their failures. A list that timed out
   * stops the others: the load ends with that error, and their answers would not be used.
   */
  async function pageTypes(target: Scope, signal: AbortSignal, into: Map<string, Annotation>): Promise<unknown[]> {
    const lists = new AbortController()
    const stop = () => lists.abort(signal.reason)
    // A listener added to an aborted signal never fires: a replaced load must not page with a live signal.
    if (signal.aborted) {
      stop()
    }
    signal.addEventListener('abort', stop, { once: true })
    const failures: unknown[] = []
    let next = 0

    const lane = async () => {
      while (next < typeFetchers.length && !lists.signal.aborted) {
        const fetcher = typeFetchers[next++]!
        try {
          await pageAll(fetcher, target, lists.signal, into)
        } catch (cause) {
          // The lists stopped after a timeout fail as aborted; only the timeout counts.
          if (!lists.signal.aborted || isTimedOut(cause)) {
            failures.push(cause)
          }
          if (isTimedOut(cause)) {
            lists.abort()
          }
        } finally {
          if (!signal.aborted) {
            progress.value.lists++
          }
        }
      }
    }

    try {
      await Promise.all(Array.from({ length: Math.min(typeListConcurrency, typeFetchers.length) }, lane))
    } finally {
      signal.removeEventListener('abort', stop)
    }
    return failures
  }

  /** True while the catalog still shows the scope: a late answer for another view must not be written. */
  const holds = (target: Scope) => !!scope.value && scopeId(scope.value) === scopeId(target)

  async function loadConfigurations(target: Scope, signal: AbortSignal): Promise<void> {
    const summaries = new Map<string, ConfigurationSummary>()
    try {
      for await (const page of paginate(
        (token, pageSignal) =>
          getConfigurations(
            target.org,
            target.project,
            target.view,
            { continuationToken: token },
            { signal: pageSignal ?? signal },
          ),
        signal,
      )) {
        for (const summary of page.Configurations ?? []) {
          if (summary.AnnotationKey) {
            summaries.set(summary.AnnotationKey, summary)
          }
        }
      }
      if (!signal.aborted && holds(target)) {
        configurations.value = summaries
      }
    } catch (cause) {
      // An abort (also between pages, where paginate throws the reason of the signal) is no failure. A failure keeps
      // the summaries known so far: null only when there were none.
      if (!signal.aborted && !isAborted(cause) && holds(target)) {
        configurationsError.value = cause
      }
    }
  }

  /**
   * Loads the catalog of a scope: the snapshot first (if any), then the seven per-type streams, four at once.
   * A type whose endpoint fails is retried through GetAnnotations, which returns every type; not after a timeout,
   * which GetAnnotations would only wait for again. A failed load always ends: `ready` with the error when there is
   * something to show, `error` otherwise.
   */
  async function load(target: Scope, options: { force?: boolean } = {}): Promise<void> {
    if (
      !options.force &&
      scope.value &&
      scopeId(scope.value) === scopeId(target) &&
      status.value !== 'error' &&
      status.value !== 'idle'
    ) {
      return
    }

    controller?.abort()
    controller = new AbortController()
    const { signal } = controller
    configurationsController?.abort()

    const sameScope = scope.value && scopeId(scope.value) === scopeId(target)
    scope.value = target
    status.value = 'loading'
    error.value = undefined
    configurationsError.value = undefined
    progress.value = initialProgress()
    partsCache.clear()
    ancestorsCache.clear()

    if (!sameScope) {
      byKey.value = new Map()
      configurations.value = null
      loadedCount.value = 0
      fromSnapshot.value = false
      publish()

      const snapshot = await readSnapshot(target)
      if (snapshot && !signal.aborted) {
        byKey.value = new Map(snapshot.annotations.map((annotation) => [annotation.AnnotationKey, annotation]))
        loadedCount.value = byKey.value.size
        loadedAt.value = snapshot.savedAt
        fromSnapshot.value = true
        publish()
      }
    }

    // Replaced while it read the snapshot: the state below (the map, the pending changes, the progress) is the next
    // load's now.
    if (signal.aborted) {
      return
    }

    void loadConfigurations(target, signal)

    // A fresh load collects into its own map while a snapshot is shown, then replaces it at once.
    const fresh = fromSnapshot.value || sameScope ? new Map<string, Annotation>() : byKey.value
    const changes = fresh === byKey.value ? undefined : new Map<string, Annotation | null>()
    pendingChanges = changes
    progress.value.replacing = fresh !== byKey.value

    try {
      const failures = await pageTypes(target, signal, fresh)

      if (signal.aborted) {
        return
      }

      const timeout = failures.find((failure) => isTimedOut(failure))
      if (timeout) {
        throw timeout
      }

      if (failures.length > 0) {
        progress.value.fallback = true
        await pageAll(fallbackFetcher, target, signal, fresh)
        progress.value.fallback = false
      }

      if (fresh !== byKey.value) {
        // Edits, creates and deletes made meanwhile win over the pages fetched before them.
        for (const [key, change] of changes ?? []) {
          if (change) {
            fresh.set(key, change)
          } else {
            fresh.delete(key)
          }
        }
        byKey.value = fresh
      }
      loadedCount.value = byKey.value.size
      loadedAt.value = Date.now()
      fromSnapshot.value = false
      status.value = 'ready'
      publish()
      await writeSnapshot(target)
    } catch (cause) {
      if (signal.aborted || isAborted(cause)) {
        return
      }
      error.value = cause
      // The shown catalog (a snapshot or the previous load) stays, but nothing replaces it any more.
      fromSnapshot.value = false
      progress.value.fallback = false
      // A first load pages straight into the shown map: what it collected before it failed misses whole types, and
      // `ready` would tell the breadcrumbs and the composition that the ancestors it lacks do not exist.
      if (fresh === byKey.value) {
        byKey.value = new Map()
        loadedCount.value = 0
        publish()
      }
      status.value = byKey.value.size > 0 ? 'ready' : 'error'
    } finally {
      if (pendingChanges === changes) {
        pendingChanges = undefined
      }
    }
  }

  function upsert(annotation: Annotation): void {
    pendingChanges?.set(annotation.AnnotationKey, annotation)
    byKey.value.set(annotation.AnnotationKey, annotation)
    loadedCount.value = byKey.value.size
    publish()
  }

  function remove(key: string): void {
    pendingChanges?.set(key, null)
    if (byKey.value.delete(key)) {
      loadedCount.value = byKey.value.size
      publish()
    }
  }

  /**
   * Reloads only the configuration summaries (a delete hides configurations, a restore brings them back). The next
   * refresh, a load and a reset abort it, and it writes only while the catalog still holds its scope.
   */
  async function refreshConfigurations(): Promise<void> {
    if (!scope.value) {
      return
    }
    configurationsController?.abort()
    configurationsController = new AbortController()
    configurationsError.value = undefined
    await loadConfigurations(scope.value, configurationsController.signal)
  }

  function setConfiguration(key: string, summary: ConfigurationSummary | undefined): void {
    if (!configurations.value) {
      return
    }
    const next = new Map(configurations.value)
    if (summary) {
      next.set(key, summary)
    } else {
      next.delete(key)
    }
    configurations.value = next
  }

  function reset(): void {
    controller?.abort()
    configurationsController?.abort()
    scope.value = null
    status.value = 'idle'
    error.value = undefined
    progress.value = initialProgress()
    byKey.value = new Map()
    configurations.value = null
    configurationsError.value = undefined
    loadedCount.value = 0
    loadedAt.value = undefined
    fromSnapshot.value = false
    publish()
  }

  async function forgetSnapshot(target: Scope): Promise<void> {
    try {
      await del(snapshotKey(target), snapshotStore())
    } catch {
      // nothing stored
    }
  }

  return {
    scope,
    status,
    error,
    loadedCount,
    loadedAt,
    fromSnapshot,
    progress,
    byKey,
    revision,
    annotations,
    childrenByKey,
    labelCounts,
    configurations,
    configurationsError,
    configured,
    partsOf,
    ancestorsOf,
    load,
    upsert,
    remove,
    setConfiguration,
    refreshConfigurations,
    reset,
    forgetSnapshot,
  }
})

// Sign-out clears the snapshots: they hold annotations of projects the next user may not see.
onSignOut(clearCatalogSnapshots)
