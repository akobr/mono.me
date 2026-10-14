<script setup lang="ts">
import { computed } from 'vue'
import type { ConfigurationImport, ConfigurationImportItem } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'

/** The outcome of an import (spec 4.3): the counts, and every file that was not unchanged. */
const props = defineProps<{ result: ConfigurationImport; scope: Scope }>()

type Color = 'success' | 'info' | 'neutral' | 'warning' | 'error'

const outcomes: Record<
  NonNullable<ConfigurationImportItem['Outcome']>,
  { color: Color; dryRun: string; label: string }
> = {
  Created: { color: 'success', label: 'Created', dryRun: 'To create' },
  Updated: { color: 'info', label: 'Updated', dryRun: 'To update' },
  Unchanged: { color: 'neutral', label: 'Unchanged', dryRun: 'Unchanged' },
  Skipped: { color: 'warning', label: 'Skipped', dryRun: 'To skip' },
  Failed: { color: 'error', label: 'Failed', dryRun: 'Fails' },
}

const counts = computed(() => {
  const summary = props.result.Summary ?? {}
  return [
    { outcome: 'Created' as const, count: summary.Created ?? 0 },
    { outcome: 'Updated' as const, count: summary.Updated ?? 0 },
    { outcome: 'Unchanged' as const, count: summary.Unchanged ?? 0 },
    { outcome: 'Skipped' as const, count: summary.Skipped ?? 0 },
    { outcome: 'Failed' as const, count: summary.Failed ?? 0 },
  ]
})

const label = (outcome: ConfigurationImportItem['Outcome']) =>
  outcome ? (props.result.IsDryRun ? outcomes[outcome].dryRun : outcomes[outcome].label) : ''

const link = (key: string) => `/${props.scope.org}/${props.scope.project}/${props.scope.view}/configurations/${key}`
</script>

<template>
  <div class="space-y-3">
    <ul class="flex flex-wrap gap-2" aria-label="Summary">
      <li v-for="item in counts" :key="item.outcome">
        <UBadge
          :color="outcomes[item.outcome].color"
          variant="subtle"
          :label="`${label(item.outcome)} ${item.count}`"
        />
      </li>
      <li v-if="result.Summary?.IgnoredFiles">
        <UBadge color="neutral" variant="outline" :label="`Ignored files ${result.Summary.IgnoredFiles}`" />
      </li>
    </ul>

    <p v-if="!result.Items?.length" class="text-sm text-muted">
      {{
        result.IsDryRun ? 'Every document equals the stored one.' : 'Every document already equalled the stored one.'
      }}
    </p>
    <ul
      v-else
      class="max-h-80 divide-y divide-default overflow-auto rounded-md border border-default text-sm"
      aria-label="Files"
    >
      <li v-for="item in result.Items" :key="item.Path" class="flex items-start gap-3 px-3 py-2">
        <UBadge
          class="mt-0.5 shrink-0"
          :color="item.Outcome ? outcomes[item.Outcome].color : 'neutral'"
          variant="subtle"
          :label="label(item.Outcome)"
        />
        <div class="min-w-0 flex-1">
          <RouterLink
            v-if="item.AnnotationKey && (item.Outcome === 'Created' || item.Outcome === 'Updated') && !result.IsDryRun"
            :to="link(item.AnnotationKey)"
            class="font-mono text-xs text-primary hover:underline"
          >
            {{ item.AnnotationKey }}
          </RouterLink>
          <KeyText v-else :value="item.AnnotationKey ?? item.Path ?? ''" />
          <p v-if="item.Message" class="text-xs break-words text-muted">{{ item.Message }}</p>
        </div>
        <span v-if="item.Version" class="shrink-0 font-mono text-xs text-muted">v{{ item.Version }}</span>
      </li>
    </ul>
    <p v-if="result.AreItemsTruncated" class="text-xs text-muted">
      Only the first {{ result.Items?.length }} files are listed; the counts include every file.
    </p>
  </div>
</template>
