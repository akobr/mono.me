<script setup lang="ts">
import { computed } from 'vue'
import { useNow } from '@vueuse/core'
import type { ConfigurationVersion } from '@42for.net/2splatform.sdk'

/**
 * The version history, newest first (spec section 11.3). History expires; expired versions are marked and their
 * documents may no longer load.
 */
const props = defineProps<{ versions: ConfigurationVersion[]; current?: number; selected?: number; from?: number }>()
const emit = defineEmits<{ view: [version: number]; compare: [version: number, from: number] }>()

const now = useNow({ interval: 60_000 })
const sorted = computed(() => props.versions.toSorted((a, b) => (b.Version ?? 0) - (a.Version ?? 0)))
const expired = (entry: ConfigurationVersion) =>
  !!entry.ExpirationTime && Date.parse(entry.ExpirationTime) <= now.value.getTime()
</script>

<template>
  <ol class="divide-y divide-default rounded-md border border-default">
    <li
      v-for="(entry, index) in sorted"
      :key="entry.Version"
      class="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm"
      :class="entry.Version === selected || entry.Version === from ? 'bg-primary/5' : ''"
    >
      <span class="w-10 font-mono font-semibold text-highlighted">v{{ entry.Version }}</span>
      <UBadge v-if="entry.Version === current" label="current" color="primary" variant="subtle" size="sm" />
      <UBadge v-if="expired(entry)" label="expired" color="warning" variant="subtle" size="sm" />
      <span class="min-w-0 truncate"><AuditActorLink :actor="entry.Modified?.By" subtle /></span>
      <RelativeTime :value="entry.Modified?.At" class="text-xs text-dimmed" />
      <span class="ms-auto flex gap-1">
        <UButton label="View" size="xs" color="neutral" variant="ghost" @click="emit('view', entry.Version!)" />
        <UButton
          v-if="sorted[index + 1]"
          label="Compare with previous"
          size="xs"
          color="neutral"
          variant="ghost"
          @click="emit('compare', entry.Version!, sorted[index + 1]!.Version!)"
        />
      </span>
    </li>
    <li v-if="sorted.length === 0" class="px-3 py-4 text-sm text-muted">No versions.</li>
  </ol>
</template>
