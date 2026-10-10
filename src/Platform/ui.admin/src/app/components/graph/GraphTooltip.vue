<script setup lang="ts">
import { computed } from 'vue'
import { useNow } from '@vueuse/core'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { annotationStatus } from '@/utils/annotation-status'

/** Hover card of a node: full key, title and status (spec section 12), positioned beside the pointer. */
const props = defineProps<{
  annotationKey: string
  annotation?: Annotation
  x: number
  y: number
  /** Size of the container, to flip the card at the right and bottom edges. */
  bounds: { width: number; height: number }
  neighbours: number
}>()

const now = useNow({ interval: 60_000 })
const status = computed(() => (props.annotation ? annotationStatus(props.annotation, now.value.getTime()) : undefined))
const style = computed(() => {
  const flipX = props.x > props.bounds.width - 300
  const flipY = props.y > props.bounds.height - 140
  return {
    left: flipX ? undefined : `${props.x + 14}px`,
    right: flipX ? `${props.bounds.width - props.x + 14}px` : undefined,
    top: flipY ? undefined : `${props.y + 14}px`,
    bottom: flipY ? `${props.bounds.height - props.y + 14}px` : undefined,
  }
})
</script>

<template>
  <div
    class="pointer-events-none absolute z-10 max-w-72 space-y-1 rounded-md border border-default bg-default px-3 py-2 text-xs shadow-lg"
    :style="style"
    role="tooltip"
  >
    <div class="flex items-center gap-2">
      <AnnotationTypeBadge :type="annotationKey.slice(0, 3)" />
      <span class="truncate font-semibold text-highlighted">{{
        annotation?.Name ?? annotationKey.split('.').at(-1)
      }}</span>
      <AnnotationStatusBadge v-if="status" :status="status" size="xs" />
    </div>
    <p class="font-mono break-all text-muted">{{ annotationKey }}</p>
    <p v-if="annotation?.Title" class="text-default">{{ annotation.Title }}</p>
    <p class="text-dimmed">{{ neighbours }} {{ neighbours === 1 ? 'connection' : 'connections' }} in the graph</p>
  </div>
</template>
