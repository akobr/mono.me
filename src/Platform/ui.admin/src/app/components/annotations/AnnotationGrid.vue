<script setup lang="ts">
import { computed, ref, useTemplateRef, watch, watchEffect } from 'vue'
import type { ColumnSizingState, RowSelectionState, SortingState, VisibilityState } from '@tanstack/vue-table'
import type { ConfigurationSummary } from '@42for.net/2splatform.sdk'
import { useNow } from '@vueuse/core'
import { usePreferencesStore } from '@/stores/preferences'
import { keySelectionOf, rowSelectionOf, type TreeNode } from '@/utils/tree-projection'
import { annotationColumns, defaultHiddenColumns } from './grid/annotation-columns'

/**
 * Annotation grid and tree (spec section 8) on Nuxt UI UTable (TanStack Table) with virtual rows. In tree mode
 * the rows are the flattened visible tree, where an annotation can have a row per path; sorting applies to the
 * grid mode only.
 */
const props = defineProps<{
  rows: TreeNode[]
  tree: boolean
  selectedKey?: string
  activeId?: string
  configurations: ReadonlyMap<string, ConfigurationSummary> | null
  isExpanded: (id: string) => boolean
}>()

const emit = defineEmits<{ open: [key: string, id: string]; toggle: [id: string] }>()
const rowSelection = defineModel<RowSelectionState>('rowSelection', { default: () => ({}) })

// The model is keyed by annotation key and UTable by row id, so every copy of a selected annotation is selected.
const tableSelection = computed<RowSelectionState>({
  get: () => rowSelectionOf(props.rows, rowSelection.value),
  set: (next) => (rowSelection.value = keySelectionOf(props.rows, rowSelection.value, tableSelection.value, next)),
})

const preferences = usePreferencesStore()
const gridPreferences = preferences.grid('annotations')
const now = useNow({ interval: 60_000 })
const table = useTemplateRef<{ $el: HTMLElement; tableRef: HTMLTableElement | null }>('table')

const sorting = ref<SortingState>([])
const columnVisibility = ref<VisibilityState>(
  gridPreferences.columnVisibility ?? Object.fromEntries(defaultHiddenColumns.map((id) => [id, false])),
)
const columnSizing = ref<ColumnSizingState>(gridPreferences.columnSizing ?? {})
const columnPinning = ref({ left: ['select', 'type', 'name'], right: [] as string[] })

watch(columnVisibility, (value) => (gridPreferences.columnVisibility = value), { deep: true })
watch(columnSizing, (value) => (gridPreferences.columnSizing = value), { deep: true })

const rowHeight = computed(() => (preferences.state.density === 'compact' ? 36 : 44))

const columns = computed(() =>
  annotationColumns({
    tree: props.tree,
    now: now.value.getTime(),
    configurations: props.configurations,
    isExpanded: props.isExpanded,
    toggle: (id) => emit('toggle', id),
  }),
)

const rowClass = (row: { original: TreeNode }) =>
  [
    // Opaque, so pinned (sticky) cells do not show the scrolled content through the highlight.
    row.original.key === props.selectedKey ? '[&>td]:bg-[color-mix(in_oklab,var(--ui-primary)_12%,var(--ui-bg))]' : '',
    row.original.id === props.activeId ? 'outline outline-1 -outline-offset-1 outline-primary/60' : '',
    row.original.matched || row.original.missing ? '' : 'opacity-50',
  ].join(' ')

/** Scrolls a row into view (keyboard navigation, focus return). */
function scrollToId(id: string): void {
  const index = props.rows.findIndex((row) => row.id === id)
  const element = table.value?.$el
  if (index < 0 || !element) {
    return
  }
  const top = index * rowHeight.value
  const header = 40
  if (top < element.scrollTop) {
    element.scrollTop = top
  } else if (top + rowHeight.value + header > element.scrollTop + element.clientHeight) {
    element.scrollTop = top + rowHeight.value + header - element.clientHeight
  }
}

function hideableColumns() {
  return columns.value.filter((column) => column.enableHiding !== false && column.id)
}

// The grid role belongs on the <table> itself (its rows and cells then map to row and gridcell); UTable puts
// attributes on its scroll container.
watchEffect(() => {
  const element = table.value?.tableRef
  if (element) {
    element.setAttribute('role', 'grid')
    element.setAttribute('aria-label', 'Annotations')
    element.setAttribute('aria-rowcount', String(props.rows.length + 1))
    element.setAttribute('aria-multiselectable', 'true')
  }
})

defineExpose({ scrollToId, columnVisibility, hideableColumns })
</script>

<template>
  <UTable
    ref="table"
    v-model:row-selection="tableSelection"
    v-model:sorting="sorting"
    v-model:column-visibility="columnVisibility"
    v-model:column-sizing="columnSizing"
    v-model:column-pinning="columnPinning"
    :data="rows"
    :columns="columns"
    :get-row-id="(row: TreeNode) => row.id"
    :enable-row-selection="(row: { original: TreeNode }) => !row.original.missing"
    :enable-sorting="!tree"
    enable-column-resizing
    column-resize-mode="onChange"
    :virtualize="{ estimateSize: rowHeight, overscan: 16 }"
    :meta="{ class: { tr: rowClass } }"
    sticky
    class="annotation-grid h-full"
    :ui="{
      base: 'table-fixed w-max min-w-full',
      th: 'py-2 text-xs font-medium',
      td: preferences.state.density === 'compact' ? 'py-1.5 text-sm' : 'py-2.5 text-sm',
      tr: 'cursor-pointer',
    }"
    :on-select="(_event: Event, row: { original: TreeNode }) => emit('open', row.original.key, row.original.id)"
    empty="No annotations match the filter."
  />
</template>
