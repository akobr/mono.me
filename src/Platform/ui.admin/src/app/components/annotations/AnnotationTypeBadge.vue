<script setup lang="ts">
import { computed } from 'vue'
import { metaOf } from '@/utils/annotation-key'

/**
 * Type chip: icon and code in the type color (color is never the only signal). `label` shows the name instead of the
 * code and `icon` only the icon; the code itself is shown only on Schemas (424-34).
 */
const props = withDefaults(defineProps<{ type?: string | null; variant?: 'chip' | 'icon' | 'label' }>(), {
  type: undefined,
  variant: 'chip',
})

const meta = computed(() => metaOf(props.type))
</script>

<template>
  <span
    v-if="meta && variant === 'icon'"
    :class="meta.textClass"
    class="inline-flex"
    :title="meta.label"
    :aria-label="meta.label"
    role="img"
  >
    <UIcon :name="meta.icon" class="size-4" />
  </span>
  <span
    v-else-if="meta"
    class="inline-flex items-center gap-1 rounded-sm px-1.5 py-0.5 text-xs font-medium whitespace-nowrap"
    :class="[meta.textClass, meta.bgClass]"
    :title="meta.label"
  >
    <UIcon :name="meta.icon" class="size-3.5 shrink-0" />
    <span class="font-mono">{{ variant === 'label' ? meta.label : meta.code }}</span>
  </span>
  <span v-else class="text-dimmed">–</span>
</template>
