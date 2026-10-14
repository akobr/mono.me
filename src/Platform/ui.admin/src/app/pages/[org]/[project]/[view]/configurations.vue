<script setup lang="ts">
import { computed, h, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMediaQuery } from '@vueuse/core'
import type { TableColumn } from '@nuxt/ui'
import type { ConfigurationSummary } from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import AnnotationTypeBadge from '@/components/annotations/AnnotationTypeBadge.vue'
import AuditActorLink from '@/components/common/AuditActorLink.vue'
import KeyText from '@/components/common/KeyText.vue'
import RelativeTime from '@/components/common/RelativeTime.vue'

interface Row {
  key: string
  name: string
  title?: string
  summary: ConfigurationSummary
}

// Configurations of the view (companion spec E6) with the same filter language as the annotations; the detail
// is a nested route, like the annotation grid. The scroll position is kept in the page memory of the view.
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

useScrollMemory(
  () => (desktop.value ? table.value?.$el : list.value),
  computed({
    get: () => memory.value.scrollTop,
    set: (value: number) => (memory.value.scrollTop = value),
  }),
  () => rows.value.length > 0,
)

const selectedKey = computed(() => (route.params as { key?: string }).key)
const base = computed(() => `/${context.org}/${context.project}/${context.view}/configurations`)

function open(key: string) {
  void router.push({ path: `${base.value}/${key}`, query: { q: route.query.q } })
}

const columns: TableColumn<Row>[] = [
  // The name of the type, as in the annotations grid (424-34); a custom name can be long, so the cell cannot grow.
  {
    id: 'type',
    header: 'Type',
    cell: ({ row }) => h(AnnotationTypeBadge, { type: row.original.key.slice(0, 3), variant: 'label' }),
    meta: { style: { th: 'width: 160px', td: 'width: 160px; max-width: 160px' } },
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
  row.original.key === selectedKey.value ? '[&>td]:bg-[color-mix(in_oklab,var(--ui-primary)_12%,var(--ui-bg))]' : ''
</script>

<template>
  <UDashboardPanel
    id="configurations"
    :class="{ hidden: detail.covers.value }"
    :ui="{ body: 'p-0 sm:p-0 gap-0 overflow-hidden' }"
  >
    <template #header>
      <AppNavbar title="Configurations" />
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
      <LoadingState v-if="!catalog.configurations && catalog.status !== 'error'" label="Loading configurations…" />
      <ErrorState
        v-else-if="catalog.status === 'error'"
        :error="catalog.error"
        @retry="context.scope && catalog.load(context.scope, { force: true })"
      />
      <EmptyState
        v-else-if="rows.length === 0"
        icon="i-lucide-settings-2"
        :title="filter.active.value ? 'No configuration matches the filter' : 'No configurations in this view'"
        description="Open an annotation and create its configuration in the Configuration tab."
      />
      <UTable
        v-else-if="desktop"
        ref="table"
        :data="rows"
        :columns="columns"
        :get-row-id="(row: Row) => row.key"
        :virtualize="{ estimateSize: 40 }"
        :meta="{ class: { tr: rowClass } }"
        sticky
        class="h-full"
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

  <RouterView />
</template>
