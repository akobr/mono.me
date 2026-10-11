import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { deleteAnnotation, setAnnotation, type Annotation } from '@42for.net/2splatform.sdk'
import { forgetAnnotation, invalidateAnnotation } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { normalizeLabels } from '@/utils/labels'

const writeConcurrency = 4
const deleteConcurrency = 4

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

/**
 * Saving, deleting and the bulk actions of the annotation screens (spec section 8). Every change updates the
 * catalog entry locally and invalidates the matching detail queries, so nothing reloads the whole catalog.
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
          const saved = await setAnnotation(target.org, target.project, target.view, annotation.AnnotationKey, annotation)
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

  /** DeleteAnnotation per key, four at a time. Returns the keys that failed. */
  async function removeMany(keys: string[]): Promise<string[]> {
    const target = scope()
    const failed: string[] = []
    progress.value = { done: 0, total: keys.length }
    try {
      await runLimited(keys, deleteConcurrency, async (key) => {
        try {
          await deleteAnnotation(target.org, target.project, target.view, key)
          catalog.remove(key)
          forgetAnnotation(queryClient, target, key)
        } catch {
          failed.push(key)
        } finally {
          progress.value = { done: progress.value!.done + 1, total: keys.length }
        }
      })
    } finally {
      progress.value = undefined
    }
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
