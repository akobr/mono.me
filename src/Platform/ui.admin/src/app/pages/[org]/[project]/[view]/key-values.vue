<script setup lang="ts">
import { computed, h, ref, resolveComponent, watch, type Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { useMediaQuery } from '@vueuse/core'
import type { TableColumn } from '@nuxt/ui'
import { deleteKeyValue, type KeyValueProtection, type KeyValueSummary } from '@42for.net/2splatform.sdk'
import { invalidateKeyValues } from '@/queries/invalidation'
import { useKeyValuePolicy, useKeyValues } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { bindingFor, daysUntil, isSecretLevel, levelMeta, levels, maskedValue, valuePreview } from '@/utils/key-values'
import KeyValueLevelBadge from '@/components/key-values/KeyValueLevelBadge.vue'
import AuditActorLink from '@/components/common/AuditActorLink.vue'
import RelativeTime from '@/components/common/RelativeTime.vue'

/**
 * The key/value pairs of the view (spec 29.2): search, a level filter, the Active or Deleted state, New, Import and
 * Export, row actions and bulk actions on the selection; the detail is a nested route.
 */
const route = useRoute()
const router = useRouter()
const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const confirm = useConfirm()
const desktop = useMediaQuery('(min-width: 768px)')
const canEdit = useCan('edit')
const canSecrets = useCan('secrets')
const canAdmin = useCan('manageAccess')

const state = computed<'Active' | 'Deleted'>(() => (route.query.state === 'Deleted' || route.query.state === 'All' ? 'Deleted' : 'Active'))
// 'all' stands for no level filter: a Select item must not have an empty-string value (reka-ui reserves it for "cleared").
const levelFilter = computed<KeyValueProtection | 'all'>(() =>
  typeof route.query.level === 'string' && levels.includes(route.query.level as KeyValueProtection) ? (route.query.level as KeyValueProtection) : 'all',
)
const search = ref(typeof route.query.q === 'string' ? route.query.q : '')
watch(
  () => route.query.q,
  (value) => (search.value = typeof value === 'string' ? value : ''),
)

const params = computed(() => ({
  state: state.value === 'Deleted' ? 'Deleted' : undefined,
  protection: levelFilter.value === 'all' ? undefined : levelFilter.value,
  includeValues: true,
}))
const list = useKeyValues(params)
const policy = useKeyValuePolicy()
const allowedLevels = computed(() => (policy.data.value?.AllowedProtections ?? ['Public', 'Standard']) as KeyValueProtection[])

const rows = computed<KeyValueSummary[]>(() => {
  const term = search.value.trim().toLowerCase()
  return (list.data.value?.Items ?? [])
    .filter((item) => !term || item.Key?.toLowerCase().includes(term) || (!isSecretLevel(item.Protection) && item.Value?.toLowerCase().includes(term)))
    .toSorted((a, b) => (a.Key ?? '').localeCompare(b.Key ?? ''))
})

const selected = ref<Set<string>>(new Set())
watch([state, () => context.view], () => (selected.value = new Set()))

function toggle(key: string, on: boolean) {
  const next = new Set(selected.value)
  if (on) {
    next.add(key)
  } else {
    next.delete(key)
  }
  selected.value = next
}

const allSelected = computed(() => rows.value.length > 0 && rows.value.every((row) => selected.value.has(row.Key ?? '')))

function toggleAll(on: boolean) {
  selected.value = on ? new Set(rows.value.map((row) => row.Key ?? '')) : new Set()
}

const selectedKey = computed(() => (route.params as { key?: string }).key)
const base = computed(() => `/${context.org}/${context.project}/${context.view}/key-values`)

function open(key: string) {
  void router.push({ path: `${base.value}/${key}`, query: route.query })
}

function setQuery(next: Record<string, string | undefined>) {
  void router.replace({ query: { ...route.query, ...next } })
}

const creating = ref(false)
const importing = ref(false)
const permanentTarget = ref<string>()
const busy = ref(false)

async function refresh() {
  if (context.scope) {
    await invalidateKeyValues(queryClient, context.scope)
  }
}

async function remove(key: string) {
  const confirmed = await confirm({
    title: `Delete '${key}'?`,
    description: 'The key can be restored for 90 days with every version; after that it is removed for good.',
    confirmLabel: 'Delete',
    color: 'error',
  })
  if (!confirmed || !context.scope) {
    return
  }
  try {
    await deleteKeyValue(context.scope.org, context.scope.project, context.scope.view, key)
    toast.add({ title: `'${key}' deleted`, description: 'Restore it from the Deleted keys.', color: 'success', icon: 'i-lucide-trash-2' })
    await refresh()
  } catch (error) {
    toast.add({ title: 'The key was not deleted', description: errorMessage(error), color: 'error' })
  }
}

async function removePermanently() {
  if (!context.scope || !permanentTarget.value) {
    return
  }
  busy.value = true
  try {
    await deleteKeyValue(context.scope.org, context.scope.project, context.scope.view, permanentTarget.value, { permanent: true })
    toast.add({ title: `'${permanentTarget.value}' removed for good`, color: 'success', icon: 'i-lucide-flame' })
    permanentTarget.value = undefined
    await refresh()
  } catch (error) {
    toast.add({ title: 'The key was not removed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

async function copy(text: string, title: string) {
  await navigator.clipboard.writeText(text)
  toast.add({ title, color: 'success', icon: 'i-lucide-copy' })
}

function rowActions(row: KeyValueSummary) {
  const key = row.Key ?? ''
  const secret = isSecretLevel(row.Protection)
  return [
    [
      { label: 'Open', icon: 'i-lucide-pencil', onSelect: () => open(key) },
      { label: 'Copy key', icon: 'i-lucide-copy', onSelect: () => copy(key, 'Key copied') },
      { label: 'Copy binding', icon: 'i-lucide-at-sign', onSelect: () => copy(bindingFor(key), `Binding ${bindingFor(key)} copied`) },
      ...(secret && canSecrets.value && !row.IsDeleted ? [{ label: 'Reveal', icon: 'i-lucide-eye', onSelect: () => open(key) }] : []),
    ],
    [
      ...(!row.IsDeleted && (secret ? canSecrets.value : canEdit.value) ? [{ label: 'Delete', icon: 'i-lucide-trash-2', color: 'error' as const, onSelect: () => remove(key) }] : []),
      ...(canAdmin.value ? [{ label: 'Delete permanently', icon: 'i-lucide-flame', color: 'error' as const, onSelect: () => (permanentTarget.value = key) }] : []),
    ],
  ].filter((group) => group.length)
}

const columns = computed<TableColumn<KeyValueSummary>[]>(() => [
  {
    id: 'select',
    header: () => h(resolveCheckbox(), { modelValue: allSelected.value, 'aria-label': 'Select all', 'onUpdate:modelValue': (value: boolean | 'indeterminate') => toggleAll(value === true) }),
    cell: ({ row }) =>
      h(resolveCheckbox(), {
        modelValue: selected.value.has(row.original.Key ?? ''),
        'aria-label': `Select ${row.original.Key}`,
        'onUpdate:modelValue': (value: boolean | 'indeterminate') => toggle(row.original.Key ?? '', value === true),
        onClick: (event: Event) => event.stopPropagation(),
      }),
    meta: { style: { th: 'width: 40px', td: 'width: 40px' } },
  },
  { id: 'key', header: 'Key', cell: ({ row }) => h('code', { class: 'font-mono text-xs text-highlighted' }, row.original.Key) },
  { id: 'level', header: 'Level', cell: ({ row }) => h(KeyValueLevelBadge, { level: row.original.Protection, reference: row.original.IsReference }) },
  {
    id: 'value',
    header: 'Value',
    cell: ({ row }) =>
      h('span', { class: 'font-mono text-xs text-muted' }, isSecretLevel(row.original.Protection) ? maskedValue : valuePreview(row.original.Value)),
  },
  { id: 'version', header: 'Version', cell: ({ row }) => h('span', { class: 'font-mono text-xs' }, `v${row.original.Version ?? '?'}`) },
  { id: 'author', header: 'Author', cell: ({ row }) => h(AuditActorLink, { actor: row.original.Modified?.By, subtle: true }) },
  {
    id: 'updated',
    header: state.value === 'Deleted' ? 'Expires' : 'Updated',
    cell: ({ row }) =>
      state.value === 'Deleted'
        ? h('span', { class: 'text-muted' }, `in ${daysUntil(row.original.ExpiresAt) ?? '?'} days`)
        : h(RelativeTime, { value: row.original.Modified?.At }),
  },
  {
    id: 'actions',
    header: '',
    cell: ({ row }) =>
      h(resolveDropdown(), { items: rowActions(row.original), content: { align: 'end' } }, () =>
        h(resolveButton(), { icon: 'i-lucide-ellipsis-vertical', color: 'neutral', variant: 'ghost', size: 'xs', 'aria-label': `Actions for ${row.original.Key}`, onClick: (event: Event) => event.stopPropagation() }),
      ),
    meta: { style: { th: 'width: 48px', td: 'width: 48px' } },
  },
])

// Nuxt UI components for the render functions of the columns; resolved loosely, as the cells pass DOM attributes too.
const resolveCheckbox = () => resolveComponent('UCheckbox') as Component
const resolveDropdown = () => resolveComponent('UDropdownMenu') as Component
const resolveButton = () => resolveComponent('UButton') as Component

const rowClass = (row: { original: KeyValueSummary }) =>
  row.original.Key === selectedKey.value ? '[&>td]:bg-[color-mix(in_oklab,var(--ui-primary)_12%,var(--ui-bg))]' : ''

const levelItems = computed(() => [{ label: 'All levels', value: 'all' }, ...levels.map((level) => ({ label: levelMeta[level].label, value: level }))])
</script>

<template>
  <UDashboardPanel id="key-values" :ui="{ body: 'p-0 sm:p-0 gap-0 overflow-hidden' }">
    <template #header>
      <AppNavbar title="Key values">
        <template #right>
          <KeyValueExportMenu />
          <!-- icon-only below sm, or the left side of the navbar (menu button, context) collapses on a phone -->
          <UButton v-if="canEdit" label="Import" aria-label="Import" icon="i-lucide-upload" color="neutral" variant="outline" :ui="{ label: 'hidden sm:inline' }" @click="importing = true" />
          <UButton v-if="canEdit" label="New key" aria-label="New key" icon="i-lucide-plus" :ui="{ label: 'hidden sm:inline' }" @click="creating = true" />
        </template>
      </AppNavbar>
      <UDashboardToolbar class="py-2">
        <div class="flex w-full flex-wrap items-center gap-2">
          <UInput
            :model-value="search"
            icon="i-lucide-search"
            placeholder="Search keys and public values"
            class="w-full sm:w-72"
            aria-label="Search"
            @update:model-value="(value) => ((search = String(value)), setQuery({ q: String(value) || undefined }))"
          />
          <USelect :model-value="levelFilter" :items="levelItems" class="w-40" aria-label="Level filter" @update:model-value="(value) => setQuery({ level: value === 'all' ? undefined : value })" />
          <UFieldGroup>
            <UButton label="Active" :color="state === 'Active' ? 'primary' : 'neutral'" :variant="state === 'Active' ? 'solid' : 'outline'" size="sm" @click="setQuery({ state: undefined })" />
            <UButton label="Deleted" :color="state === 'Deleted' ? 'primary' : 'neutral'" :variant="state === 'Deleted' ? 'solid' : 'outline'" size="sm" @click="setQuery({ state: 'Deleted' })" />
          </UFieldGroup>
          <span class="ms-auto text-xs text-muted">{{ rows.length }} of {{ list.data.value?.Items?.length ?? 0 }}</span>
        </div>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ByokBanner />
      <KeyValueBulkBar
        v-if="selected.size"
        :keys="[...selected]"
        :state="state"
        :allowed-levels="allowedLevels"
        @done="(failed) => ((selected = new Set(failed)), refresh())"
      />
      <LoadingState v-if="list.isPending.value" label="Loading key values…" />
      <ErrorState v-else-if="list.isError.value" :error="list.error.value" @retry="list.refetch()" />
      <EmptyState
        v-else-if="rows.length === 0"
        icon="i-lucide-key-round"
        :title="search ? 'No key matches the search' : state === 'Deleted' ? 'No deleted keys' : 'No key values in this view'"
        :description="state === 'Deleted' ? 'Deleted keys can be restored here for 90 days.' : 'Key values are the configuration values and secrets that bindings (@key) insert into configurations.'"
      >
        <UButton v-if="canEdit && state === 'Active' && !search" label="New key" icon="i-lucide-plus" @click="creating = true" />
      </EmptyState>
      <UTable
        v-else-if="desktop"
        :data="rows"
        :columns="columns"
        :get-row-id="(row: KeyValueSummary) => row.Key ?? ''"
        :meta="{ class: { tr: rowClass } }"
        sticky
        class="h-full"
        :ui="{ td: 'py-2 text-sm', th: 'py-2 text-xs', tr: 'cursor-pointer' }"
        :on-select="(_event: Event, row: { original: KeyValueSummary }) => open(row.original.Key ?? '')"
      />
      <ul v-else class="divide-y divide-default overflow-auto">
        <li v-for="row in rows" :key="row.Key">
          <button type="button" class="flex w-full flex-col gap-1 px-4 py-3 text-start" :aria-current="row.Key === selectedKey ? 'true' : undefined" @click="open(row.Key ?? '')">
            <span class="flex items-center gap-2">
              <code class="min-w-0 truncate font-mono text-sm font-medium">{{ row.Key }}</code>
              <KeyValueLevelBadge :level="row.Protection" :reference="row.IsReference" class="ms-auto" />
            </span>
            <span class="truncate font-mono text-xs text-muted">{{ isSecretLevel(row.Protection) ? maskedValue : valuePreview(row.Value) }} · v{{ row.Version }}</span>
          </button>
        </li>
      </ul>
    </template>
  </UDashboardPanel>

  <RouterView />

  <KeyValueCreateDialog v-model:open="creating" @created="(pair) => (refresh(), open(pair.Key ?? ''))" />
  <KeyValueImportDialog v-model:open="importing" @imported="refresh" />
  <ConfirmDialog
    :open="!!permanentTarget"
    title="Delete permanently?"
    :description="`'${permanentTarget}' and every version of it are removed for good; nothing can restore it.`"
    :confirm-text="permanentTarget"
    confirm-label="Delete permanently"
    :loading="busy"
    @update:open="(value) => !value && (permanentTarget = undefined)"
    @confirm="removePermanently"
  />
</template>
