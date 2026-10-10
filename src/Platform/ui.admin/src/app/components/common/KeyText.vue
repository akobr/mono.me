<script setup lang="ts">
import { computed } from 'vue'

/** An annotation key in monospace: the type code and separators dimmed, the own name emphasized. */
const props = withDefaults(defineProps<{ value: string; copy?: boolean; truncate?: boolean }>(), {
  copy: false,
  truncate: true,
})

const segments = computed(() => props.value.split('.'))
</script>

<template>
  <span class="inline-flex min-w-0 items-center gap-1">
    <code class="min-w-0 font-mono text-xs" :class="truncate ? 'truncate' : 'break-all'" :title="value">
      <template v-for="(segment, index) in segments" :key="index">
        <span v-if="index > 0" class="text-muted">.</span>
        <span
          :class="
            index === 0 ? 'text-muted' : index === segments.length - 1 ? 'font-semibold text-highlighted' : 'text-muted'
          "
          >{{ segment }}</span
        >
      </template>
    </code>
    <CopyButton v-if="copy" :value="value" />
  </span>
</template>
