<script setup lang="ts">
import { computed } from 'vue'
import { useColorMode } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'

// Light, dark or system; stored by VueUse under the key public/theme-init.js reads before the first paint.
const mode = useColorMode({ emitAuto: true })

const icon = computed(() =>
  mode.store.value === 'auto' ? 'i-lucide-monitor' : mode.store.value === 'dark' ? 'i-lucide-moon' : 'i-lucide-sun',
)

const items = computed<DropdownMenuItem[]>(() =>
  (
    [
      ['light', 'Light', 'i-lucide-sun'],
      ['dark', 'Dark', 'i-lucide-moon'],
      ['auto', 'System', 'i-lucide-monitor'],
    ] as const
  ).map(([value, label, itemIcon]) => ({
    label,
    icon: itemIcon,
    type: 'checkbox' as const,
    checked: mode.store.value === value,
    onSelect: () => (mode.store.value = value),
  })),
)
</script>

<template>
  <UDropdownMenu :items="items" :content="{ align: 'end' }">
    <UButton :icon="icon" color="neutral" variant="ghost" aria-label="Color mode" />
  </UDropdownMenu>
</template>
