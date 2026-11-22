<script setup lang="ts">
import { computed, h, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMediaQuery } from '@vueuse/core'
import type { RowSelectionState } from '@tanstack/vue-table'
import type { TableColumn } from '@nuxt/ui'
import type { ConfigurationSummary } from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import AnnotationTypeBadge from '@/components/annotations/AnnotationTypeBadge.vue'
import RowSelect from '@/components/annotations/grid/RowSelect.vue'
import AuditActorLink from '@/components/common/AuditActorLink.vue'
import KeyText from '@/components/common/KeyText.vue'
import RelativeTime from '@/components/common/RelativeTime.vue'
import { mergeShownSelection, selectedKeysOf, shownSelection } from '@/utils/row-selection'

interface Row {
  key: string
  name: string
  title?: string
  summary: ConfigurationSummary
}

// Configurations of the view (companion spec E6) with the same filter language as the annotations; the detail
// is a nested route, like the annotation grid. The selection of the rows (spec "Copy and Compare Annotations and
// Configurations Between Views", section 8) and the scroll position are kept in the page memory of the view.
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const filter = useAnnotationFilter()
const desktop = useMediaQuery('(min-width: 768px)')
const detail = useDetailPanel()
const memory = usePageMemory('configurations')
const table = useTemplateRef<{ $el: HTMLElement }>('table')
const list = useTemplateRef<HTMLElement>('list')

watch(
  () => context.scope,
  (scope) => scope && void catalog.load(scope),
  { immediate: true },
)

const rows = computed<Row[]>(() => {
  const configurations = catalog.configurations
  if (!configurations) {
    return []
  }
  return [...configurations.values()]
    .filter((summary) => {
      const annotation = summary.AnnotationKey ? catalog.byKey.get(summary.AnnotationKey) : undefined
      return !filter.active.value || (annotation ? filter.predicate.value(annotation) : false)
    })
    .map((summary) => {
      const annotation = catalog.byKey.get(summary.AnnotationKey!)
      return {
        key: summary.AnnotationKey!,
        name: annotation?.Name ?? summary.AnnotationKey!.split('.').at(-1)!,
        title: annotation?.Title,
        summary,
      }
    })
    .toSorted((a, b) => a.key.localeCompare(b.key))
})

// The selection is keyed by annotation key, as on Annotations.
const selection = computed<RowSelectionState>({
  get: () => memory.value.selection,
  set: (value) => (memory.value.selection = value),
})
const shownKeys = computed(() => rows.value.map((row) => row.key))
// The table gets only the selected keys of its rows: it counts every key of its selection for the header checkbox,
// which would be wrong under a filter. The keys that the filter hides stay selected.
const tableSelection = computed<RowSelectionState>({
  get: () => shownSelection(selection.value, shownKeys.value),
  set: (next) => (selection.value = mergeShownSelection(selection.value, shownKeys.value, next)),
})
// A remembered selection may name configurations that are gone meanwhile; those never reach a bulk action.
const selectedKeys = computed(() => selectedKeysOf(selection.value).filter((key) => catalog.configurations?.has(key)))

useScrollMemory(
  () => (desktop.value ? table.value?.$el : list.value),
  computed({
    get: () => memory.value.scrollTop,
    set: (value: number) => (memory.value.scrollTop = value),
  }),
  () => rows.value.length > 0,
)

// New (424-42) for Contributors.
const canEdit = useCan('edit')
const creating = ref(false)

const selectedKey = computed(() => (route.params as { key?: string }).key)
const base = computed(() => `/${context.org}/${context.project}/${context.view}/configurations`)

function open(key: string) {
  void router.push({ path: `${base.value}/${key}`, query: { q: route.query.q } })
}

// The cells of the checkbox and of the type stay on the page background, as the pinned ones of the annotation grid:
// the tint of a selected row under the colored type badge would take its contrast.
const opaque = 'bg-default'

const columns: TableColumn<Row>[] = [
  // The checkboxes of the annotation grid. A click on one does not open the detail: the table leaves a click on a
  // control to the control.
  {
    id: 'select',
    header: ({ table: api }) =>
      h(RowSelect, {
        checked: api.getIsAllRowsSelected() ? true : api.getIsSomeRowsSelected() ? 'indeterminate' : false,
        label: 'Select all rows',
        'onUpdate:checked': (value: boolean) => api.toggleAllRowsSelected(value),
      }),
    cell: ({ row }) =>
      h(RowSelect, {
        checked: row.getIsSelected(),
        label: `Select ${row.original.key}`,
        'onUpdate:checked': (value: boolean) => row.toggleSelected(value),
      }),
    meta: { class: { td: opaque }, style: { th: 'width: 40px', td: 'width: 40px' } },
  },
  // The name of the type, as in the annotations grid (424-34); a custom name can be long, so the cell cannot grow.
  {
    id: 'type',
    header: 'Type',
    cell: ({ row }) => h(AnnotationTypeBadge, { type: row.original.key.slice(0, 3), variant: 'label' }),
    meta: { class: { td: opaque }, style: { th: 'width: 160px', td: 'width: 160px; max-width: 160px' } },
  },
  {
    id: 'name',
    header: 'Name',
    cell: ({ row }) => h('span', { class: 'font-medium text-highlighted' }, row.original.name),
  },
  { id: 'key', header: 'Key', cell: ({ row }) => h(KeyText, { value: row.original.key }) },
  {
    id: 'version',
    header: 'Version',
    cell: ({ row }) => h('span', { class: 'font-mono text-xs' }, `v${row.original.summary.Version ?? '?'}`),
  },
  {
    id: 'author',
    header: 'Author',
    cell: ({ row }) =>
      row.original.summary.Modified ? h(AuditActorLink, { actor: row.original.summary.Modified.By, subtle: true }) : '',
  },
  { id: 'updated', header: 'Updated', cell: ({ row }) => h(RelativeTime, { value: row.original.summary.Modified?.At }) },
]

const rowClass = (row: { original: Row }) =>
  row.original.key === selectedKey.value ? '[&>td]:bg-[color-mix(in_oklab,var(--ui-primary)_10%,var(--ui-bg))]' : ''
</script>

<template>
  <UDashboardPanel
    id="configurations"
    :class="{ hidden: detail.covers.value }"
    :ui="{ body: 'p-0 sm:p-0 gap-0 sm:gap-0 overflow-hidden' }"
  >
    <template #header>
      <AppNavbar title="Configurations">
        <template #right>
          <!-- The main action, without a tooltip as on the other pages; labelled from 768 px. -->
          <UButton
            v-if="canEdit"
            icon="i-lucide-plus"
            :label="desktop ? 'New' : undefined"
            aria-label="New configuration"
            @click="creating = true"
          />
        </template>
      </AppNavbar>
      <UDashboardToolbar class="py-2">
        <AnnotationFilterBar
          :query="filter.query.value"
          :parsed="filter.parsed.value"
          mode="grid"
          perspective="subject"
          :shown="rows.length"
          :total="catalog.configurations?.size ?? 0"
          compact
          @update:query="(value) => (filter.query.value = value)"
        />
      </UDashboardToolbar>
    </template>

    <template #body>
      <ConfigurationBulkBar v-if="selectedKeys.length && canEdit" :keys="selectedKeys" @clear="selection = {}" />

      <LoadingState
        v-if="!catalog.configurations && catalog.status !== 'error' && !catalog.configurationsError"
        label="Loading configurations…"
      />
      <ErrorState
        v-else-if="catalog.status === 'error'"
        :error="catalog.error"
        @retry="context.scope && catalog.load(context.scope, { force: true })"
      />
      <!-- The list of the summaries failed and none are known: its error, not "Loading configurations…" forever. -->
      <ErrorState
        v-else-if="!catalog.configurations"
        :error="catalog.configurationsError"
        title="The configurations could not be loaded"
        @retry="catalog.refreshConfigurations()"
      />
      <EmptyState
        v-else-if="rows.length === 0"
        icon="i-lucide-settings-2"
        :title="filter.active.value ? 'No configuration matches the filter' : 'No configurations in this view'"
        :description="
          canEdit ? 'Create one with New, or from the configuration link in the header of an annotation.' : undefined
        "
      >
        <template v-if="canEdit" #default>
          <UButton label="New configuration" icon="i-lucide-plus" @click="creating = true" />
        </template>
      </EmptyState>
      <!-- min-h-0 flex-1, as the annotation grid: the bulk bar above must not push the last row out. -->
      <UTable
        v-else-if="desktop"
        ref="table"
        v-model:row-selection="tableSelection"
        :data="rows"
        :columns="columns"
        :get-row-id="(row: Row) => row.key"
        :virtualize="{ estimateSize: 40 }"
        :meta="{ class: { tr: rowClass } }"
        sticky
        class="min-h-0 flex-1"
        :ui="{ td: 'py-2 text-sm', th: 'py-2 text-xs', tr: 'cursor-pointer' }"
        :on-select="(_event: Event, row: { original: Row }) => open(row.original.key)"
      />
      <ul v-else ref="list" class="divide-y divide-default overflow-auto">
        <li v-for="row in rows" :key="row.key">
          <button type="button" class="flex w-full flex-col gap-1 px-4 py-3 text-start" @click="open(row.key)">
            <span class="flex items-center gap-2">
              <AnnotationTypeBadge :type="row.key.slice(0, 3)" variant="icon" />
              <span class="font-medium">{{ row.name }}</span>
              <span class="ms-auto font-mono text-xs text-muted">v{{ row.summary.Version }}</span>
            </span>
            <span class="truncate font-mono text-xs text-muted">{{ row.key }}</span>
          </button>
        </li>
      </ul>
    </template>
  </UDashboardPanel>

  <ConfigurationCreateDialog v-if="canEdit" v-model:open="creating" @picked="open" />

  <RouterView />
</template>
