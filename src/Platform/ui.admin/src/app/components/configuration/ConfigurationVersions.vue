<script setup lang="ts">
import { computed } from 'vue'
import {
  getConfigurationVersion,
  getConfigurationVersionDiffCustom,
  getConfigurationVersions,
  type DiffResult,
} from '@42for.net/2splatform.sdk'
import { getGetConfigurationQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import type { VersionSource } from '@/components/versions/version-source'

/** The Versions tab of a configuration. */
const props = defineProps<{ annotationKey: string }>()

const scope = useScope()
const { version, missing } = useConfiguration(() => props.annotationKey)

const source = computed<VersionSource>(() => {
  const [org, project, view, key] = [scope.org(), scope.project(), scope.view(), props.annotationKey]
  return {
    queryKey: getGetConfigurationQueryKey(org, project, view, key),
    list: (signal) => getConfigurationVersions(org, project, view, key, { signal }),
    content: async (target, signal) =>
      (await getConfigurationVersion(org, project, view, key, target, { signal })).Content,
    diff: async (target, from, signal) =>
      (await getConfigurationVersionDiffCustom(org, project, view, key, target, from, undefined, {
        signal,
      })) as DiffResult,
    current: version.value,
  }
})
</script>

<template>
  <EmptyState v-if="missing" icon="i-lucide-history" title="No configuration, no history" />
  <VersionsView v-else :source="source" />
</template>
