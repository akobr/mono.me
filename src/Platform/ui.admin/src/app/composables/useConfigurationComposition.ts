import { toValue, type MaybeRefOrGetter } from 'vue'
import { useGetConfigurationComposition } from '@42for.net/2splatform.sdk/vue-query'
import { useScope } from './useAppContext'

/**
 * How the effective configuration is composed (spec 8.2 of the Composition of the Effective Configuration): the
 * merged documents in order and the step that set each value. It is asked only while the Composition view is shown,
 * never cached as fresh and read again on focus, so it is not part of `useConfiguration`, which the Edit, Effective,
 * Versions and Schema tabs start eagerly. Its key sits under the configuration's path, so `invalidateConfiguration`
 * and a template save's `invalidateAllConfigurations` cover it.
 */
export function useConfigurationComposition(key: MaybeRefOrGetter<string>, shown: MaybeRefOrGetter<boolean> = true) {
  const scope = useScope()
  const keyValue = () => toValue(key)

  return useGetConfigurationComposition(scope.org, scope.project, scope.view, keyValue, {
    query: {
      enabled: () => toValue(shown) && !!keyValue() && !!scope.view(),
      staleTime: 0,
      refetchOnWindowFocus: true,
    },
  })
}
