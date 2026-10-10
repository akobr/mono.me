<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { CommandPaletteGroup, CommandPaletteItem } from '@nuxt/ui'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { metaOf } from '@/utils/annotation-key'
import { groupMemberships } from '@/utils/memberships'
import { buildNavigation } from '@/utils/navigation'

// ⌘K / Ctrl+K (spec section 8): annotations of the catalog (Fuse.js, top 20), recently opened keys, pages of
// the current context, projects and actions.
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const session = useSessionStore()
const preferences = usePreferencesStore()
const role = useRole()
const shortcuts = useShortcutsHelp()
const sidebar = useSidebar()
const create = useAnnotationCreate()
const canEdit = useCan('edit')

const open = ref(false)
const searchTerm = ref('')

const viewBase = computed(() =>
  context.org && context.project && context.view ? `/${context.org}/${context.project}/${context.view}` : undefined,
)
const catalogInScope = computed(
  () =>
    !!viewBase.value &&
    catalog.scope?.org === context.org &&
    catalog.scope?.project === context.project &&
    catalog.scope?.view === context.view,
)

const annotationItem = (key: string): CommandPaletteItem => {
  const annotation = catalog.byKey.get(key)
  const meta = metaOf(key.slice(0, 3))
  return {
    label: annotation?.Name ?? key,
    suffix: key,
    description: annotation?.Title,
    icon: meta?.icon,
    ui: { itemLeadingIcon: meta?.textClass },
    to: `${viewBase.value}/annotations/${key}`,
  }
}

// Built only while the palette is open: mapping 20,000 annotations on every catalog change would be wasted work.
const annotationItems = computed<CommandPaletteItem[]>(() =>
  open.value && catalogInScope.value ? [...catalog.byKey.keys()].map(annotationItem) : [],
)

const recentItems = computed<CommandPaletteItem[]>(() =>
  open.value && catalogInScope.value
    ? preferences.state.recentKeys.filter((key) => catalog.byKey.has(key)).map(annotationItem)
    : [],
)

const pageItems = computed<CommandPaletteItem[]>(() =>
  buildNavigation({
    org: context.org,
    project: context.project,
    view: context.view,
    projectRole: role.projectRole.value,
    orgRole: role.orgRole.value,
  }).flatMap((section) =>
    section.items.map((item) => ({ label: item.label, suffix: section.label, icon: item.icon, to: item.to })),
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
  ...(catalogInScope.value && canEdit.value
    ? [
        {
          label: 'New annotation',
          icon: 'i-lucide-plus',
          onSelect: async () => {
            await router.push(`${viewBase.value}/annotations`)
            create.show()
          },
        },
      ]
    : []),
  { label: 'Switch project', icon: 'i-lucide-arrow-left-right', to: '/orgs' },
  {
    label: preferences.sidebarPinned ? 'Unpin the sidebar' : 'Pin the sidebar',
    icon: 'i-lucide-pin',
    onSelect: () => (preferences.sidebarPinned = !preferences.sidebarPinned),
  },
  { label: 'Keyboard shortcuts', icon: 'i-lucide-keyboard', kbds: ['?'], onSelect: () => shortcuts.show() },
  { label: 'Sign out', icon: 'i-lucide-log-out', onSelect: () => void session.signOut() },
])

const groups = computed<CommandPaletteGroup[]>(() => [
  ...(recentItems.value.length && !searchTerm.value
    ? [{ id: 'recent', label: 'Recent', items: recentItems.value }]
    : []),
  ...(annotationItems.value.length
    ? [
        {
          id: 'annotations',
          label: 'Annotations',
          items: annotationItems.value,
          // Only the best 20 matches; nothing is listed before the user types.
          postFilter: (term: string, items: CommandPaletteItem[]) => (term ? items.slice(0, 20) : []),
        },
      ]
    : []),
  { id: 'pages', label: 'Pages', items: pageItems.value },
  { id: 'projects', label: 'Projects', items: projectItems.value },
  { id: 'actions', label: 'Actions', items: actionItems.value },
])

const go = (path: string | undefined) => path && void router.push(path)

defineShortcuts({
  '?': () => shortcuts.show(),
  '[': () => sidebar.toggleFor(preferences.sidebarPinned),
  'g-o': () => go('/orgs'),
  'g-a': () => go(viewBase.value && `${viewBase.value}/annotations`),
  'g-c': () => go(viewBase.value && `${viewBase.value}/configurations`),
  'g-g': () => go(viewBase.value && `${viewBase.value}/graph`),
  'g-t': () => go(viewBase.value && `${viewBase.value}/templates`),
  'g-s': () => go(viewBase.value && `${viewBase.value}/schemas`),
})
</script>

<template>
  <UDashboardSearch
    v-model:open="open"
    v-model:search-term="searchTerm"
    :groups="groups"
    :fuse="{
      fuseOptions: { keys: ['label', 'suffix', 'description'], threshold: 0.3, ignoreLocation: true },
      resultLimit: 20,
    }"
    color-mode
    placeholder="Search annotations, pages, projects and actions…"
  />
  <ShortcutsHelp />
</template>
