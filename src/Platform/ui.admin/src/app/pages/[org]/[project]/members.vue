<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useContextStore } from '@/stores/context'

const route = useRoute()
const context = useContextStore()
// A history entry links its actor here with ?highlight=<account id>.
const highlight = computed(() => (typeof route.query.highlight === 'string' ? route.query.highlight : undefined))
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
        <MembersTable v-if="context.projectKey" :access-point-key="context.projectKey" :highlight="highlight" />
      </div>
    </template>
  </UDashboardPanel>
</template>
