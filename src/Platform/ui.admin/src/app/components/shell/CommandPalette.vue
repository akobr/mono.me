<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { groupMemberships } from '@/utils/memberships'
import { buildNavigation } from '@/utils/navigation'

// ⌘K / Ctrl+K: pages of the current context, projects of every organization, and actions.
const router = useRouter()
const context = useContextStore()
const session = useSessionStore()
const preferences = usePreferencesStore()
const role = useRole()
const shortcuts = useShortcutsHelp()
const sidebar = useSidebar()

const open = ref(false)
const searchTerm = ref('')

const pageItems = computed<CommandPaletteItem[]>(() =>
  buildNavigation({
    org: context.org,
    project: context.project,
    view: context.view,
    projectRole: role.projectRole.value,
    orgRole: role.orgRole.value,
  }).flatMap((section) =>
    section.items.map((item) => ({
      label: item.label,
      suffix: section.label,
      icon: item.icon,
      to: item.to,
    })),
  ),
)

const projectItems = computed<CommandPaletteItem[]>(() =>
  groupMemberships(session.accessMap).flatMap((organization) =>
    organization.projects.map((project) => ({
      label: `${project.org} / ${project.name}`,
      suffix: project.role,
      icon: 'i-lucide-folder',
      to: `/${project.org}/${project.name}/default/annotations`,
    })),
  ),
)

const actionItems = computed<CommandPaletteItem[]>(() => [
  {
    label: preferences.sidebarPinned ? 'Unpin the sidebar' : 'Pin the sidebar',
    icon: 'i-lucide-pin',
    onSelect: () => (preferences.sidebarPinned = !preferences.sidebarPinned),
  },
  { label: 'Keyboard shortcuts', icon: 'i-lucide-keyboard', kbds: ['?'], onSelect: () => shortcuts.show() },
  { label: 'Sign out', icon: 'i-lucide-log-out', onSelect: () => void session.signOut() },
])

const groups = computed<CommandPaletteGroup[]>(() => [
  { id: 'pages', label: 'Pages', items: pageItems.value },
  { id: 'projects', label: 'Projects', items: projectItems.value },
  { id: 'actions', label: 'Actions', items: actionItems.value },
])

const viewPath = (section: string) =>
  context.org && context.project && context.view
    ? `/${context.org}/${context.project}/${context.view}/${section}`
    : undefined

const go = (path: string | undefined) => path && void router.push(path)

defineShortcuts({
  '?': () => shortcuts.show(),
  '[': () => sidebar.toggleFor(preferences.sidebarPinned),
  'g-o': () => go('/orgs'),
  'g-a': () => go(viewPath('annotations')),
})
</script>

<template>
  <UDashboardSearch
    v-model:open="open"
    v-model:search-term="searchTerm"
    :groups="groups"
    color-mode
    placeholder="Search pages, projects and actions…"
  />
  <ShortcutsHelp />
</template>
