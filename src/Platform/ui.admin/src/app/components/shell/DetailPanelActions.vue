<script setup lang="ts">
import { computed } from 'vue'

/**
 * Pin and full-screen buttons of a detail header, placed before its close button. They change the remembered
 * preferences, so the choice applies at once and to every detail opened later.
 */
const { mode, canPin, canFullscreen, togglePinned, toggleFullscreen } = useDetailPanel()

const pinLabel = computed(() => (mode.value === 'pinned' ? 'Unpin the detail' : 'Pin the detail'))
const fullscreenLabel = computed(() => (mode.value === 'full' ? 'Exit full screen' : 'Open in full screen'))
</script>

<template>
  <UTooltip v-if="canPin" :text="pinLabel">
    <UButton
      :icon="mode === 'pinned' ? 'i-lucide-pin-off' : 'i-lucide-pin'"
      color="neutral"
      variant="ghost"
      size="sm"
      :aria-label="pinLabel"
      @click="togglePinned"
    />
  </UTooltip>
  <UTooltip v-if="canFullscreen" :text="fullscreenLabel">
    <UButton
      :icon="mode === 'full' ? 'i-lucide-minimize-2' : 'i-lucide-maximize-2'"
      color="neutral"
      variant="ghost"
      size="sm"
      :aria-label="fullscreenLabel"
      @click="toggleFullscreen"
    />
  </UTooltip>
</template>
