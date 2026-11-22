import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { sameView } from '@/utils/context-paths'
import { copyToView, copyWrites, type ViewCopyRequest, type ViewCopyResult } from '@/utils/view-copy'

/**
 * The copy of annotations or configurations to another view of the project, with its progress (spec "Copy and
 * Compare Annotations and Configurations Between Views", section 7). The run itself is `copyToView`; this binds it to
 * the query cache and the catalog of the app.
 */
export function useViewCopy() {
  const client = useQueryClient()
  const catalog = useCatalogStore()
  /** The keys handled so far; undefined while nothing runs. */
  const progress = ref<{ done: number; total: number }>()

  /** Runs the request, or checks it (`dryRun`). The signal stops it after the key that is under way. */
  async function run(request: ViewCopyRequest, signal?: AbortSignal): Promise<ViewCopyResult> {
    progress.value = { done: 0, total: new Set(request.keys).size }
    try {
      const result = await copyToView(request, {
        client,
        signal,
        onProgress: (done, total) => (progress.value = { done, total }),
      })

      // The catalog holds the source view, which a copy does not change. When it holds the target by the end of the
      // run (the user went there meanwhile), what was written is in none of its lists.
      const target = { ...request.source, view: request.target }
      if (!result.dryRun && copyWrites(result.items) > 0 && catalog.scope && sameView(catalog.scope, target)) {
        void catalog.load(target, { force: true })
      }
      return result
    } finally {
      progress.value = undefined
    }
  }

  return { progress, run }
}
