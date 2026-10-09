<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import type { NavigationMenuItem } from '@nuxt/ui'
import { usePreferencesStore } from '@/stores/preferences'
import { useContextStore } from '@/stores/context'
import { useBootState } from '@/boot/state'
import { buildNavigation } from '@/utils/navigation'

const route = useRoute()
const preferences = usePreferencesStore()
const context = useContextStore()
const role = useRole()
const { open, collapsed: sidebarCollapsed, close } = useSidebar()
const boot = useBootState()

const environment = computed(() => boot.value.config?.environmentName)

const items = computed<NavigationMenuItem[][]>(() =>
  buildNavigation({
    org: context.org,
    project: context.project,
    view: context.view,
    projectRole: role.projectRole.value,
    orgRole: role.orgRole.value,
  }).map((section) => [
    ...(section.label ? [{ label: section.label, type: 'label' as const }] : []),
    ...section.items.map((item) => ({
      label: item.label,
      icon: item.icon,
      to: item.to,
      exact: item.exact,
      badge: item.locked ? { icon: 'i-lucide-lock', color: 'neutral' as const, variant: 'subtle' as const } : undefined,
      tooltip: item.locked ? { text: 'Read-only for your role' } : undefined,
    })),
  ]),
)

// The slideover closes after navigation.
watch(() => route.fullPath, close)
</script>

<template>
  <UDashboardSidebar
    v-if="preferences.sidebarPinned"
    id="sidebar"
    v-model:open="open"
    v-model:collapsed="sidebarCollapsed"
    mode="slideover"
    collapsible
    resizable
    :min-size="12"
    :default-size="16"
    :max-size="24"
    :ui="{ footer: 'border-t border-default' }"
  >
    <template #header="{ collapsed }">
      <SidebarBrand :collapsed="collapsed" />
    </template>

    <template #default="{ collapsed }">
      <UNavigationMenu :items="items" :collapsed="collapsed" orientation="vertical" tooltip popover />
    </template>

    <template #footer="{ collapsed }">
      <div class="flex w-full items-center justify-between gap-2">
        <UBadge v-if="environment && !collapsed" :label="environment" color="neutral" variant="subtle" size="sm" />
        <UDashboardSidebarCollapse />
      </div>
    </template>
  </UDashboardSidebar>

  <USlideover v-else v-model:open="open" side="left" title="Navigation" :ui="{ content: 'max-w-72' }">
    <template #content>
      <div class="flex h-full flex-col gap-4 p-4">
        <SidebarBrand :collapsed="false" />
        <UNavigationMenu :items="items" orientation="vertical" />
        <UBadge
          v-if="environment"
          class="mt-auto self-start"
          :label="environment"
          color="neutral"
          variant="subtle"
          size="sm"
        />
      </div>
    </template>
  </USlideover>
</template>
