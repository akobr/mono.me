<script setup lang="ts">
withDefaults(defineProps<{ label?: string }>(), { label: 'Loading…' })
</script>

<template>
  <div class="flex items-center justify-center gap-2 py-12 text-sm text-muted" role="status" aria-live="polite">
    <UIcon name="i-lucide-loader-circle" class="size-5 animate-spin" />
    <span>{{ label }}</span>
  </div>
</template>
