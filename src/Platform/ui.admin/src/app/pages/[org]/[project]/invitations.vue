<script setup lang="ts">
import { useContextStore } from '@/stores/context'

const context = useContextStore()
</script>

<template>
  <UDashboardPanel id="project-invitations">
    <template #header>
      <AppNavbar title="Invitations" />
    </template>
    <template #body>
      <InvitationsTable v-if="context.projectKey" :access-point-key="context.projectKey" />
    </template>
  </UDashboardPanel>
</template>
