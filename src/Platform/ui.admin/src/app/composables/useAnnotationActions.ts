import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  deleteAnnotation,
  getAnnotation,
  hasErrorCode,
  isNotFound,
  patchAnnotationVersioned,
  purgeAnnotation,
  restoreAnnotations,
  setAnnotationsSimple,
  type Annotation,
  type AnnotationDeletionResult,
  type AnnotationKeyFailure,
  type AnnotationPatchBody,
  type JsonPatchOperation,
} from '@42for.net/2splatform.sdk'
import { forgetAnnotation, invalidateAnnotation, invalidateDeleted } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore, type Scope } from '@/stores/context'
import { getAllAncestors, getAnnotationTypeInfo } from '@/utils/annotation-key'
import { sameView } from '@/utils/context-paths'
import { labelAddition, labelGuard, labelRemoval, patchableLabels } from '@/utils/labels'

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

/** The attempts of a label action whose test fails because the labels changed meanwhile (412 PatchTestFailed). */
const labelAttempts = 3

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
 * Saving, deleting, restoring and the bulk actions of the annotation screens (spec section 8). Every change of an
 * existing annotation is a PATCH of only that change (424-225), so a concurrent change of another member is kept.
 * Every change updates the catalog entry locally and invalidates the matching detail queries, so nothing reloads the
 * whole catalog.
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

  // The catalog holds one view; a run that outlives a move to another view must not write into it.
  const catalogHolds = (target: Scope) => !!catalog.scope && sameView(catalog.scope, target)

  /**
   * PatchAnnotation (424-225): a merge patch (an object) or a JSON Patch (an array) of only what changes, merged on
   * the server against the stored state, so a concurrent change of another member is kept. A patch never creates or
   * re-creates a key (404 AnnotationNotFound or AnnotationDeleted). The catalog takes the stored result. `target` is
   * the view of the run: a bulk run keeps the view it started in, also when the user moves to another one meanwhile.
   */
  async function patch(key: string, body: AnnotationPatchBody, target: Scope = scope()): Promise<Annotation> {
    const { value: saved } = await patchAnnotationVersioned(target.org, target.project, target.view, key, body)
    if (catalogHolds(target)) {
      catalog.upsert(saved)
    }
    await invalidateAnnotation(queryClient, target, saved.AnnotationKey)
    return saved
  }

  /**
   * One write per annotation, four at a time, all to the view the run started in: `write` patches it and returns the
   * stored result, or undefined when it had nothing to write. The bulk endpoint (SetAnnotations) only creates, so edits
   * go one by one. A key deleted (or purged) meanwhile, which a patch answers and a read again with 404, is not
   * re-created: it fails, and it leaves the catalog, as a delete would have taken it.
   */
  async function patchMany(
    annotations: Annotation[],
    write: (annotation: Annotation, target: Scope) => Promise<Annotation | undefined>,
  ): Promise<void> {
    const target = scope()
    const failed: string[] = []
    const gone: string[] = []
    progress.value = { done: 0, total: annotations.length }
    try {
      await runLimited(annotations, writeConcurrency, async (annotation) => {
        try {
          await write(annotation, target)
        } catch (error) {
          if (!isNotFound(error)) {
            failed.push(annotation.AnnotationKey)
            return
          }
          if (catalogHolds(target)) {
            catalog.remove(annotation.AnnotationKey)
          }
          forgetAnnotation(queryClient, target, annotation.AnnotationKey)
          gone.push(annotation.AnnotationKey)
        } finally {
          progress.value = { done: progress.value!.done + 1, total: annotations.length }
        }
      })
    } finally {
      progress.value = undefined
    }
    if (gone.length) {
      void invalidateDeleted(queryClient, target)
    }
    // the keys deleted meanwhile apart from the failed writes: they are not an error to retry, they left the list
    const problems = [
      failed.length &&
        `${failed.length} of ${annotations.length} annotations were not saved: ${failed.slice(0, 5).join(', ')}.`,
      gone.length &&
        `${gone.length} of ${annotations.length} annotations were deleted meanwhile and left the list: ${gone.slice(0, 5).join(', ')}.`,
    ].filter(Boolean)
    if (problems.length) {
      throw new Error(problems.join(' '))
    }
  }

  /**
   * A label action as a JSON Patch guarded by a test of the labels it was built from (labelAddition, labelRemoval),
   * at most three attempts: first from the catalog's copy; when the labels changed meanwhile (412 PatchTestFailed), the
   * annotation is read again and the patch rebuilt from its labels. The catalog's copy may be stale, so when it needs
   * no change the guard goes alone (a patch that changes nothing writes nothing): it confirms the copy, or fails and
   * the fresh labels decide. A fresh copy that needs no change is done, and goes into the catalog.
   */
  async function patchLabels(
    annotation: Annotation,
    build: (labels: string[]) => JsonPatchOperation[] | undefined,
    target: Scope,
  ): Promise<Annotation | undefined> {
    let current = annotation
    for (let attempt = 1; ; attempt++) {
      const labels = patchableLabels(current.Labels)
      const operations = build(labels) ?? (current === annotation ? [labelGuard(labels)] : undefined)
      if (!operations) {
        // a fresh copy that needs no change: the list and the detail show it
        if (catalogHolds(target)) {
          catalog.upsert(current)
        }
        await invalidateAnnotation(queryClient, target, current.AnnotationKey)
        return undefined
      }
      try {
        return await patch(annotation.AnnotationKey, operations, target)
      } catch (error) {
        if (!hasErrorCode(error, 'PatchTestFailed') || attempt >= labelAttempts) {
          throw error
        }
      }
      current = await getAnnotation(target.org, target.project, target.view, annotation.AnnotationKey, {
        includeInactive: true,
      })
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
   * go back into the catalog; a failure of one key does not stop the others. Only the deleted lists (the Deleted page's
   * own data) are awaited; the queries of the restored keys refresh in the background.
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

    for (const annotation of outcome.restored) {
      void invalidateAnnotation(queryClient, target, annotation.AnnotationKey)
    }
    await invalidateDeleted(queryClient, target)
    void catalog.refreshConfigurations()
    return outcome
  }

  /**
   * SetAnnotationsSimple, up to 100 keys per request, one request after another (424-33). The keys go in merge
   * order, so a key of the list never comes back as an ancestor of an earlier batch. The API creates the missing
   * ancestors and skips existing keys. A failed batch stops the run; the outcome keeps what was created and the
   * error, since the batches before it (and maybe a part of it) stay written. It returns after the writes: the refresh
   * of the touched queries and of the catalog does not hold the dialog's result.
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
    void invalidateDeleted(queryClient, target)
    for (const key of touched) {
      void invalidateAnnotation(queryClient, target, key)
    }
    // The relation arrays of the response are snapshots that miss the children written later in the same request,
    // and a failed batch is in no response, so the catalog reloads in the background (its configurations with it).
    if (inCatalog()) {
      void catalog.load(target, { force: true })
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

  /**
   * A label action over the selected keys. A key that needs no change (it has the label in any spelling, for an add;
   * it lacks it, for a remove: the UI keeps labels unique case-insensitively, the API dedupes ordinally only) is checked
   * by the guard alone, since its catalog copy may be stale.
   */
  const labelAction = (keys: string[], build: (labels: string[]) => JsonPatchOperation[] | undefined) =>
    patchMany(selected(keys), (annotation, target) => patchLabels(annotation, build, target))

  return {
    progress,
    patch,
    patchMany,
    removeMany,
    remove: (key: string) => removeMany([key]),
    restoreMany,
    createMany,
    purgeMany,
    /** A merge patch of IsDisabled alone (null enables: the API treats null and false alike, and the form writes null). */
    setDisabled: (keys: string[], disabled: boolean) =>
      patchMany(selected(keys), (annotation, target) =>
        patch(annotation.AnnotationKey, { IsDisabled: disabled ? true : null }, target),
      ),
    /** Appends the trimmed label (add /Labels/-) where no spelling of it is, guarded by the labels read. */
    addLabel: (keys: string[], label: string) =>
      label.trim() ? labelAction(keys, (labels) => labelAddition(labels, label)) : Promise.resolve(),
    /** Removes every spelling of the label (remove /Labels/<index>), guarded by the labels read. */
    removeLabel: (keys: string[], label: string) =>
      label.trim() ? labelAction(keys, (labels) => labelRemoval(labels, label)) : Promise.resolve(),
  }
}
