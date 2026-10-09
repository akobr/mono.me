<script setup lang="ts">
withDefaults(defineProps<{ icon?: string; title: string; description?: string }>(), {
  icon: 'i-lucide-inbox',
  description: undefined,
})
</script>

<template>
  <div class="mx-auto flex max-w-md flex-col items-center gap-3 px-4 py-12 text-center">
    <div class="flex size-12 items-center justify-center rounded-full bg-(--ui-bg-elevated)">
      <UIcon :name="icon" class="size-6 text-muted" />
    </div>
    <h2 class="text-base font-semibold text-highlighted">{{ title }}</h2>
    <p v-if="description" class="text-sm text-muted">{{ description }}</p>
    <div v-if="$slots.default" class="mt-2 flex flex-wrap justify-center gap-2">
      <slot />
    </div>
  </div>
</template>
