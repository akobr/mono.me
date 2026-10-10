<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useGetViews } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'
import { groupMemberships } from '@/utils/memberships'
import { projectTarget, viewTarget } from '@/utils/context-paths'

// Organization / project / view breadcrumb dropdowns (like Vercel). Switching keeps the current section.
const route = useRoute()
const context = useContextStore()
const session = useSessionStore()

const organizations = computed(() => groupMemberships(session.accessMap))
const currentOrganization = computed(() => organizations.value.find((entry) => entry.name === context.org))

const views = useGetViews(
  () => context.org ?? '',
  () => context.project ?? '',
  undefined,
  { query: { enabled: () => !!context.org && !!context.project && context.access === 'ok', staleTime: 5 * 60_000 } },
)

const organizationItems = computed<DropdownMenuItem[][]>(() => [
  organizations.value.map((entry) => ({
    label: entry.name,
    icon: 'i-lucide-building-2',
    to: `/${entry.name}`,
    active: entry.name === context.org,
  })),
  [{ label: 'All organizations', icon: 'i-lucide-layout-grid', to: '/orgs' }],
])

const projectItems = computed<DropdownMenuItem[][]>(() => [
  (currentOrganization.value?.projects ?? []).map((entry) => ({
    label: entry.name,
    icon: 'i-lucide-folder',
    to: projectTarget(route.path, entry.org, entry.name),
    active: entry.name === context.project,
  })),
  [{ label: 'Organization overview', icon: 'i-lucide-building-2', to: `/${context.org}` }],
])

const viewItems = computed<DropdownMenuItem[][]>(() => {
  const names = (views.data.value ?? []).map((view) => view.Name).filter((name): name is string => !!name)
  const list = names.length > 0 ? names : ['default']

  return [
    list.map((name) => ({
      label: name,
      icon: 'i-lucide-eye',
      to: viewTarget(route.path, context.org!, context.project!, name),
      active: name === context.view,
    })),
    [{ label: 'Manage views', icon: 'i-lucide-settings-2', to: `/${context.org}/${context.project}/views` }],
  ]
})
</script>

<template>
  <nav class="flex min-w-0 items-center gap-0.5 text-sm" aria-label="Context">
    <UDropdownMenu :items="organizationItems" :content="{ align: 'start' }">
      <UButton
        :label="context.org"
        color="neutral"
        variant="ghost"
        size="sm"
        trailing-icon="i-lucide-chevrons-up-down"
        class="max-w-40"
        :ui="{ label: 'truncate' }"
      />
    </UDropdownMenu>
    <template v-if="context.project">
      <span class="text-dimmed" aria-hidden="true">/</span>
      <UDropdownMenu :items="projectItems" :content="{ align: 'start' }">
        <UButton
          :label="context.project"
          color="neutral"
          variant="ghost"
          size="sm"
          trailing-icon="i-lucide-chevrons-up-down"
          class="max-w-40"
          :ui="{ label: 'truncate' }"
        />
      </UDropdownMenu>
    </template>
    <template v-if="context.project && context.view">
      <span class="text-dimmed hidden sm:inline" aria-hidden="true">/</span>
      <UDropdownMenu :items="viewItems" :content="{ align: 'start' }">
        <UButton
          :label="context.view"
          color="neutral"
          variant="ghost"
          size="sm"
          icon="i-lucide-eye"
          trailing-icon="i-lucide-chevrons-up-down"
          class="hidden max-w-40 sm:inline-flex"
          :ui="{ label: 'truncate' }"
        />
      </UDropdownMenu>
    </template>
  </nav>
</template>
