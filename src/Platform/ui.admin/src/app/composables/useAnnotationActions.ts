import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  deleteAnnotation,
  isNotFound,
  purgeAnnotation,
  restoreAnnotations,
  setAnnotation,
  setAnnotationsSimple,
  type Annotation,
  type AnnotationDeletionResult,
  type AnnotationKeyFailure,
} from '@42for.net/2splatform.sdk'
import { forgetAnnotation, invalidateAnnotation, invalidateDeleted } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { getAllAncestors, getAnnotationTypeInfo } from '@/utils/annotation-key'
import { sameView } from '@/utils/context-paths'
import { normalizeLabels } from '@/utils/labels'

const writeConcurrency = 4
const deleteConcurrency = 4
/** RestoreAnnotations accepts at most this many keys in one request. */
const restoreBatchSize = 100
/** SetAnnotationsSimple accepts at most this many lines in one request (ApiConstants.MaxInputItems). */
const simpleBatchSize = 100

/** Runs `worker` over the items with at most `limit` running at once. */
export async function runLimited<T>(
  items: readonly T[],
  limit: number,
  worker: (item: T) => Promise<void>,
): Promise<void> {
  let next = 0
  const lanes = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const item = items[next++]!
      await worker(item)
    }
  })
  await Promise.all(lanes)
}

export function chunk<T>(items: readonly T[], size: number): T[][] {
  const chunks: T[][] = []
  for (let index = 0; index < items.length; index += size) {
    chunks.push(items.slice(index, index + size))
  }
  return chunks
}

export interface DeletionOutcome {
  /** Keys the API did not delete. */
  failed: string[]
  /** The deletions of the keys deleted now; a key already deleted returns its existing deletion with no keys. */
  deletions: AnnotationDeletionResult[]
}

export interface RestoreOutcome {
  restored: Annotation[]
  /** Missing ancestors the API created for the restored keys. */
  created: string[]
  failed: AnnotationKeyFailure[]
}

export interface BulkCreateOutcome {
  /** Everything the API created, in the order it answered: ancestors and the keys of the list. */
  created: Annotation[]
  /** Sent keys the API skipped, because they existed already. */
  skipped: string[]
  /** Set when a batch failed; the batches before it stay created, and the failed one may have written some keys. */
  failure?: unknown
}

/** The keys a delete can be undone with: the roots of the deletions made now. */
export const undoKeys = (deletions: readonly AnnotationDeletionResult[]) =>
  deletions
    .filter((deletion) => deletion.DeletedKeys?.length && deletion.Deletion?.RootKey)
    .map((deletion) => deletion.Deletion!.RootKey!)

/**
 * Saving, deleting, restoring and the bulk actions of the annotation screens (spec section 8). Every change updates
 * the catalog entry locally and invalidates the matching detail queries, so nothing reloads the whole catalog.
 */
export function useAnnotationActions() {
  const catalog = useCatalogStore()
  const context = useContextStore()
  const queryClient = useQueryClient()
  const progress = ref<{ done: number; total: number }>()

  const scope = () => {
    if (!context.scope) {
      throw new Error('No view is selected.')
    }
    return context.scope
  }

  async function save(annotation: Annotation): Promise<Annotation> {
    const target = scope()
    const saved = await setAnnotation(target.org, target.project, target.view, annotation.AnnotationKey, annotation)
    catalog.upsert(saved)
    await invalidateAnnotation(queryClient, target, saved.AnnotationKey)
    return saved
  }

  /**
   * SetAnnotation per key, four at a time. The bulk endpoint (SetAnnotations) only creates and silently skips every
   * existing key, so edits go one by one, and the catalog takes what the server returned.
   */
  async function saveMany(annotations: Annotation[]): Promise<void> {
    const target = scope()
    const failed: string[] = []
    progress.value = { done: 0, total: annotations.length }
    try {
      await runLimited(annotations, writeConcurrency, async (annotation) => {
        try {
          const saved = await setAnnotation(
            target.org,
            target.project,
            target.view,
            annotation.AnnotationKey,
            annotation,
          )
          catalog.upsert(saved)
          await invalidateAnnotation(queryClient, target, saved.AnnotationKey)
        } catch {
          failed.push(annotation.AnnotationKey)
        } finally {
          progress.value = { done: progress.value!.done + 1, total: annotations.length }
        }
      })
    } finally {
      progress.value = undefined
    }
    if (failed.length) {
      throw new Error(
        `${failed.length} of ${annotations.length} annotations were not saved: ${failed.slice(0, 5).join(', ')}`,
      )
    }
  }

  /**
   * DeleteAnnotation per key, four at a time. The API moves the annotation and its descendants to Deleted and returns
   * every key it took (DeletedKeys), so exactly those leave the catalog. A key already gone (404) counts as deleted.
   */
  async function removeMany(keys: string[]): Promise<DeletionOutcome> {
    const target = scope()
    const failed: string[] = []
    const deletions: AnnotationDeletionResult[] = []
    progress.value = { done: 0, total: keys.length }
    try {
      await runLimited(keys, deleteConcurrency, async (key) => {
        let result: AnnotationDeletionResult | undefined
        try {
          result = await deleteAnnotation(target.org, target.project, target.view, key)
          deletions.push(result)
        } catch (error) {
          if (!isNotFound(error)) {
            failed.push(key)
            return
          }
        } finally {
          progress.value = { done: progress.value!.done + 1, total: keys.length }
        }
        for (const removed of result?.DeletedKeys?.length ? result.DeletedKeys : [key]) {
          catalog.remove(removed)
          forgetAnnotation(queryClient, target, removed)
        }
      })
    } finally {
      progress.value = undefined
    }
    await invalidateDeleted(queryClient, target)
    void catalog.refreshConfigurations()
    return { failed, deletions }
  }

  /**
   * RestoreAnnotations, up to 100 keys per request. The restored annotations (and the ancestors created for them)
   * go back into the catalog; a failure of one key does not stop the others.
   */
  async function restoreMany(keys: string[], options: { descendants?: boolean } = {}): Promise<RestoreOutcome> {
    const target = scope()
    const outcome: RestoreOutcome = { restored: [], created: [], failed: [] }

    for (const batch of chunk([...new Set(keys)], restoreBatchSize)) {
      const result = await restoreAnnotations(target.org, target.project, target.view, {
        Keys: batch,
        Descendants: options.descendants ?? true,
      })
      for (const annotation of result.Annotations ?? []) {
        catalog.upsert(annotation)
        outcome.restored.push(annotation)
      }
      outcome.created.push(...(result.CreatedKeys ?? []))
      outcome.failed.push(...(result.Failed ?? []))
    }

    await Promise.all([
      invalidateDeleted(queryClient, target),
      ...outcome.restored.map((annotation) => invalidateAnnotation(queryClient, target, annotation.AnnotationKey)),
    ])
    void catalog.refreshConfigurations()
    return outcome
  }

  /**
   * SetAnnotationsSimple, up to 100 keys per request, one request after another (424-33). The keys go in merge
   * order, so a key of the list never comes back as an ancestor of an earlier batch. The API creates the missing
   * ancestors and skips existing keys. A failed batch stops the run; the outcome keeps what was created and the
   * error, since the batches before it (and maybe a part of it) stay written.
   */
  async function createMany(keys: string[]): Promise<BulkCreateOutcome> {
    const target = scope()
    // A run of up to ten requests takes a while, and the user may move to another view meanwhile: the catalog then
    // holds that view, so it neither takes the annotations created here nor is replaced by a reload of this view.
    const inCatalog = () => !!catalog.scope && sameView(catalog.scope, target)
    const mergeOrder = (key: string) => getAnnotationTypeInfo(key.slice(0, key.indexOf('.')))?.mergeOrder ?? 0
    const ordered = [...new Set(keys)].toSorted((a, b) => mergeOrder(a) - mergeOrder(b))
    const outcome: BulkCreateOutcome = { created: [], skipped: [] }
    const sent: string[] = []
    let failed: string[] = []

    progress.value = { done: 0, total: ordered.length }
    try {
      for (const batch of chunk(ordered, simpleBatchSize)) {
        try {
          for (const annotation of await setAnnotationsSimple(target.org, target.project, target.view, batch)) {
            if (inCatalog()) {
              catalog.upsert(annotation)
            }
            outcome.created.push(annotation)
          }
          sent.push(...batch)
        } catch (error) {
          outcome.failure = error
          failed = batch
          break
        } finally {
          progress.value = { done: progress.value!.done + batch.length, total: ordered.length }
        }
      }
    } finally {
      progress.value = undefined
    }

    const created = new Set(outcome.created.map((annotation) => annotation.AnnotationKey))
    outcome.skipped = sent.filter((key) => !created.has(key))

    // The failed batch is in no response: its keys and their ancestors may have been written (or re-created).
    const touched = new Set([...created, ...failed.flatMap((key) => [key, ...getAllAncestors(key)])])
    await Promise.all([
      invalidateDeleted(queryClient, target),
      ...[...touched].map((key) => invalidateAnnotation(queryClient, target, key)),
    ])
    // The relation arrays of the response are snapshots that miss the children written later in the same request,
    // and a failed batch is in no response, so the catalog reloads in the background.
    if (inCatalog()) {
      void catalog.load(target, { force: true })
      void catalog.refreshConfigurations()
    }
    return outcome
  }

  /**
   * PurgeAnnotation per key (Administrator), four at a time. Returns the keys that failed; a key already gone (404,
   * for example a descendant of a key purged in the same batch) counts as purged.
   */
  async function purgeMany(keys: string[]): Promise<string[]> {
    const target = scope()
    const failed: string[] = []
    progress.value = { done: 0, total: keys.length }
    try {
      await runLimited(keys, deleteConcurrency, async (key) => {
        try {
          const result = await purgeAnnotation(target.org, target.project, target.view, key)
          for (const purged of result.PurgedKeys ?? [key]) {
            forgetAnnotation(queryClient, target, purged)
          }
        } catch (error) {
          if (!isNotFound(error)) {
            failed.push(key)
          }
        } finally {
          progress.value = { done: progress.value!.done + 1, total: keys.length }
        }
      })
    } finally {
      progress.value = undefined
    }
    await invalidateDeleted(queryClient, target)
    return failed
  }

  const selected = (keys: string[]) =>
    keys.map((key) => catalog.byKey.get(key)).filter((annotation): annotation is Annotation => !!annotation)

  return {
    progress,
    save,
    saveMany,
    removeMany,
    remove: (key: string) => removeMany([key]),
    restoreMany,
    createMany,
    purgeMany,
    setDisabled: (keys: string[], disabled: boolean) =>
      saveMany(selected(keys).map((annotation) => ({ ...annotation, IsDisabled: disabled ? true : null }))),
    addLabel: (keys: string[], label: string) =>
      saveMany(
        selected(keys).map((annotation) => ({
          ...annotation,
          Labels: normalizeLabels([...(annotation.Labels ?? []), label]),
        })),
      ),
    removeLabel: (keys: string[], label: string) =>
      saveMany(
        selected(keys).map((annotation) => ({
          ...annotation,
          Labels: (annotation.Labels ?? []).filter((existing) => existing.toLowerCase() !== label.toLowerCase()),
        })),
      ),
  }
}
