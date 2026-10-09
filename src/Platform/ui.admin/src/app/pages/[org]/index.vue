<script setup lang="ts">
import { computed, ref } from 'vue'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'
import { groupMemberships } from '@/utils/memberships'

const context = useContextStore()
const session = useSessionStore()

const organization = computed(() => groupMemberships(session.accessMap).find((entry) => entry.name === context.org))
const creating = ref(false)
</script>

<template>
  <UDashboardPanel id="organization">
    <template #header>
      <AppNavbar title="Overview">
        <template #right>
          <RoleGate capability="owner" scope="org" mode="disable">
            <template #default="{ allowed, reason }">
              <UTooltip :text="reason" :disabled="allowed">
                <UButton label="New project" icon="i-lucide-plus" :disabled="!allowed" @click="creating = true" />
              </UTooltip>
            </template>
          </RoleGate>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="space-y-6">
        <div class="flex flex-wrap items-center gap-3">
          <UIcon name="i-lucide-building-2" class="size-6 text-muted" />
          <h1 class="text-xl font-semibold text-highlighted">{{ context.org }}</h1>
          <RoleBadge v-if="organization?.role" :role="organization.role" />
          <UBadge v-else label="Member of some projects" color="neutral" variant="outline" size="sm" />
        </div>

        <section class="space-y-3">
          <h2 class="text-sm font-semibold text-muted uppercase">Projects</h2>
          <EmptyState
            v-if="!organization?.projects.length"
            icon="i-lucide-folder"
            title="No projects you are a member of"
          />
          <div v-else class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            <UCard v-for="project in organization.projects" :key="project.key" :ui="{ body: 'p-4 sm:p-4' }">
              <div class="flex items-start justify-between gap-2">
                <RouterLink
                  :to="`/${project.org}/${project.name}/default/annotations`"
                  class="flex min-w-0 items-center gap-2 font-medium hover:underline"
                >
                  <UIcon name="i-lucide-folder" class="size-4 shrink-0 text-muted" />
                  <span class="truncate">{{ project.name }}</span>
                </RouterLink>
                <RoleBadge :role="project.role" />
              </div>
              <p class="mt-1 font-mono text-xs text-muted">{{ project.key }}</p>
            </UCard>
          </div>
        </section>

        <UAlert
          v-if="!organization?.role"
          color="neutral"
          variant="subtle"
          icon="i-lucide-info"
          description="You have no role on the organization itself. An organization role does not grant access to its projects, and project roles do not grant the organization's."
        />
      </div>

      <ProjectCreateDialog v-if="context.org" v-model:open="creating" :org="context.org" />
    </template>
  </UDashboardPanel>
</template>
