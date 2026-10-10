<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { acceptInvitation, declineInvitation, hasErrorCode, type Invitation } from '@42for.net/2splatform.sdk'
import { useGetMyInvitations } from '@42for.net/2splatform.sdk/vue-query'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'

definePage({ meta: { layout: 'bare', allowWithoutAccount: true } })

// The signed-in user's pending invitations (companion spec E3). Accepting creates the account when needed and
// lands in the project.
const router = useRouter()
const session = useSessionStore()
const toast = useToast()

const invitations = useGetMyInvitations({ query: { meta: { silent: true } } })
const pending = computed(() => (invitations.data.value ?? []).filter((invitation) => invitation.Status === 'Pending'))
const busy = ref<string>()

const reasons: Record<string, string> = {
  EmailNotVerified: 'Verify your email address with the sign-in provider first.',
  EmailMismatch: 'The invitation was sent to another email address than the one you signed in with.',
  InvitationExpired: 'The invitation has expired; ask for a new one.',
  InvitationNotPending: 'The invitation was already used or revoked.',
}

const describe = (error: unknown) =>
  Object.entries(reasons).find(([code]) => hasErrorCode(error, code))?.[1] ?? errorMessage(error)

const target = (invitation: Invitation) => {
  const [org, project] = (invitation.AccessPointKey ?? '').split('.')
  return project ? `/${org}/${project}/default/annotations` : `/${org}`
}

async function accept(invitation: Invitation) {
  busy.value = invitation.Id
  try {
    session.setAccount(await acceptInvitation(invitation.Id!))
    toast.add({ title: `You joined ${invitation.AccessPointKey}`, color: 'success', icon: 'i-lucide-party-popper' })
    await router.replace(target(invitation))
  } catch (error) {
    toast.add({ title: 'The invitation could not be accepted', description: describe(error), color: 'error' })
  } finally {
    busy.value = undefined
  }
}

async function decline(invitation: Invitation) {
  busy.value = invitation.Id
  try {
    await declineInvitation(invitation.Id!)
    await invitations.refetch()
  } catch (error) {
    toast.add({ title: 'The invitation could not be declined', description: describe(error), color: 'error' })
  } finally {
    busy.value = undefined
  }
}
</script>

<template>
  <UCard>
    <div class="space-y-4">
      <div class="space-y-1">
        <h1 class="text-xl font-semibold text-highlighted">Invitations</h1>
        <p class="text-sm text-muted">Organizations and projects that invited {{ session.user?.email ?? 'you' }}.</p>
      </div>

      <LoadingState v-if="invitations.isPending.value" />
      <ErrorState
        v-else-if="invitations.isError.value"
        :error="invitations.error.value"
        compact
        @retry="invitations.refetch()"
      />
      <EmptyState v-else-if="pending.length === 0" icon="i-lucide-mail-open" title="No pending invitations" />
      <ul v-else class="divide-y divide-default rounded-md border border-default">
        <li v-for="invitation in pending" :key="invitation.Id" class="space-y-2 p-3">
          <div class="flex flex-wrap items-center gap-2">
            <span class="font-mono font-medium">{{ invitation.AccessPointKey }}</span>
            <RoleBadge :role="invitation.Role" />
          </div>
          <p class="text-xs text-muted">
            Invited by {{ invitation.InvitedByName ?? invitation.InvitedById }} · expires
            <RelativeTime :value="invitation.ExpiresAt" />
          </p>
          <div class="flex gap-2">
            <UButton label="Accept" size="sm" :loading="busy === invitation.Id" @click="accept(invitation)" />
            <UButton
              label="Decline"
              size="sm"
              color="neutral"
              variant="ghost"
              :disabled="busy === invitation.Id"
              @click="decline(invitation)"
            />
          </div>
        </li>
      </ul>

      <div class="flex justify-between gap-2 text-sm">
        <UButton
          v-if="session.accountStatus === 'missing'"
          to="/onboarding"
          label="Create my own organization instead"
          color="neutral"
          variant="link"
          size="sm"
        />
        <UButton v-else to="/orgs" label="Go to my organizations" color="neutral" variant="link" size="sm" />
        <UButton label="Sign out" color="neutral" variant="link" size="sm" @click="session.signOut()" />
      </div>
    </div>
  </UCard>
</template>
