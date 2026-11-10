<script setup lang="ts">
import { computed } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getConfigurationVersion,
  getConfigurationVersionDiffCustom,
  getConfigurationVersions,
  restoreConfigurationVersion,
  type DiffResult,
} from '@42for.net/2splatform.sdk'
import { getGetConfigurationQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateConfiguration } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import type { VersionSource } from '@/components/versions/version-source'

/**
 * The Versions tab of a configuration. A deleted configuration keeps its versions, and restoring one brings it back.
 * Without an active annotation nothing can be restored: the API refuses until a deleted annotation is restored, and a
 * purged one keeps only its versions until they expire.
 */
const props = defineProps<{ annotationKey: string }>()

const scope = useScope()
const catalog = useCatalogStore()
const queryClient = useQueryClient()
const { version, missing, noOwn } = useConfiguration(() => props.annotationKey)

const source = computed<VersionSource>(() => {
  const [org, project, view, key] = [scope.org(), scope.project(), scope.view(), props.annotationKey]
  return {
    document: 'configuration',
    queryKey: getGetConfigurationQueryKey(org, project, view, key),
    list: (signal) => getConfigurationVersions(org, project, view, key, { signal }),
    content: async (target, signal) =>
      (await getConfigurationVersion(org, project, view, key, target, { signal })).Content,
    diff: async (target, from, signal) =>
      (await getConfigurationVersionDiffCustom(org, project, view, key, target, from, undefined, {
        signal,
      })) as DiffResult,
    current: version.value,
    restore: missing.value
      ? undefined
      : (target, force) =>
          restoreConfigurationVersion(org, project, view, key, target, force ? { force: true } : undefined),
    // As after a save: the list and the badges follow at once, the documents and the history refresh.
    afterRestore: async (result) => {
      if (result.Outcome === 'Restored') {
        catalog.setConfiguration(key, {
          AnnotationKey: key,
          Version: result.Version,
          Modified: result.Modified,
          HasContent: true,
        })
      }
      await invalidateConfiguration(queryClient, { org, project, view }, key)
    },
  }
})
</script>

<template>
  <VersionsView :source="source" :deleted="noOwn || missing" empty-title="No configuration, no history" />
</template>
