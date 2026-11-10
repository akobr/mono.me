<script setup lang="ts">
import { computed } from 'vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  getConfigurationVersion,
  getConfigurationVersionDiffCustom,
  getConfigurationVersions,
  hasErrorCode,
  restoreConfigurationVersion,
  type DiffResult,
} from '@42for.net/2splatform.sdk'
import { getGetConfigurationQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateConfiguration } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { versionsQueryKey, type VersionSource } from '@/components/versions/version-source'

/**
 * The Versions tab of a configuration. A deleted configuration keeps its versions, and restoring one brings it back.
 * Without an active annotation nothing can be restored: a deleted annotation hides its versions until it is restored
 * (the API answers 404 AnnotationDeleted), and a purged one keeps only its versions until they expire.
 */
const props = defineProps<{ annotationKey: string }>()

const scope = useScope()
const catalog = useCatalogStore()
const queryClient = useQueryClient()
const { effective, version, missing, noOwn } = useConfiguration(() => props.annotationKey)

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

// The history's own list query, observed here too: a deleted annotation fails it with AnnotationDeleted.
const versions = useQuery({
  queryKey: computed(() => versionsQueryKey(source.value)),
  queryFn: ({ signal }) => source.value.list(signal),
})

/** The annotation is deleted: the configuration read or the version list answers 404 AnnotationDeleted. */
const annotationDeleted = computed(
  () =>
    hasErrorCode(effective.error.value, 'AnnotationDeleted') || hasErrorCode(versions.error.value, 'AnnotationDeleted'),
)
</script>

<template>
  <EmptyState
    v-if="annotationDeleted"
    icon="i-lucide-archive"
    title="The annotation is deleted"
    description="Restore the annotation before its configuration versions can be seen or restored."
  >
    <UButton
      :to="`/${scope.org()}/${scope.project()}/${scope.view()}/annotations/${annotationKey}`"
      label="Open the annotation"
      icon="i-lucide-tags"
      color="neutral"
      variant="outline"
    />
  </EmptyState>
  <VersionsView v-else :source="source" :deleted="noOwn || missing" empty-title="No configuration, no history" />
</template>
