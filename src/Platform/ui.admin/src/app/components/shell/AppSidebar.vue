<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import type { NavigationMenuItem } from '@nuxt/ui'
import { hasRole } from '@42for.net/2splatform.sdk'
import { usePreferencesStore } from '@/stores/preferences'
import { useBootState } from '@/boot/state'
import { useViewInbox } from '@/queries/inbox'
import { buildNavigation } from '@/utils/navigation'
import { defaultViewBadge } from '@/utils/views'

const route = useRoute()
const preferences = usePreferencesStore()
const navigation = useNavigationContext()
const { open, collapsed: sidebarCollapsed, close } = useSidebar()
const boot = useBootState()

const environment = computed(() => boot.value.config?.environmentName)

// The mark of the Inbox entry (424-31): the open items of the view the menu shows, which is the remembered view outside
// view pages. Asked only from Contributor up, as the API serves the inbox. It is the query the Inbox page observes and
// reads again while it is open, so the sidebar never polls. A failed read is silent and leaves no mark, and so does a
// read that has not answered yet. Below Contributor there is no mark either, also when an answer from before the role
// was lost is still cached: a query that is not asked keeps its last answer.
const canAsk = computed(() => hasRole(navigation.value.projectRole, 'Contributor'))
const inbox = useViewInbox(navigation, 'Open', { enabled: canAsk })
const hasOpenItems = computed(
  () => canAsk.value && !inbox.isError.value && (inbox.data.value?.OpenCount ?? 0) > 0,
)
/** What the mark means: the tooltip of the entry in the rail, and its description for a screen reader. */
const openItemsHint = 'Open items'
/** The element with the hint that describes the marked link: the dot alone says nothing to a screen reader. */
const markDescriptionId = 'navigation-inbox-mark'

// The sections of the remembered view and project stay on the pages of a less specific context (424-17).
// The default view is the production one: a "prod" badge follows its name, and the lock badges of the entries keep
// the right edge (424-43). The marked entry gets a dot on its icon, which the rail and the menu of a phone show too:
// not a number, so the text of the link stays its label and the trailing badge stays the lock.
const items = computed<NavigationMenuItem[][]>(() =>
  buildNavigation(navigation.value, { inbox: hasOpenItems.value }).map((section) => [
    ...(section.label
      ? [
          {
            label: section.label,
            type: 'label' as const,
            badge: section.defaultView ? defaultViewBadge : undefined,
            ui: { linkTrailing: 'ms-0' },
          },
        ]
      : []),
    ...section.items.map((item) => ({
      label: item.label,
      icon: item.icon,
      to: item.to,
      exact: item.exact,
      badge: item.locked ? { icon: 'i-lucide-lock', color: 'neutral' as const, variant: 'subtle' as const } : undefined,
      tooltip: item.locked
        ? { text: 'Read-only for your role' }
        : item.marked
          ? { text: openItemsHint }
          : undefined,
      ...(item.marked ? { chip: true, 'aria-describedby': markDescriptionId } : {}),
    })),
  ]),
)

// The slideover closes after navigation.
watch(() => route.fullPath, close)
</script>

<template>
  <!-- Outside the menus: the docked sidebar renders its menu a second time in the slideover of a phone, and an id must
  stay unique. A hidden element still describes the link that refers to it. -->
  <span v-if="hasOpenItems" :id="markDescriptionId" hidden>{{ openItemsHint }}</span>

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
