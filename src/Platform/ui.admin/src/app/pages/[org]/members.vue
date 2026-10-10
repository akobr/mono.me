<script setup lang="ts">
import { useContextStore } from '@/stores/context'

const context = useContextStore()
</script>

<template>
  <UDashboardPanel id="organization-members">
    <template #header>
      <AppNavbar title="Members">
        <template #right>
          <RoleLegend />
        </template>
      </AppNavbar>
    </template>
    <template #body>
      <div v-if="context.org" class="space-y-6">
        <section class="space-y-2">
          <h2 class="text-xs font-semibold text-muted uppercase">Organization members</h2>
          <p class="text-sm text-muted">
            Roles on <span class="font-mono">{{ context.org }}</span> itself; they do not grant access to its projects.
          </p>
          <MembersTable :access-point-key="context.org" />
        </section>
        <section class="space-y-2">
          <h2 class="text-xs font-semibold text-muted uppercase">Invitations</h2>
          <InvitationsTable :access-point-key="context.org" />
        </section>
      </div>
    </template>
  </UDashboardPanel>
</template>
