<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSessionStore } from '@/stores/session'
import { groupMemberships } from '@/utils/memberships'

const session = useSessionStore()
const organizations = computed(() => groupMemberships(session.accessMap))
const creating = ref(false)
</script>

<template>
  <UDashboardPanel id="orgs">
    <template #header>
      <AppNavbar title="Organizations">
        <template #right>
          <UButton
            label="New organization"
            icon="i-lucide-plus"
            class="hidden sm:inline-flex"
            @click="creating = true"
          />
          <UButton icon="i-lucide-plus" aria-label="New organization" class="sm:hidden" @click="creating = true" />
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <EmptyState
        v-if="organizations.length === 0"
        icon="i-lucide-building-2"
        title="No organizations yet"
        description="Create an organization with its first project, or ask a colleague to invite you."
      >
        <UButton label="New organization" icon="i-lucide-plus" @click="creating = true" />
      </EmptyState>

      <div v-else class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <UCard v-for="organization in organizations" :key="organization.name" :ui="{ body: 'p-0 sm:p-0' }">
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <RouterLink
                :to="`/${organization.name}`"
                class="flex min-w-0 items-center gap-2 font-semibold hover:underline"
              >
                <UIcon name="i-lucide-building-2" class="size-4 shrink-0 text-muted" />
                <span class="truncate">{{ organization.name }}</span>
              </RouterLink>
              <RoleBadge v-if="organization.role" :role="organization.role" />
              <UBadge v-else label="Project member" color="neutral" variant="outline" size="sm" />
            </div>
          </template>
          <ul class="divide-y divide-default">
            <li v-for="project in organization.projects" :key="project.key">
              <RouterLink
                :to="`/${project.org}/${project.name}/default/annotations`"
                class="flex items-center justify-between gap-2 px-4 py-2.5 text-sm hover:bg-elevated/50"
              >
                <span class="flex min-w-0 items-center gap-2">
                  <UIcon name="i-lucide-folder" class="size-4 shrink-0 text-muted" />
                  <span class="truncate">{{ project.name }}</span>
                </span>
                <RoleBadge :role="project.role" />
              </RouterLink>
            </li>
            <li v-if="organization.projects.length === 0" class="px-4 py-3 text-sm text-muted">
              No projects you are a member of.
            </li>
          </ul>
        </UCard>
      </div>

      <OrganizationCreateDialog v-model:open="creating" />
    </template>
  </UDashboardPanel>
</template>
