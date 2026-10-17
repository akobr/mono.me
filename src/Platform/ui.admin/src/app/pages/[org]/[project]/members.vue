<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useGetMembers } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'

// Members and invitations of the project, like the organization's Members page (424-35).
const route = useRoute()
const context = useContextStore()
// A history entry links its actor here with ?highlight=<account id>.
const highlight = computed(() => (typeof route.query.highlight === 'string' ? route.query.highlight : undefined))
// Invite is the page's main action, in the top bar (424-41); an organization role does not grant it.
const canInvite = useCan('manageAccess')
const inviting = ref(false)

// #invitations (the old Invitations page redirects here) scrolls once the members above it are listed; the query is
// the one MembersTable reads.
const members = useGetMembers(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey, meta: { silent: true } },
})
useHashScroll(() => !members.isPending.value)
</script>

<template>
  <UDashboardPanel id="project-members">
    <template #header>
      <AppNavbar title="Members">
        <template #right>
          <RoleLegend />
          <!-- icon-only below sm, with the same name, like the main actions of the other pages -->
          <UButton
            v-if="canInvite"
            label="Invite"
            aria-label="Invite"
            icon="i-lucide-user-plus"
            :ui="{ label: 'hidden sm:inline' }"
            @click="inviting = true"
          />
        </template>
      </AppNavbar>
    </template>
    <template #body>
      <div v-if="context.projectKey" class="space-y-6">
        <section class="space-y-2">
          <h2 class="text-xs font-semibold text-muted uppercase">Project members</h2>
          <p class="text-sm text-muted">
            Members of <span class="font-mono">{{ context.projectKey }}</span
            >. An organization role does not grant access to its projects; invite people to the project itself.
          </p>
          <MembersTable :access-point-key="context.projectKey" :highlight="highlight" />
        </section>
        <section id="invitations" tabindex="-1" class="scroll-mt-4 space-y-2 outline-none">
          <h2 class="text-xs font-semibold text-muted uppercase">Invitations</h2>
          <InvitationsTable :access-point-key="context.projectKey" />
        </section>
      </div>

      <InviteDialog v-if="context.projectKey" v-model:open="inviting" :access-point-key="context.projectKey" />
    </template>
  </UDashboardPanel>
</template>
