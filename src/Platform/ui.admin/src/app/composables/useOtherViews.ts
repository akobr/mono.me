import { computed } from 'vue'
import { useGetViews } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'

/**
 * The views of the project that a comparison or a copy can go to: every listed view but the one of the route (spec
 * "Copy and Compare Annotations and Configurations Between Views", section 8). The query has the arguments of the
 * Modeling header, Compare views and the context switcher, so they share it. Views that exist only in data
 * (discovered ones) are not listed: a copy must not go to a view nobody registered.
 */
export function useOtherViews() {
  const context = useContextStore()
  const views = useGetViews(
    () => context.org ?? '',
    () => context.project ?? '',
    undefined,
    { query: { enabled: () => !!context.project } },
  )

  /** The names of the other views, in the order of the API: the default view first. */
  const names = computed(() =>
    (views.data.value ?? []).map((view) => view.Name ?? '').filter((name) => name && name !== context.view),
  )
  /** The views are known. */
  const known = computed(() => views.isSuccess.value)
  /** The views are known, and the project has only the view of the route. */
  const alone = computed(() => known.value && names.value.length === 0)
  /** Whether the name is another listed view of the project. */
  const has = (name: string | undefined) => !!name && names.value.includes(name)

  return { views, names, known, alone, has }
}
