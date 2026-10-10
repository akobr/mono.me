<script setup lang="ts">
import { useContextStore } from '@/stores/context'

const context = useContextStore()
</script>

<template>
  <UDashboardPanel id="project-members">
    <template #header>
      <AppNavbar title="Members">
        <template #right>
          <RoleLegend />
        </template>
      </AppNavbar>
    </template>
    <template #body>
      <div class="space-y-3">
        <p class="text-sm text-muted">
          Members of <span class="font-mono">{{ context.projectKey }}</span
          >. An organization role does not grant access to its projects; invite people to the project itself.
        </p>
        <MembersTable v-if="context.projectKey" :access-point-key="context.projectKey" />
      </div>
    </template>
  </UDashboardPanel>
</template>
