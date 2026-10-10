<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { toJson } from '@/utils/json-yaml'
import type { VersionSource } from './version-source'

const DiffEditor = defineAsyncComponent(() => import('@/components/editor/DiffEditor.vue'))

/**
 * Compare two versions (spec section 11.3): the server DiffResult gives the counts and hunk positions; both full
 * documents are serialized identically (2 spaces, original key order) for the side-by-side view, so the line diff
 * and the server hunks agree. When a document is gone (expired or deleted), the server hunks are shown instead.
 */
const props = defineProps<{ source: VersionSource; version: number; from: number }>()

const diff = useQuery({
  queryKey: computed(() => [...props.source.queryKey, 'diff', props.version, props.from]),
  queryFn: ({ signal }) => props.source.diff(props.version, props.from, signal),
  meta: { silent: true },
})

const documents = useQuery({
  queryKey: computed(() => [...props.source.queryKey, 'pair', props.from, props.version]),
  queryFn: async ({ signal }) => {
    const [before, after] = await Promise.allSettled([
      props.source.content(props.from, signal),
      props.source.content(props.version, signal),
    ])
    return {
      before: before.status === 'fulfilled' ? toJson(before.value) : undefined,
      after: after.status === 'fulfilled' ? toJson(after.value) : undefined,
    }
  },
})

const complete = computed(() => documents.data.value?.before !== undefined && documents.data.value?.after !== undefined)
</script>

<template>
  <div class="space-y-3">
    <p class="text-sm">
      <span class="font-mono">v{{ from }}</span> → <span class="font-mono">v{{ version }}</span>
    </p>
    <LoadingState v-if="documents.isPending.value || diff.isPending.value" label="Loading both versions…" />
    <ErrorState v-else-if="diff.isError.value && !complete" :error="diff.error.value" @retry="diff.refetch()" />
    <DiffEditor
      v-else-if="complete"
      :original="documents.data.value!.before!"
      :modified="documents.data.value!.after!"
      :stats="diff.data.value?.Stats"
      :hunks="diff.data.value?.Hunks"
      :original-label="`v${from}`"
      :modified-label="`v${version}`"
    />
    <template v-else>
      <UAlert
        color="warning"
        variant="subtle"
        icon="i-lucide-history"
        title="A version is no longer available"
        description="Its document expired or was deleted, so only the changes recorded by the server are shown."
      />
      <HunkList :hunks="diff.data.value?.Hunks ?? []" />
    </template>
  </div>
</template>
