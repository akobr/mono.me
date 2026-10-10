<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useNow } from '@vueuse/core'
import { annotationStatus } from '@/utils/annotation-status'
import type { TreeNode } from '@/utils/tree-projection'

/** The mobile renderer of the same rows: two-line cards in a virtual list (spec section 8). */
const props = defineProps<{ rows: TreeNode[]; selectedKey?: string }>()
const emit = defineEmits<{ open: [key: string] }>()

const scroller = useTemplateRef<HTMLElement>('scroller')
const now = useNow({ interval: 60_000 })

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.rows.length,
    getScrollElement: () => scroller.value ?? null,
    estimateSize: () => 64,
    overscan: 10,
  })),
)
const items = computed(() => virtualizer.value.getVirtualItems())
</script>

<template>
  <div ref="scroller" class="h-full overflow-auto" role="list" aria-label="Annotations">
    <p v-if="rows.length === 0" class="p-6 text-center text-sm text-muted">No annotations match the filter.</p>
    <div :style="{ height: `${virtualizer.getTotalSize()}px` }" class="relative w-full">
      <div
        v-for="item in items"
        :key="rows[item.index]!.key"
        role="listitem"
        class="absolute inset-x-0 h-16 border-b border-default"
        :style="{ transform: `translateY(${item.start}px)` }"
      >
        <button
          type="button"
          class="flex size-full flex-col justify-center gap-1 px-4 text-start active:bg-elevated"
          :class="rows[item.index]!.key === selectedKey ? 'bg-primary/10' : ''"
          :aria-current="rows[item.index]!.key === selectedKey ? 'true' : undefined"
          @click="emit('open', rows[item.index]!.key)"
        >
          <span class="flex min-w-0 items-center gap-2">
            <AnnotationTypeBadge :type="rows[item.index]!.key.slice(0, 3)" variant="icon" />
            <span
              class="truncate font-medium text-highlighted"
              :style="{ paddingInlineStart: `${rows[item.index]!.depth * 12}px` }"
            >
              {{ rows[item.index]!.annotation?.Name ?? `(missing) ${rows[item.index]!.key}` }}
            </span>
            <AnnotationStatusBadge
              v-if="rows[item.index]!.annotation"
              class="ms-auto shrink-0"
              size="xs"
              :status="annotationStatus(rows[item.index]!.annotation!, now.getTime())"
            />
          </span>
          <span class="truncate font-mono text-xs text-muted">{{ rows[item.index]!.key }}</span>
        </button>
      </div>
    </div>
  </div>
</template>
