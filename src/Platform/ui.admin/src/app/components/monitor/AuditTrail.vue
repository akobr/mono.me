<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { getKeyValueAudit, type KeyValueAuditEvent } from '@42for.net/2splatform.sdk'
import { useKeyValueAudit } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

// The secret audit trail of the project (spec 29.7), the Audit tab of Monitor (424-35): filters, paging, and the
// correlation id to match vault logs. The filters sit in a toolbar above the table, which scrolls with a sticky header.
const context = useContextStore()
const toast = useToast()
const canAdmin = useCan('manageAccess')

const operations = [
  'Reveal',
  'Export',
  'BindingRead',
  'Create',
  'Update',
  'Delete',
  'Restore',
  'PermanentDelete',
  'Purge',
  'Reprotect',
  'Import',
  'PolicyChange',
  'Rotate',
  'Rewrap',
  'ByokVerify',
  'ByokUnavailable',
  'RecipientAdded',
  'RecipientChanged',
  'RecipientRemoved',
]

const view = ref('')
const key = ref('')
// 'all' stands for every operation: a Select item must not have an empty-string value.
const operation = ref('all')
const from = ref('')
const to = ref('')

const params = computed(() => ({
  view: view.value.trim() || undefined,
  key: key.value.trim() || undefined,
  operation: operation.value === 'all' ? undefined : operation.value,
  from: from.value ? new Date(from.value).toISOString() : undefined,
  to: to.value ? new Date(new Date(to.value).getTime() + 86_400_000).toISOString() : undefined,
}))

const first = useKeyValueAudit(params, canAdmin)
const more = ref<KeyValueAuditEvent[]>([])
const nextToken = ref<string | null>()
const loadingMore = ref(false)
// A page of more events belongs to the project and filters it was asked for: changing them starts a new generation,
// and a page that arrives for an older one is dropped.
let generation = 0

watch([params, () => context.org, () => context.project], () => {
  generation++
  more.value = []
  nextToken.value = undefined
})

watch(
  () => first.data.value,
  (page) => {
    more.value = []
    nextToken.value = page?.ContinuationToken
  },
  { immediate: true },
)

const events = computed(() => [...(first.data.value?.Items ?? []), ...more.value])

async function loadMore() {
  if (!context.org || !context.project || !nextToken.value) {
    return
  }
  const started = generation
  loadingMore.value = true
  try {
    const page = await getKeyValueAudit(context.org, context.project, {
      ...params.value,
      continuationToken: nextToken.value,
    })
    if (started === generation) {
      more.value = [...more.value, ...(page.Items ?? [])]
      nextToken.value = page.ContinuationToken
    }
  } catch (error) {
    if (started === generation) {
      toast.add({ title: 'The next page did not load', description: errorMessage(error), color: 'error' })
    }
  } finally {
    loadingMore.value = false
  }
}

const operationItems = [
  { label: 'Every operation', value: 'all' },
  ...operations.map((entry) => ({ label: entry, value: entry })),
]
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <UDashboardToolbar class="py-2">
      <div class="flex w-full flex-wrap items-center gap-2">
        <UInput v-model="view" placeholder="View" class="w-32 font-mono" aria-label="View filter" />
        <UInput v-model="key" placeholder="Key" class="w-48 font-mono" aria-label="Key filter" />
        <USelect v-model="operation" :items="operationItems" class="w-44" aria-label="Operation filter" />
        <UInput v-model="from" type="date" class="w-40" aria-label="From" />
        <UInput v-model="to" type="date" class="w-40" aria-label="To" />
        <span class="ms-auto text-xs text-muted">{{ events.length }} event(s)</span>
      </div>
    </UDashboardToolbar>
    <LoadingState v-if="first.isPending.value" />
    <ErrorState v-else-if="first.isError.value" :error="first.error.value" @retry="first.refetch()" />
    <EmptyState
      v-else-if="events.length === 0"
      icon="i-lucide-scroll-text"
      title="No audit events"
      description="Reveals, writes, exports, policy and key changes of secrets are recorded here for 400 days."
    />
    <div v-else class="min-h-0 flex-1 overflow-auto">
      <table class="w-full text-sm">
        <thead class="sticky top-0 bg-elevated/80 text-xs text-muted backdrop-blur">
          <tr>
            <th class="px-3 py-2 text-start font-medium">Time</th>
            <th class="px-3 py-2 text-start font-medium">Operation</th>
            <th class="px-3 py-2 text-start font-medium">View</th>
            <th class="px-3 py-2 text-start font-medium">Key</th>
            <th class="px-3 py-2 text-start font-medium">Version</th>
            <th class="px-3 py-2 text-start font-medium">Level</th>
            <th class="px-3 py-2 text-start font-medium">Actor</th>
            <th class="px-3 py-2 text-start font-medium">Channel</th>
            <th class="px-3 py-2 text-start font-medium">Count</th>
            <th class="px-3 py-2 text-start font-medium">Correlation</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-default">
          <tr v-for="event in events" :key="event.Id">
            <td class="px-3 py-2 whitespace-nowrap">{{ event.Time ? formatDateTime(new Date(event.Time)) : '' }}</td>
            <td class="px-3 py-2">
              <span class="font-medium">{{ event.Operation }}</span>
              <span v-if="event.Detail" class="block text-xs text-muted">{{ event.Detail }}</span>
            </td>
            <td class="px-3 py-2 font-mono text-xs">{{ event.View ?? '' }}</td>
            <td class="px-3 py-2 font-mono text-xs">{{ event.Key ?? '' }}</td>
            <td class="px-3 py-2 font-mono text-xs">{{ event.Version ? `v${event.Version}` : '' }}</td>
            <td class="px-3 py-2"><KeyValueLevelBadge v-if="event.Protection" :level="event.Protection" /></td>
            <td class="px-3 py-2 text-xs"><AuditActorLink :actor="event.Actor" /></td>
            <td class="px-3 py-2 text-xs text-muted">{{ event.Channel }}</td>
            <td class="px-3 py-2 text-end font-mono text-xs">{{ event.Count ?? 1 }}</td>
            <td class="px-3 py-2">
              <span v-if="event.CorrelationId" class="inline-flex items-center gap-1 font-mono text-xs text-muted">
                {{ event.CorrelationId.slice(0, 8) }}…
                <CopyButton :value="event.CorrelationId" />
              </span>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-if="nextToken" class="flex justify-center p-4">
        <UButton label="Load more" color="neutral" variant="outline" :loading="loadingMore" @click="loadMore" />
      </div>
    </div>
  </div>
</template>
