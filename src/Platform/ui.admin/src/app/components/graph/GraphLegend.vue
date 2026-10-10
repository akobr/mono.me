<script setup lang="ts">
import { computed } from 'vue'
import { typeCodes, typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'

/** Type legend with toggles and counts (spec section 12); the colors are the `--st-*` tokens of the grid. */
const props = defineProps<{
  /** Nodes per type in the graph. */
  counts: ReadonlyMap<AnnotationTypeCode, number>
  hidden: ReadonlySet<AnnotationTypeCode>
}>()

const emit = defineEmits<{ toggle: [code: AnnotationTypeCode]; showAll: [] }>()

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
  <div
    class="flex flex-wrap items-center gap-1 rounded-md border border-default bg-default/90 p-1 text-xs shadow-sm backdrop-blur"
    role="group"
    aria-label="Annotation types"
  >
    <button
      v-for="item in items"
      :key="item.code"
      type="button"
      class="inline-flex items-center gap-1.5 rounded px-1.5 py-1 hover:bg-elevated"
      :class="item.hidden ? 'opacity-50' : ''"
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
  </div>
</template>
