<script setup lang="ts">
import { computed } from 'vue'
import { isNotFound, type AnnotationAuditEntry } from '@42for.net/2splatform.sdk'
import { useAnnotationHistory, useDeletedAnnotation } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { actionColors, actionIcons, actionLabels, auditChanges, causeText } from '@/utils/audit'

/**
 * The History tab (spec 4.3): who changed the annotation, when and what, newest first. A deleted annotation gets a
 * banner with Restore above its history.
 */
const props = defineProps<{ annotationKey: string }>()
const emit = defineEmits<{ restored: [] }>()

const context = useContextStore()
const history = useAnnotationHistory(() => props.annotationKey)
const isDeleted = computed(() => history.data.value?.State === 'Deleted')
const deleted = useDeletedAnnotation(() => props.annotationKey, isDeleted)

interface Item {
  value: string
  icon: string
  entry: AnnotationAuditEntry
}

const actionLabel = (item: Item) => (item.entry.Action ? actionLabels[item.entry.Action] : '')
const color = (item: Item) => (item.entry.Action ? actionColors[item.entry.Action] : 'neutral')

const items = computed<Item[]>(() =>
  (history.data.value?.Entries ?? []).map((entry, index) => ({
    value: entry.Id ?? String(index),
    icon: entry.Action ? actionIcons[entry.Action] : 'i-lucide-circle',
    entry,
  })),
)

const versionLink = (version: number) => ({
  path: `/${context.org}/${context.project}/${context.view}/configurations/${props.annotationKey}/versions`,
  query: { v: String(version) },
})
</script>

<template>
  <div class="flex flex-col gap-4 p-4">
    <DeletedAnnotationBanner v-if="deleted.data.value" :annotation="deleted.data.value" @restored="emit('restored')" />

    <LoadingState v-if="history.isPending.value" label="Loading the history…" />
    <EmptyState
      v-else-if="history.isError.value && isNotFound(history.error.value)"
      icon="i-lucide-history"
      title="No history"
      :description="`${annotationKey} does not exist in this view, or it was deleted permanently.`"
    />
    <ErrorState v-else-if="history.isError.value" :error="history.error.value" @retry="history.refetch()" />
    <template v-else>
      <section aria-label="History">
        <UTimeline :items="items" size="xs" color="neutral" :ui="{ wrapper: 'min-w-0 flex-1' }">
          <template #date="{ item }">
            <RelativeTime :value="item.entry.At" />
          </template>
          <template #title="{ item }">
            <USeparator
              v-if="item.entry.Action === 'Recreated'"
              label="New lifetime"
              class="mb-1"
              :ui="{ label: 'text-xs text-muted' }"
            />
            <span class="flex flex-wrap items-center gap-x-2 gap-y-1">
              <UBadge
                v-if="item.entry.Action"
                :label="actionLabel(item)"
                :color="color(item)"
                variant="subtle"
                size="sm"
              />
              <span class="text-sm text-muted">by <AuditActorLink :actor="item.entry.Actor" /></span>
              <span v-if="causeText(item.entry.Cause)" class="font-mono text-xs text-muted">{{
                causeText(item.entry.Cause)
              }}</span>
              <span v-if="item.entry.DescendantCount" class="text-xs text-muted">
                {{ item.entry.DescendantCount }} descendant{{ item.entry.DescendantCount === 1 ? '' : 's' }}
              </span>
            </span>
          </template>
          <template #description="{ item }">
            <ul v-if="auditChanges(item.entry).length" class="mt-1 space-y-1 text-sm">
              <li v-for="(change, index) in auditChanges(item.entry)" :key="index" class="min-w-0">
                <template v-if="change.kind === 'field'">
                  <span class="font-medium text-highlighted">{{ change.field }}</span
                  >:
                  <span class="line-clamp-2 break-all text-muted">{{ change.previous }} → {{ change.value }}</span>
                </template>
                <span v-else-if="change.kind === 'flag'" class="text-muted">{{ change.field }} changed</span>
                <span v-else-if="change.kind === 'labels'" class="flex flex-wrap gap-1">
                  <UBadge
                    v-for="label in change.added"
                    :key="`+${label}`"
                    :label="`+${label}`"
                    color="success"
                    variant="subtle"
                    size="sm"
                  />
                  <UBadge
                    v-for="label in change.removed"
                    :key="`-${label}`"
                    :label="`−${label}`"
                    color="error"
                    variant="subtle"
                    size="sm"
                  />
                </span>
                <span v-else-if="change.kind === 'value'" class="line-clamp-2 break-all text-muted">{{
                  change.text
                }}</span>
                <template v-else-if="change.kind === 'configuration'">
                  <RouterLink
                    v-if="item.entry.Action === 'ConfigurationChanged'"
                    :to="versionLink(change.version)"
                    class="text-primary hover:underline"
                    >Configuration saved as v{{ change.version }}</RouterLink
                  >
                  <span v-else class="text-muted">Configuration v{{ change.version }} deleted</span>
                </template>
              </li>
            </ul>
          </template>
        </UTimeline>
      </section>
      <p v-if="history.data.value?.IsTrimmed" class="text-xs text-muted">
        Older changes were removed to keep the history small.
      </p>
    </template>
  </div>
</template>
