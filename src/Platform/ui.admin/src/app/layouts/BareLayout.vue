<script setup lang="ts">
// wide: the column of the onboarding wizard; every other bare page keeps the narrow one.
defineProps<{ wide?: boolean }>()
</script>

<template>
  <div class="min-h-dvh flex flex-col items-center justify-center bg-(--ui-bg-muted) px-4 py-10">
    <div class="mb-6 flex items-center gap-2 text-lg font-semibold">
      <img src="@/assets/images/42logo-128.png" alt="" class="size-7" />
      <span>2S Admin</span>
    </div>
    <div class="w-full" :class="wide ? 'max-w-2xl' : 'max-w-md'">
      <slot />
    </div>
  </div>
</template>
