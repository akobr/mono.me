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

/** The seven per-type list endpoints, paged concurrently (spec section 6). */
const typeFetchers: PageFetcher[] = [
  (s, token, signal) => getResponsibilities(s.org, s.project, s.view, { continuationToken: token }, { signal }),
  (s, token, signal) => getSubjects(s.org, s.project, s.view, { continuationToken: token }, { signal }),
  (s, token, signal) => getUsages(s.org, s.project, s.view, { continuationToken: token }, { signal }),
  (s, token, signal) => getContexts(s.org, s.project, s.view, { continuationToken: token }, { signal }),
  (s, token, signal) => getExecutions(s.org, s.project, s.view, { continuationToken: token }, { signal }),
  (s, token, signal) => getUnits(s.org, s.project, s.view, { continuationToken: token }, { signal }),
  (s, token, signal) => getUnitsOfExecution(s.org, s.project, s.view, { continuationToken: token }, { signal }),
]

const fallbackFetcher: PageFetcher = (s, token, signal) =>
  getAnnotations(s.org, s.project, s.view, { continuationToken: token }, { signal })

interface Snapshot {
  savedAt: number
  annotations: Annotation[]
}

const snapshotStore = () => createStore('2s-admin', 'catalog')
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
  /** Bumped on every change of byKey; derived indexes depend on it. */
  const revision = ref(0)

  let controller: AbortController | undefined
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
      if (into === byKey.value) {
        loadedCount.value = into.size
        publish()
      }
    }
  }

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
      configurations.value = summaries
    } catch (cause) {
      if (!isAborted(cause)) {
        configurations.value = null
      }
    }
  }

  /**
   * Loads the catalog of a scope: the snapshot first (if any), then the seven per-type streams in parallel.
   * A type whose endpoint fails is retried through GetAnnotations, which returns every type.
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

    const sameScope = scope.value && scopeId(scope.value) === scopeId(target)
    scope.value = target
    status.value = 'loading'
    error.value = undefined
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

    void loadConfigurations(target, signal)

    // A fresh load collects into its own map while a snapshot is shown, then replaces it at once.
    const fresh = fromSnapshot.value || sameScope ? new Map<string, Annotation>() : byKey.value

    try {
      const results = await Promise.allSettled(typeFetchers.map((fetcher) => pageAll(fetcher, target, signal, fresh)))

      if (signal.aborted) {
        return
      }

      if (results.some((result) => result.status === 'rejected')) {
        await pageAll(fallbackFetcher, target, signal, fresh)
      }

      if (fresh !== byKey.value) {
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
      status.value = byKey.value.size > 0 ? 'ready' : 'error'
    }
  }

  function upsert(annotation: Annotation): void {
    byKey.value.set(annotation.AnnotationKey, annotation)
    loadedCount.value = byKey.value.size
    publish()
  }

  function remove(key: string): void {
    if (byKey.value.delete(key)) {
      loadedCount.value = byKey.value.size
      publish()
    }
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
    scope.value = null
    status.value = 'idle'
    byKey.value = new Map()
    configurations.value = null
    loadedCount.value = 0
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
    byKey,
    revision,
    annotations,
    childrenByKey,
    labelCounts,
    configurations,
    configured,
    partsOf,
    ancestorsOf,
    load,
    upsert,
    remove,
    setConfiguration,
    reset,
    forgetSnapshot,
  }
})

// Sign-out clears the snapshots: they hold annotations of projects the next user may not see.
onSignOut(async () => {
  try {
    await clearSnapshots(snapshotStore())
  } catch {
    // IndexedDB unavailable
  }
})
