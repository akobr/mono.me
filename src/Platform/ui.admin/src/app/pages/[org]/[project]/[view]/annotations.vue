<script setup lang="ts">
import { computed, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useEventListener, useMediaQuery } from '@vueuse/core'
import type { RowSelectionState, SortingState } from '@tanstack/vue-table'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { columnLabels } from '@/components/annotations/grid/annotation-columns'
import { expandableIds, projectTree, type TreeNode } from '@/utils/tree-projection'

// Annotation grid or tree. The detail is a nested route (<RouterView>), so the list keeps its scroll position,
// selection and filter while the detail changes (spec section 5). What the URL does not hold (tree expansion,
// selection, sorting, keyboard cursor, scroll position) lives in the page memory of the view, so the page comes
// back as it was left.
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const filter = useAnnotationFilter()
const canEdit = useCan('edit')
const desktop = useMediaQuery('(min-width: 768px)')
const detail = useDetailPanel()

const grid = useTemplateRef<{
  scrollToId: (id: string) => void
  columnVisibility: Record<string, boolean>
  hideableColumns: () => { id?: string }[]
  scrollElement: () => HTMLElement | undefined
}>('grid')
const list = useTemplateRef<{ scrollElement: () => HTMLElement | undefined }>('list')
const memory = usePageMemory('annotations')
// The selection is keyed by annotation key; the expansion and the cursor by row id, since in the tree an annotation
// with several parents has a row (and an expansion) per path.
const rowSelection = computed<RowSelectionState>({
  get: () => memory.value.selection,
  set: (value) => (memory.value.selection = value),
})
const expanded = computed<Set<string>>({
  get: () => memory.value.expanded,
  set: (value) => (memory.value.expanded = value),
})
const sorting = computed<SortingState>({
  get: () => memory.value.sorting,
  set: (value) => (memory.value.sorting = value),
})
const activeId = computed<string | undefined>({
  get: () => memory.value.activeId,
  set: (value) => (memory.value.activeId = value),
})
const scrollTop = computed({
  get: () => memory.value.scrollTop,
  set: (value: number) => (memory.value.scrollTop = value),
})
const { open: creating } = useAnnotationCreate()

watch(
  () => context.scope,
  (scope) => scope && void catalog.load(scope),
  { immediate: true },
)

const selectedKey = computed(() => {
  const key = (route.params as { key?: string }).key
  return typeof key === 'string' ? key : undefined
})

const tree = computed(() =>
  filter.mode.value === 'tree'
    ? projectTree(
        catalog.annotations,
        filter.perspective.value,
        filter.active.value ? filter.predicate.value : undefined,
      )
    : [],
)

// While filtering, the whole matching tree is open; otherwise the user's expansion applies.
const isExpanded = (id: string) => filter.active.value || expanded.value.has(id)

const rows = computed<TreeNode[]>(() => {
  if (filter.mode.value === 'grid' || !desktop.value) {
    return filter.filtered.value.map((annotation) => ({
      id: annotation.AnnotationKey,
      key: annotation.AnnotationKey,
      annotation,
      missing: false,
      matched: true,
      depth: 0,
      children: [],
    }))
  }

  const flat: TreeNode[] = []
  const walk = (node: TreeNode) => {
    flat.push(node)
    if (isExpanded(node.id)) {
      node.children.forEach(walk)
    }
  }
  tree.value.forEach(walk)
  return flat
})

// A remembered selection may name annotations deleted meanwhile; those never reach a bulk action.
const selectedKeys = computed(() =>
  Object.keys(rowSelection.value).filter((key) => rowSelection.value[key] && catalog.byKey.has(key)),
)

useScrollMemory(
  () => (desktop.value ? grid.value?.scrollElement() : list.value?.scrollElement()),
  scrollTop,
  () => rows.value.length > 0,
)

function toggle(id: string) {
  const next = new Set(expanded.value)
  if (next.has(id)) {
    next.delete(id)
  } else {
    next.add(id)
  }
  expanded.value = next
}

const listPath = computed(() => `/${context.org}/${context.project}/${context.view}/annotations`)

// The row under the keyboard cursor (J/K); without one, the first row of the open annotation.
const activeRow = computed(
  () =>
    rows.value.find((row) => row.id === activeId.value) ??
    (selectedKey.value ? rows.value.find((row) => row.key === selectedKey.value) : undefined),
)

function open(key: string, id?: string) {
  activeId.value = id ?? key
  void router.push({ path: `${listPath.value}/${key}`, query: route.query })
}

function move(step: number) {
  const index = activeRow.value ? rows.value.indexOf(activeRow.value) : -1
  const next = rows.value[Math.min(Math.max(index + step, 0), rows.value.length - 1)]
  if (next) {
    activeId.value = next.id
    grid.value?.scrollToId(next.id)
  }
}

defineShortcuts({
  j: () => move(1),
  k: () => move(-1),
  arrowdown: () => move(1),
  arrowup: () => move(-1),
})

// Enter and Escape are not Nuxt UI shortcuts: those call preventDefault() before the handler, which would cancel
// a focused button (Save, Delete, the confirm dialogs). They act only outside controls, menus and dialogs.
useEventListener(window, 'keydown', (event: KeyboardEvent) => {
  if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) {
    return
  }
  const target = event.target as HTMLElement | null
  if (
    target?.closest(
      'input, textarea, select, button, a, [contenteditable="true"], [role="dialog"], [role="menu"], [role="listbox"]',
    )
  ) {
    return
  }
  if (event.key === 'Enter' && activeRow.value) {
    event.preventDefault()
    open(activeRow.value.key, activeRow.value.id)
  } else if (event.key === 'Escape' && selectedKey.value) {
    void router.push({ path: listPath.value, query: route.query })
  }
})

// The cursor follows the open annotation unless its row already shows it. Focus returns to that row when the
// detail closes; after the render, so a grid hidden by a full-screen detail is visible again.
watch(
  selectedKey,
  (key, previous) => {
    if (key) {
      if (activeRow.value?.key !== key) {
        activeId.value = rows.value.find((row) => row.key === key)?.id ?? key
      }
    } else if (previous && activeRow.value) {
      grid.value?.scrollToId(activeRow.value.id)
    }
  },
  { flush: 'post' },
)

const columnItems = computed<DropdownMenuItem[]>(() =>
  (grid.value?.hideableColumns() ?? [])
    .filter((column) => column.id && column.id in columnLabels)
    .map((column) => ({
      label: columnLabels[column.id!],
      type: 'checkbox' as const,
      checked: grid.value?.columnVisibility[column.id!] !== false,
      onUpdateChecked: (checked: boolean) => {
        if (grid.value) {
          grid.value.columnVisibility[column.id!] = checked
        }
      },
      onSelect: (event: Event) => event.preventDefault(),
    })),
)

// The row under the keyboard cursor (J/K) is announced; the rows themselves are virtualized.
const activeLabel = computed(() => {
  const annotation = activeRow.value ? catalog.byKey.get(activeRow.value.key) : undefined
  return annotation ? `${annotation.Name}, ${annotation.AnnotationKey}` : ''
})

const progressLabel = computed(() => `Loaded ${new Intl.NumberFormat().format(catalog.loadedCount)} annotations…`)
</script>

<template>
  <UDashboardPanel
    id="annotations"
    :class="{ hidden: detail.covers.value }"
    :ui="{ body: 'p-0 sm:p-0 gap-0 overflow-hidden' }"
  >
    <template #header>
      <AppNavbar title="Annotations">
        <template #right>
          <UTooltip text="Reload the catalog">
            <UButton
              icon="i-lucide-refresh-cw"
              color="neutral"
              variant="ghost"
              aria-label="Reload the catalog"
              :loading="catalog.status === 'loading'"
              @click="context.scope && catalog.load(context.scope, { force: true })"
            />
          </UTooltip>
          <UButton
            v-if="canEdit"
            label="New"
            icon="i-lucide-plus"
            class="hidden sm:inline-flex"
            @click="creating = true"
          />
          <UButton
            v-if="canEdit"
            icon="i-lucide-plus"
            aria-label="New annotation"
            class="sm:hidden"
            @click="creating = true"
          />
        </template>
      </AppNavbar>
      <UDashboardToolbar class="py-2">
        <AnnotationFilterBar
          :query="filter.query.value"
          :parsed="filter.parsed.value"
          :mode="filter.mode.value"
          :perspective="filter.perspective.value"
          :shown="filter.filtered.value.length"
          :total="catalog.loadedCount"
          :columns="columnItems"
          :compact="!desktop"
          @update:query="(value) => (filter.query.value = value)"
          @update:mode="(value) => (filter.mode.value = value)"
          @update:perspective="(value) => (filter.perspective.value = value)"
          @expand-all="expanded = new Set(expandableIds(tree))"
          @collapse-all="expanded = new Set()"
        />
      </UDashboardToolbar>
    </template>

    <template #body>
      <div
        v-if="catalog.status === 'loading' || catalog.fromSnapshot"
        class="flex items-center gap-3 border-b border-default px-4 py-1.5 text-xs text-muted"
      >
        <UProgress size="xs" class="w-40" />
        <span>{{ catalog.fromSnapshot ? 'Refreshing…' : progressLabel }}</span>
      </div>
      <AnnotationBulkBar v-if="selectedKeys.length && canEdit" :keys="selectedKeys" @clear="rowSelection = {}" />

      <ErrorState
        v-if="catalog.status === 'error'"
        :error="catalog.error"
        title="The catalog could not be loaded"
        @retry="context.scope && catalog.load(context.scope, { force: true })"
      />
      <EmptyState
        v-else-if="catalog.status === 'ready' && catalog.loadedCount === 0"
        icon="i-lucide-tags"
        title="No annotations in this view"
        description="Create the first annotation, or import them with the sform CLI."
      >
        <UButton v-if="canEdit" label="New annotation" icon="i-lucide-plus" @click="creating = true" />
      </EmptyState>
      <AnnotationGrid
        v-else-if="desktop"
        ref="grid"
        v-model:row-selection="rowSelection"
        v-model:sorting="sorting"
        :rows="rows"
        :tree="filter.mode.value === 'tree'"
        :selected-key="selectedKey"
        :active-id="activeRow?.id"
        :configurations="catalog.configurations"
        :is-expanded="isExpanded"
        class="min-h-0 flex-1"
        @open="open"
        @toggle="toggle"
      />
      <AnnotationList v-else ref="list" :rows="rows" :selected-key="selectedKey" class="min-h-0 flex-1" @open="open" />

      <p class="sr-only" aria-live="polite">{{ activeLabel }}</p>
      <AnnotationCreateDialog v-if="canEdit" v-model:open="creating" @created="open" />
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
