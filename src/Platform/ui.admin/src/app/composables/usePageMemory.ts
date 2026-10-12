import { computed, type ComputedRef } from 'vue'
import { useContextStore } from '@/stores/context'
import { usePageMemoryStore, type RememberedSection, type SectionMemories } from '@/stores/page-memory'

/**
 * The remembered state of a view section for the view of the route. It follows the view switcher, and keeps the
 * last view's memory while the route is already outside a view and the page has not unmounted yet.
 */
export function usePageMemory<S extends RememberedSection>(section: S): ComputedRef<SectionMemories[S]> {
  const store = usePageMemoryStore()
  const context = useContextStore()

  return computed<SectionMemories[S]>((previous) => {
    const scope = context.scope
    if (scope) {
      return store.page(scope, section)
    }
    return previous ?? store.page({ org: '', project: '', view: '' }, section)
  })
}
