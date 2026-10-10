import { toValue, type MaybeRefOrGetter } from 'vue'
import { useGetAnnotation } from '@42for.net/2splatform.sdk/vue-query'
import { useScope } from '@/composables/useAppContext'

// Thin wrappers that bind org, project and view of the current context to the generated composables, so
// screens pass only the key (spec section 6).

export function useAnnotation(key: MaybeRefOrGetter<string | undefined>) {
  const scope = useScope()
  return useGetAnnotation(scope.org, scope.project, scope.view, () => toValue(key) ?? '', {
    query: { enabled: () => !!toValue(key) && !!scope.view(), staleTime: 30_000, refetchOnWindowFocus: true },
  })
}
