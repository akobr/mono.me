<script setup lang="ts">
import type { ParsedFilter } from '@/utils/filter-parser'
import type { GraphLayout } from './graph-model'

/**
 * Graph toolbar (spec section 12): the filter box of the grid, then layout, focus depth, Fit, Reheat, Export PNG,
 * the switch between focus mode and the whole view, and "Open as list".
 */
defineProps<{
  query: string
  parsed: ParsedFilter
  shown: number
  total: number
  layout: GraphLayout
  depth: number
  /** The focused key; undefined while the whole view is shown. */
  focusKey?: string
  canShowAll: boolean
  canList: boolean
  pinned: boolean
  compact?: boolean
}>()

const emit = defineEmits<{
  'update:query': [value: string]
  'update:layout': [value: GraphLayout]
  'update:depth': [value: number]
  fit: []
  reheat: []
  export: []
  showAll: []
  focus: []
  openList: []
  releaseAll: []
}>()

const layouts = [
  { label: 'Layered', value: 'layered', icon: 'i-lucide-rows-3' },
  { label: 'Force', value: 'force', icon: 'i-lucide-share-2' },
  { label: 'Radial', value: 'radial', icon: 'i-lucide-orbit' },
]
</script>

<template>
  <div class="flex w-full flex-col gap-2">
    <AnnotationFilterBar
      :query="query"
      :parsed="parsed"
      mode="grid"
      perspective="subject"
      :shown="shown"
      :total="total"
      compact
      @update:query="(value) => emit('update:query', value)"
    />
    <div class="flex flex-wrap items-center gap-2">
      <USelect
        :model-value="layout"
        :items="layouts"
        size="sm"
        class="w-32"
        aria-label="Layout"
        @update:model-value="(value) => emit('update:layout', value as GraphLayout)"
      />
      <template v-if="focusKey">
        <div class="flex items-center gap-1.5">
          <span class="text-xs text-muted">Depth</span>
          <UInputNumber
            :model-value="depth"
            :min="1"
            :max="6"
            size="sm"
            class="w-24"
            aria-label="Focus depth"
            @update:model-value="(value) => emit('update:depth', Number(value) || 2)"
          />
        </div>
        <UBadge color="primary" variant="subtle" size="md" class="max-w-64 min-w-0 gap-1 font-mono" :title="focusKey">
          <UIcon name="i-lucide-crosshair" class="size-3.5 shrink-0" />
          <span class="truncate">{{ focusKey }}</span>
        </UBadge>
      </template>

      <div class="ms-auto flex flex-wrap items-center gap-1">
        <UTooltip text="Fit the graph to the screen">
          <UButton icon="i-lucide-scan" color="neutral" variant="ghost" size="sm" aria-label="Fit" @click="emit('fit')">
            <span v-if="!compact">Fit</span>
          </UButton>
        </UTooltip>
        <UTooltip text="Run the layout again">
          <UButton
            icon="i-lucide-flame"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Reheat"
            @click="emit('reheat')"
          >
            <span v-if="!compact">Reheat</span>
          </UButton>
        </UTooltip>
        <UTooltip v-if="pinned" text="Release every dragged node (or double-click the background)">
          <UButton
            icon="i-lucide-pin-off"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Release pinned nodes"
            @click="emit('releaseAll')"
          />
        </UTooltip>
        <UTooltip text="Download the graph as PNG">
          <UButton
            icon="i-lucide-image-down"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Export PNG"
            @click="emit('export')"
          >
            <span v-if="!compact">PNG</span>
          </UButton>
        </UTooltip>
        <UTooltip
          :text="canList ? 'Show these annotations in the grid' : 'Too many nodes for a list; narrow the focus'"
        >
          <UButton
            icon="i-lucide-table"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Open as list"
            :disabled="!canList"
            @click="emit('openList')"
          >
            <span v-if="!compact">Open as list</span>
          </UButton>
        </UTooltip>
        <UButton
          v-if="focusKey"
          icon="i-lucide-maximize"
          label="Show everything"
          color="neutral"
          variant="outline"
          size="sm"
          :disabled="!canShowAll"
          :title="canShowAll ? undefined : 'Available up to 20,000 annotations'"
          @click="emit('showAll')"
        />
        <UButton
          v-else
          icon="i-lucide-crosshair"
          label="Focus"
          color="neutral"
          variant="outline"
          size="sm"
          @click="emit('focus')"
        />
      </div>
    </div>
  </div>
</template>
