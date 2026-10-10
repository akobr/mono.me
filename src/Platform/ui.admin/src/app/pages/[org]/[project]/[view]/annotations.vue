<script setup lang="ts">
import { computed, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMediaQuery } from '@vueuse/core'
import type { RowSelectionState } from '@tanstack/vue-table'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { columnLabels } from '@/components/annotations/grid/annotation-columns'
import { expandableKeys, projectTree, type TreeNode } from '@/utils/tree-projection'

// Annotation grid or tree. The detail is a nested route (<RouterView>), so the list keeps its scroll position,
// selection and filter while the detail changes (spec section 5).
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const filter = useAnnotationFilter()
const canEdit = useCan('edit')
const desktop = useMediaQuery('(min-width: 768px)')

const grid = useTemplateRef<{
  scrollToKey: (key: string) => void
  columnVisibility: Record<string, boolean>
  hideableColumns: () => { id?: string }[]
}>('grid')
const rowSelection = ref<RowSelectionState>({})
const expanded = ref(new Set<string>())
const activeKey = ref<string>()
const { open: creating } = useAnnotationCreate()

watch(
  () => context.scope,
  (scope) => {
    if (scope) {
      void catalog.load(scope)
      rowSelection.value = {}
    }
  },
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
const isExpanded = (key: string) => filter.active.value || expanded.value.has(key)

const rows = computed<TreeNode[]>(() => {
  if (filter.mode.value === 'grid' || !desktop.value) {
    return filter.filtered.value.map((annotation) => ({
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
    if (isExpanded(node.key)) {
      node.children.forEach(walk)
    }
  }
  tree.value.forEach(walk)
  return flat
})

const selectedKeys = computed(() => Object.keys(rowSelection.value).filter((key) => rowSelection.value[key]))

function toggle(key: string) {
  const next = new Set(expanded.value)
  if (next.has(key)) {
    next.delete(key)
  } else {
    next.add(key)
  }
  expanded.value = next
}

const listPath = computed(() => `/${context.org}/${context.project}/${context.view}/annotations`)

function open(key: string) {
  activeKey.value = key
  void router.push({ path: `${listPath.value}/${key}`, query: route.query })
}

function move(step: number) {
  const index = rows.value.findIndex((row) => row.key === (activeKey.value ?? selectedKey.value))
  const next = rows.value[Math.min(Math.max(index + step, 0), rows.value.length - 1)]
  if (next) {
    activeKey.value = next.key
    grid.value?.scrollToKey(next.key)
  }
}

defineShortcuts({
  j: () => move(1),
  k: () => move(-1),
  arrowdown: () => move(1),
  arrowup: () => move(-1),
  enter: () => activeKey.value && open(activeKey.value),
  escape: () => selectedKey.value && router.push({ path: listPath.value, query: route.query }),
})

// Focus returns to the originating row when the detail closes.
watch(selectedKey, (key, previous) => {
  if (key) {
    activeKey.value = key
  } else if (previous) {
    grid.value?.scrollToKey(previous)
  }
})

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

const progressLabel = computed(() => `Loaded ${new Intl.NumberFormat().format(catalog.loadedCount)} annotations…`)
</script>

<template>
  <UDashboardPanel id="annotations" :ui="{ body: 'p-0 sm:p-0 gap-0 overflow-hidden' }">
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
          @expand-all="expanded = new Set(expandableKeys(tree))"
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
        :rows="rows"
        :tree="filter.mode.value === 'tree'"
        :selected-key="selectedKey"
        :active-key="activeKey"
        :configurations="catalog.configurations"
        :is-expanded="isExpanded"
        class="min-h-0 flex-1"
        @open="open"
        @toggle="toggle"
      />
      <AnnotationList v-else :rows="rows" :selected-key="selectedKey" class="min-h-0 flex-1" @open="open" />

      <AnnotationCreateDialog v-if="canEdit" v-model:open="creating" @created="open" />
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
