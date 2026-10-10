<script setup lang="ts">
import { computed } from 'vue'
import { createReusableTemplate } from '@vueuse/core'
import { typeCodes, typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'

/**
 * Type legend with toggles and counts (spec section 12); the colors are the `--st-*` tokens of the grid.
 * Collapsed (narrow screens): a "Types" button with the legend in a popover, so the graph keeps its space.
 */
const props = defineProps<{
  /** Nodes per type in the graph. */
  counts: ReadonlyMap<AnnotationTypeCode, number>
  hidden: ReadonlySet<AnnotationTypeCode>
  collapsed?: boolean
}>()

const emit = defineEmits<{ toggle: [code: AnnotationTypeCode]; showAll: [] }>()

const [DefineList, ReuseList] = createReusableTemplate()
const formatter = new Intl.NumberFormat()
const items = computed(() =>
  typeCodes.map((code) => ({
    code,
    meta: typeMeta[code],
    count: props.counts.get(code) ?? 0,
    hidden: props.hidden.has(code),
  })),
)
</script>

<template>
  <div>
    <DefineList>
      <button
        v-for="item in items"
        :key="item.code"
        type="button"
        class="inline-flex min-h-8 items-center gap-1.5 rounded px-1.5 py-1 hover:bg-elevated"
        :class="item.hidden ? 'text-muted' : ''"
        :aria-pressed="!item.hidden"
        :title="item.hidden ? `Show ${item.meta.plural.toLowerCase()}` : `Hide ${item.meta.plural.toLowerCase()}`"
        @click="emit('toggle', item.code)"
      >
        <span
          class="size-2.5 rounded-full"
          :class="item.hidden ? 'border border-current bg-transparent' : 'bg-current'"
          :style="{ color: `var(--st-${item.code})` }"
          aria-hidden="true"
        />
        <span :class="item.hidden ? 'line-through' : ''">{{ item.meta.plural }}</span>
        <span class="text-muted tabular-nums">{{ formatter.format(item.count) }}</span>
      </button>
      <UButton v-if="hidden.size" label="All types" color="neutral" variant="link" size="xs" @click="emit('showAll')" />
    </DefineList>

    <UPopover v-if="collapsed" :content="{ align: 'start' }">
      <UButton
        icon="i-lucide-palette"
        :label="hidden.size ? `Types (${typeCodes.length - hidden.size}/${typeCodes.length})` : 'Types'"
        color="neutral"
        variant="outline"
        size="sm"
        class="bg-default/90 shadow-sm"
      />
      <template #content>
        <div class="flex max-w-72 flex-col items-stretch p-1 text-xs" role="group" aria-label="Annotation types">
          <ReuseList />
        </div>
      </template>
    </UPopover>
    <div
      v-else
      class="flex flex-wrap items-center gap-1 rounded-md border border-default bg-default/90 p-1 text-xs shadow-sm backdrop-blur"
      role="group"
      aria-label="Annotation types"
    >
      <ReuseList />
    </div>
  </div>
</template>
