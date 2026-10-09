<script setup lang="ts">
import { usePreferencesStore } from '@/stores/preferences'

defineProps<{ collapsed: boolean }>()

const preferences = usePreferencesStore()
</script>

<template>
  <div class="flex w-full items-center gap-2">
    <RouterLink to="/" class="flex min-w-0 items-center gap-2 font-semibold" aria-label="2S Admin home">
      <img src="/favicon.svg" alt="" class="size-6 shrink-0" />
      <span v-if="!collapsed" class="truncate">2S Admin</span>
    </RouterLink>
    <UTooltip v-if="!collapsed" :text="preferences.sidebarPinned ? 'Unpin sidebar' : 'Pin sidebar'">
      <UButton
        class="ms-auto"
        :icon="preferences.sidebarPinned ? 'i-lucide-pin-off' : 'i-lucide-pin'"
        :aria-label="preferences.sidebarPinned ? 'Unpin sidebar' : 'Pin sidebar'"
        color="neutral"
        variant="ghost"
        size="sm"
        @click="preferences.sidebarPinned = !preferences.sidebarPinned"
      />
    </UTooltip>
  </div>
</template>
