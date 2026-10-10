<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import type { VersionSource } from './version-source'

/**
 * Timeline, single-version view and compare, addressed by `?v=7&from=5` (spec section 11.3). Reused by
 * configurations, templates and all three schema kinds.
 */
const props = defineProps<{ source: VersionSource }>()

const route = useRoute()
const router = useRouter()
const preferences = usePreferencesStore()

const versions = useQuery({
  queryKey: computed(() => [...props.source.queryKey, 'versions']),
  queryFn: ({ signal }) => props.source.list(signal),
})

const number = (value: unknown) => {
  const parsed = Number(Array.isArray(value) ? value[0] : value)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined
}
const selected = computed(() => number(route.query.v))
const from = computed(() => number(route.query.from))

const options = computed(() =>
  [...(versions.data.value ?? [])]
    .toSorted((a, b) => (b.Version ?? 0) - (a.Version ?? 0))
    .map((entry) => ({ label: `v${entry.Version}`, value: entry.Version! })),
)
const pickFrom = ref<number>()
const pickTo = ref<number>()
watch(
  [from, selected],
  ([fromValue, selectedValue]) => {
    pickFrom.value = fromValue
    pickTo.value = selectedValue
  },
  { immediate: true },
)

const format = ref<DocumentFormat>(preferences.state.editorFormat)
const single = useQuery({
  queryKey: computed(() => [...props.source.queryKey, 'version', selected.value]),
  queryFn: ({ signal }) => props.source.content(selected.value!, signal),
  enabled: computed(() => !!selected.value && !from.value),
  meta: { silent: true },
})

function go(query: { v?: number; from?: number }) {
  void router.replace({ query: { ...route.query, v: query.v, from: query.from } })
}
</script>

<template>
  <div class="space-y-4 p-4">
    <LoadingState v-if="versions.isPending.value" />
    <ErrorState v-else-if="versions.isError.value" :error="versions.error.value" @retry="versions.refetch()" />
    <template v-else>
      <div class="flex flex-wrap items-end gap-2">
        <UFormField label="Compare">
          <USelect v-model="pickFrom" :items="options" placeholder="From" class="w-28" />
        </UFormField>
        <UFormField label="with">
          <USelect v-model="pickTo" :items="options" placeholder="To" class="w-28" />
        </UFormField>
        <UButton
          label="Compare"
          icon="i-lucide-git-compare"
          :disabled="!pickFrom || !pickTo || pickFrom === pickTo"
          @click="go({ v: pickTo, from: pickFrom })"
        />
        <UButton v-if="selected" label="Back to history" color="neutral" variant="ghost" @click="go({})" />
      </div>

      <VersionCompare v-if="selected && from" :source="source" :version="selected" :from="from" />

      <template v-else-if="selected">
        <p class="text-sm">
          Version <span class="font-mono">v{{ selected }}</span>
        </p>
        <LoadingState v-if="single.isPending.value" />
        <ErrorState
          v-else-if="single.isError.value"
          :error="single.error.value"
          title="This version is not available"
          @retry="single.refetch()"
        />
        <DocumentEditor
          v-else
          :model-value="single.data.value ?? {}"
          :format="format"
          readonly
          :aria-label="`Version ${selected}`"
          @update:format="(value) => (format = value)"
        />
      </template>

      <VersionTimeline
        :versions="versions.data.value ?? []"
        :current="source.current"
        :selected="selected"
        :from="from"
        @view="(version) => go({ v: version })"
        @compare="(version, previous) => go({ v: version, from: previous })"
      />
    </template>
  </div>
</template>
