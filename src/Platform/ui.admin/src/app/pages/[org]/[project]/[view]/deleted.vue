<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { useNow } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'
import { collectAll, getDeletedAnnotations, type Annotation } from '@42for.net/2splatform.sdk'
import { getGetDeletedAnnotationsQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { formatDate, formatRelative } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

// The deleted annotations of the view (spec 4.5): the roots of deletions, newest first, or every deleted annotation
// with "Show cascaded". Contributors restore them (with or without descendants), Administrators delete them
// permanently. The rest is purged automatically (PurgeAt).
const route = useRoute()
const router = useRouter()
const context = useContextStore()
const actions = useAnnotationActions()
const canEdit = useCan('edit')
const canPurge = useCan('manageAccess')
const toast = useToast()
const now = useNow({ interval: 60_000 })

const showCascaded = computed({
  get: () => route.query.cascaded === '1',
  set: (value: boolean) => void router.replace({ query: { ...route.query, cascaded: value ? '1' : undefined } }),
})

const scope = {
  org: () => context.org ?? '',
  project: () => context.project ?? '',
  view: () => context.view ?? '',
}

const deleted = useQuery({
  queryKey: computed(() =>
    getGetDeletedAnnotationsQueryKey(scope.org, scope.project, scope.view, { roots: !showCascaded.value }),
  ),
  queryFn: ({ signal }) =>
    collectAll(
      (token, pageSignal) =>
        getDeletedAnnotations(
          scope.org(),
          scope.project(),
          scope.view(),
          { roots: !showCascaded.value, continuationToken: token },
          { signal: pageSignal ?? signal },
        ),
      (page) => page.Annotations,
      signal,
    ),
  enabled: () => !!context.scope,
  meta: { silent: true },
})

const rows = computed(() => deleted.data.value ?? [])
const selected = ref(new Set<string>())
const busy = ref(false)
const purging = ref<Annotation[]>([])
const purgeOpen = computed({
  get: () => purging.value.length > 0,
  set: (value: boolean) => !value && (purging.value = []),
})
const detail = ref<Annotation>()
const detailOpen = computed({
  get: () => !!detail.value,
  set: (value: boolean) => !value && (detail.value = undefined),
})

watch(
  () => [context.scope, showCascaded.value],
  () => (selected.value = new Set()),
)

const allSelected = computed(
  () => rows.value.length > 0 && rows.value.every((row) => selected.value.has(row.AnnotationKey)),
)
const formatter = new Intl.NumberFormat()

function toggle(key: string, value: boolean) {
  const next = new Set(selected.value)
  if (value) {
    next.add(key)
  } else {
    next.delete(key)
  }
  selected.value = next
}

function toggleAll(value: boolean) {
  selected.value = value ? new Set(rows.value.map((row) => row.AnnotationKey)) : new Set()
}

const daysLeft = (annotation: Annotation) =>
  annotation.Deletion?.PurgeAt
    ? (Date.parse(annotation.Deletion.PurgeAt) - now.value.getTime()) / 86_400_000
    : undefined

async function restore(keys: string[], descendants = true) {
  busy.value = true
  try {
    const outcome = await actions.restoreMany(keys, { descendants })
    toast.add(
      outcome.failed.length
        ? {
            title: `${outcome.failed.length} of ${keys.length} were not restored`,
            description: outcome.failed
              .slice(0, 3)
              .map((failure) => `${failure.Key}: ${failure.Message}`)
              .join(' '),
            color: 'warning',
          }
        : {
            title: keys.length === 1 ? `${keys[0]} restored` : `${formatter.format(keys.length)} annotations restored`,
            description: outcome.created.length
              ? `The missing ancestors were created: ${outcome.created.slice(0, 3).join(', ')}`
              : undefined,
            color: 'success',
            icon: 'i-lucide-archive-restore',
          },
    )
    selected.value = new Set()
  } catch (error) {
    toast.add({ title: 'Restore failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

const purgeDescription = computed(() => {
  if (purging.value.length !== 1) {
    return `The ${formatter.format(purging.value.length)} annotations, their descendants and their change history are removed for good. Their configuration versions stay until they expire.`
  }
  const count = purging.value[0]!.Deletion?.DescendantCount ?? 0
  return count
    ? `The annotation, its ${count} descendant${count === 1 ? '' : 's'} and their change history are removed for good. Their configuration versions stay until they expire.`
    : 'The annotation and its change history are removed for good. Its configuration versions stay until they expire.'
})

async function purge() {
  const keys = purging.value.map((annotation) => annotation.AnnotationKey)
  busy.value = true
  try {
    const failed = await actions.purgeMany(keys)
    purging.value = []
    selected.value = new Set()
    toast.add(
      failed.length
        ? {
            title: `${failed.length} could not be deleted permanently`,
            description: failed.slice(0, 5).join(', '),
            color: 'warning',
          }
        : {
            title:
              keys.length === 1
                ? `${keys[0]} deleted permanently`
                : `${formatter.format(keys.length)} annotations deleted permanently`,
            color: 'success',
            icon: 'i-lucide-trash-2',
          },
    )
  } catch (error) {
    toast.add({ title: 'Delete failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

const rowMenu = (annotation: Annotation): DropdownMenuItem[][] => [
  [{ label: 'Show details', icon: 'i-lucide-panel-right-open', onSelect: () => (detail.value = annotation) }],
  ...(canEdit.value
    ? [
        [
          {
            label: 'Restore',
            icon: 'i-lucide-archive-restore',
            onSelect: () => void restore([annotation.AnnotationKey]),
          },
          {
            label: 'Restore without descendants',
            icon: 'i-lucide-archive-restore',
            onSelect: () => void restore([annotation.AnnotationKey], false),
          },
        ],
      ]
    : []),
  ...(canPurge.value
    ? [
        [
          {
            label: 'Delete permanently',
            icon: 'i-lucide-trash-2',
            color: 'error' as const,
            onSelect: () => (purging.value = [annotation]),
          },
        ],
      ]
    : []),
]

const selectedRows = computed(() => rows.value.filter((row) => selected.value.has(row.AnnotationKey)))
</script>

<template>
  <UDashboardPanel id="deleted">
    <template #header>
      <AppNavbar title="Deleted" />
      <UDashboardToolbar class="py-2">
        <div class="flex w-full flex-wrap items-center gap-3">
          <USwitch
            v-model="showCascaded"
            label="Show cascaded"
            description="Every deleted annotation, not only the keys a delete was requested for."
            size="sm"
          />
          <span v-if="deleted.data.value" class="ms-auto text-xs text-muted"
            >{{ formatter.format(rows.length) }} shown</span
          >
        </div>
      </UDashboardToolbar>
      <div
        v-if="selected.size > 0"
        class="flex flex-wrap items-center gap-2 border-b border-default bg-elevated/50 px-4 py-2 text-sm"
        role="toolbar"
        aria-label="Bulk actions"
      >
        <span class="font-medium">{{ formatter.format(selected.size) }} selected</span>
        <UButton
          v-if="canEdit"
          label="Restore"
          icon="i-lucide-archive-restore"
          size="xs"
          variant="outline"
          :disabled="busy"
          @click="restore([...selected])"
        />
        <UButton
          v-if="canPurge"
          label="Delete permanently"
          icon="i-lucide-trash-2"
          size="xs"
          color="error"
          variant="outline"
          :disabled="busy"
          @click="purging = selectedRows"
        />
        <UProgress
          v-if="actions.progress.value"
          class="w-32"
          size="sm"
          :model-value="actions.progress.value.done"
          :max="actions.progress.value.total"
        />
        <UButton
          class="ms-auto"
          label="Clear selection"
          size="xs"
          color="neutral"
          variant="ghost"
          @click="toggleAll(false)"
        />
      </div>
    </template>

    <template #body>
      <LoadingState v-if="deleted.isPending.value" label="Loading the deleted annotations…" />
      <ErrorState v-else-if="deleted.isError.value" :error="deleted.error.value" @retry="deleted.refetch()" />
      <EmptyState
        v-else-if="rows.length === 0"
        icon="i-lucide-archive-restore"
        title="Nothing is deleted"
        description="Deleted annotations stay here with their descendants and configuration until they are restored or purged."
      />
      <div v-else class="overflow-x-auto rounded-md border border-default">
        <table class="w-full text-sm">
          <thead class="bg-elevated/50 text-xs text-muted">
            <tr>
              <th class="w-8 px-3 py-2">
                <UCheckbox
                  :model-value="allSelected"
                  aria-label="Select all"
                  @update:model-value="(value) => toggleAll(value === true)"
                />
              </th>
              <th class="px-3 py-2 text-start font-medium">Key</th>
              <th class="hidden px-3 py-2 text-start font-medium md:table-cell">Title</th>
              <th v-if="showCascaded" class="px-3 py-2 text-start font-medium">Root</th>
              <th class="px-3 py-2 text-start font-medium">Deleted</th>
              <th class="px-3 py-2 text-start font-medium">By</th>
              <th class="hidden px-3 py-2 text-end font-medium md:table-cell">Descendants</th>
              <th class="px-3 py-2 text-start font-medium">Purged</th>
              <th class="w-10 px-3 py-2"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-default">
            <tr v-for="annotation in rows" :key="annotation.AnnotationKey">
              <td class="px-3 py-2">
                <UCheckbox
                  :model-value="selected.has(annotation.AnnotationKey)"
                  :aria-label="`Select ${annotation.AnnotationKey}`"
                  @update:model-value="(value) => toggle(annotation.AnnotationKey, value === true)"
                />
              </td>
              <td class="px-3 py-2">
                <span class="flex min-w-0 items-center gap-2">
                  <AnnotationTypeBadge :type="annotation.AnnotationKey.slice(0, 3)" variant="label" />
                  <button
                    type="button"
                    class="min-w-0 text-start hover:underline"
                    :aria-label="`Show ${annotation.AnnotationKey}`"
                    @click="detail = annotation"
                  >
                    <KeyText :value="annotation.AnnotationKey" />
                  </button>
                </span>
              </td>
              <td class="hidden max-w-64 truncate px-3 py-2 text-muted md:table-cell">{{ annotation.Title ?? '' }}</td>
              <td v-if="showCascaded" class="px-3 py-2">
                <UBadge v-if="annotation.Deletion?.IsRoot" label="root" color="neutral" variant="outline" size="sm" />
                <KeyText v-else-if="annotation.Deletion?.RootKey" :value="annotation.Deletion.RootKey" />
              </td>
              <td class="px-3 py-2 text-xs"><RelativeTime :value="annotation.Deletion?.DeletedAt" /></td>
              <td class="px-3 py-2 text-xs"><AuditActorLink :actor="annotation.Deletion?.DeletedBy" /></td>
              <td class="hidden px-3 py-2 text-end text-xs tabular-nums md:table-cell">
                {{ annotation.Deletion?.DescendantCount ?? '' }}
              </td>
              <td class="px-3 py-2 text-xs">
                <UTooltip v-if="annotation.Deletion?.PurgeAt" :text="formatDate(new Date(annotation.Deletion.PurgeAt))">
                  <span :class="(daysLeft(annotation) ?? 99) < 3 ? 'font-medium text-error' : undefined">{{
                    formatRelative(new Date(annotation.Deletion.PurgeAt), now)
                  }}</span>
                </UTooltip>
              </td>
              <td class="px-3 py-2 text-end">
                <UDropdownMenu :items="rowMenu(annotation)" :content="{ align: 'end' }">
                  <UButton
                    icon="i-lucide-ellipsis"
                    color="neutral"
                    variant="ghost"
                    size="sm"
                    :aria-label="`Actions for ${annotation.AnnotationKey}`"
                    :disabled="busy"
                  />
                </UDropdownMenu>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <USlideover
        v-model:open="detailOpen"
        :title="detail?.AnnotationKey ?? ''"
        description="The deleted annotation, read-only, and its history."
        :ui="{ content: 'max-w-xl' }"
      >
        <template #body>
          <div v-if="detail" class="flex flex-col gap-4">
            <pre
              class="max-h-80 overflow-auto rounded-md border border-default bg-muted p-3 font-mono text-xs"
              aria-label="Annotation document"
              >{{ JSON.stringify(detail, null, 2) }}</pre>
            <AnnotationHistory :annotation-key="detail.AnnotationKey" class="-mx-4" @restored="detail = undefined" />
          </div>
        </template>
      </USlideover>

      <ConfirmDialog
        v-model:open="purgeOpen"
        :title="
          purging.length === 1
            ? `Delete ${purging[0]?.AnnotationKey} permanently?`
            : `Delete ${formatter.format(purging.length)} annotations permanently?`
        "
        :description="purgeDescription"
        :confirm-text="purging.length === 1 ? purging[0]?.AnnotationKey : String(purging.length)"
        confirm-label="Delete permanently"
        :loading="busy"
        @confirm="purge"
      />
    </template>
  </UDashboardPanel>
</template>
