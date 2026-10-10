<script setup lang="ts">
import { computed } from 'vue'
import type { Header } from '@tanstack/vue-table'
import type { TreeNode } from '@/utils/tree-projection'

/** Sortable header label (Shift-click adds a sort level) with a resize handle. */
const props = defineProps<{ header: Header<TreeNode, unknown>; label: string }>()

const column = computed(() => props.header.column)
const sorted = computed(() => column.value.getIsSorted())
const icon = computed(() =>
  sorted.value === 'asc'
    ? 'i-lucide-arrow-up'
    : sorted.value === 'desc'
      ? 'i-lucide-arrow-down'
      : 'i-lucide-arrow-up-down',
)
const sortIndex = computed(() => (column.value.getSortIndex() > 0 ? column.value.getSortIndex() + 1 : undefined))
</script>

<template>
  <div class="group relative flex items-center gap-1 pe-2">
    <button
      v-if="column.getCanSort()"
      type="button"
      class="-ms-1 inline-flex min-w-0 items-center gap-1 rounded px-1 py-0.5 hover:bg-elevated"
      :aria-label="`Sort by ${label}`"
      @click="(event) => column.toggleSorting(undefined, event.shiftKey)"
    >
      <span class="truncate">{{ label }}</span>
      <UIcon
        :name="icon"
        class="size-3.5 shrink-0"
        :class="sorted ? 'text-primary' : 'text-dimmed opacity-0 group-hover:opacity-100'"
      />
      <span v-if="sortIndex" class="text-[10px] text-primary">{{ sortIndex }}</span>
    </button>
    <span v-else class="truncate">{{ label }}</span>
    <span
      v-if="column.getCanResize()"
      class="absolute -end-2 top-1/2 hidden h-5 w-1.5 -translate-y-1/2 cursor-col-resize rounded bg-(--ui-border-accented) opacity-0 group-hover:opacity-100 pointer-fine:block"
      :class="column.getIsResizing() ? 'bg-primary opacity-100' : ''"
      role="separator"
      :aria-label="`Resize ${label}`"
      @mousedown.stop="header.getResizeHandler()($event)"
      @touchstart.stop="header.getResizeHandler()($event)"
      @dblclick="column.resetSize()"
    />
  </div>
</template>
