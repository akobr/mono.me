<script setup lang="ts">
import { computed } from 'vue'
import { labelColor } from '@/utils/labels'

const props = withDefaults(defineProps<{ labels?: string[]; max?: number }>(), { labels: () => [], max: 3 })

const shown = computed(() => props.labels.slice(0, props.max))
const rest = computed(() => props.labels.length - shown.value.length)
</script>

<template>
  <span class="flex min-w-0 items-center gap-1 overflow-hidden">
    <UBadge
      v-for="label in shown"
      :key="label"
      :label="label"
      :color="labelColor(label)"
      variant="subtle"
      size="sm"
      class="max-w-28 truncate"
    />
    <UTooltip v-if="rest > 0" :text="labels.slice(max).join(', ')">
      <UBadge :label="`+${rest}`" color="neutral" variant="outline" size="sm" />
    </UTooltip>
  </span>
</template>
