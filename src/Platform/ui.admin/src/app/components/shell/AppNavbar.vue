<script setup lang="ts">
import { usePreferencesStore } from '@/stores/preferences'
import { useContextStore } from '@/stores/context'

defineProps<{ title?: string }>()

const preferences = usePreferencesStore()
const context = useContextStore()
const { toggle } = useSidebar()
</script>

<template>
  <UDashboardNavbar :toggle="false" :ui="{ left: 'min-w-0 gap-1.5', title: 'truncate' }">
    <template #leading>
      <UButton
        icon="i-lucide-menu"
        color="neutral"
        variant="ghost"
        aria-label="Open navigation"
        :class="preferences.sidebarPinned ? 'lg:hidden' : ''"
        @click="toggle"
      />
      <ContextSwitcher v-if="context.org" />
    </template>

    <template #title>
      <span :class="context.org ? 'hidden 2xl:inline' : ''">{{ title }}</span>
    </template>

    <template #right>
      <slot name="right" />
      <UDashboardSearchButton :collapsed="true" variant="ghost" />
      <UserMenu />
    </template>
  </UDashboardNavbar>
</template>
