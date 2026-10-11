import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { isNotFound } from '@42for.net/2splatform.sdk'
import {
  useGetCombinedConfigurationSchema,
  useGetConfiguration,
  useGetConfigurationVersion,
} from '@42for.net/2splatform.sdk/vue-query'
import { useScope } from './useAppContext'

/**
 * The documents of one configuration (spec section 11.2):
 * - effective: GetConfiguration (ancestors, then the type template, then the own content, merged), with `Version`;
 * - own: GetConfigurationVersion(key, Version), the stored own content that the editor patches; it answers 404
 *   when nothing is stored yet (the effective document then has Version 0), and a PATCH would fail the same way;
 * - schema: GetCombinedConfigurationSchema (MergedContent and the applied layers).
 */
export function useConfiguration(key: MaybeRefOrGetter<string>) {
  const scope = useScope()
  const keyValue = () => toValue(key)
  const enabled = () => !!keyValue() && !!scope.view()

  const effective = useGetConfiguration(scope.org, scope.project, scope.view, keyValue, {
    query: { enabled, refetchOnWindowFocus: true },
  })
  const version = computed(() => effective.data.value?.Version)

  const own = useGetConfigurationVersion(scope.org, scope.project, scope.view, keyValue, () => version.value ?? 0, {
    query: { enabled: () => enabled() && version.value !== undefined },
  })

  const combined = useGetCombinedConfigurationSchema(scope.org, scope.project, scope.view, keyValue, {
    query: { enabled, staleTime: 5 * 60_000 },
  })

  /** The annotation does not exist (GetConfiguration 404). */
  const missing = computed(() => effective.isError.value && isNotFound(effective.error.value))
  /** Nothing is stored for the annotation yet: the first content has to be created (POST), not patched. */
  const noOwn = computed(() => own.isError.value && isNotFound(own.error.value))

  /** The merged JSON Schema, or undefined when no schema applies (Form mode is then hidden). */
  const schema = computed(() => {
    const merged = combined.data.value?.MergedContent as Record<string, unknown> | undefined
    return merged && Object.keys(merged).some((name) => name !== '$schema') ? merged : undefined
  })

  return { effective, own, combined, missing, noOwn, version, schema }
}
