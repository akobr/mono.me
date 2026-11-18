<script setup lang="ts">
import { ref } from 'vue'
import { useStepUpStore } from '@/composables/useStepUp'
import { useContextStore } from '@/stores/context'

const context = useContextStore()
// Invite is the page's main action, in the top bar (424-41); it needs Administrator on the organization itself.
const canInvite = useCan('manageAccess', 'org')
const inviting = ref(false)
const stepUp = useStepUpStore()

// An invitation is a deferred grant: it needs a recent sign-in (424-264), asked for before the dialog is filled in.
async function invite() {
  // for the organization of the click: the page stays when the switcher moves on while the sign-in is checked
  const org = context.org
  if ((await stepUp.ensureRecentSignIn()) && org === context.org) {
    inviting.value = true
  }
}
</script>

<template>
  <UDashboardPanel id="organization-members">
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
            @click="invite()"
          />
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

      <InviteDialog v-if="context.org" v-model:open="inviting" :access-point-key="context.org" />
    </template>
  </UDashboardPanel>
</template>
