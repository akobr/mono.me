<script setup lang="ts">
import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { useClipboard } from '@vueuse/core'
import { resendInvitation, revokeInvitation, type Invitation } from '@42for.net/2splatform.sdk'
import { useGetInvitations } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { errorMessage } from '@/utils/errors'

/**
 * Invitations of an access point: email, role, status, invited by and expiry, with Resend, Revoke and Copy link.
 * **Invite** is in the top bar of the Members pages (424-41).
 */
const props = defineProps<{ accessPointKey: string }>()

const queryClient = useQueryClient()
const toast = useToast()
const { copy } = useClipboard({ legacy: true })

const invitations = useGetInvitations(() => props.accessPointKey, { query: { meta: { silent: true } } })
const revoking = ref<Invitation>()

const statusColor = (status?: string) =>
  status === 'Pending' ? 'info' : status === 'Accepted' ? 'success' : status === 'Expired' ? 'warning' : 'neutral'

async function act(title: string, action: () => Promise<unknown>) {
  try {
    await action()
    toast.add({ title, color: 'success', icon: 'i-lucide-check' })
    await invalidateAccess(queryClient, props.accessPointKey)
  } catch (error) {
    toast.add({ title: 'The action failed', description: errorMessage(error), color: 'error' })
  }
}

async function copyLink(invitation: Invitation) {
  await copy(`${location.origin}/invitations/accept?id=${encodeURIComponent(invitation.Id ?? '')}`)
  toast.add({ title: 'Invitation link copied', icon: 'i-lucide-link' })
}
</script>

<template>
  <div class="space-y-2">
    <LoadingState v-if="invitations.isPending.value" />
    <ErrorState v-else-if="invitations.isError.value" :error="invitations.error.value" @retry="invitations.refetch()" />
    <div v-else class="overflow-x-auto rounded-md border border-default">
      <table class="w-full text-sm">
        <thead class="bg-elevated/50 text-xs text-muted">
          <tr>
            <th class="px-3 py-2 text-start font-medium">Email</th>
            <th class="px-3 py-2 text-start font-medium">Role</th>
            <th class="px-3 py-2 text-start font-medium">Status</th>
            <th class="px-3 py-2 text-start font-medium">Invited by</th>
            <th class="px-3 py-2 text-start font-medium">Expires</th>
            <th class="px-3 py-2" />
          </tr>
        </thead>
        <tbody class="divide-y divide-default">
          <tr v-for="invitation in invitations.data.value ?? []" :key="invitation.Id">
            <td class="px-3 py-2 font-medium">{{ invitation.Email }}</td>
            <td class="px-3 py-2"><RoleBadge :role="invitation.Role" /></td>
            <td class="px-3 py-2">
              <UBadge :label="invitation.Status" :color="statusColor(invitation.Status)" variant="subtle" size="sm" />
              <UTooltip
                v-if="invitation.Status === 'Pending' && invitation.IsEmailSent === false"
                text="The email could not be sent; use Resend or copy the link."
              >
                <UIcon name="i-lucide-mail-warning" class="ms-1 size-4 text-warning" />
              </UTooltip>
            </td>
            <td class="px-3 py-2 text-muted">{{ invitation.InvitedByName ?? invitation.InvitedById }}</td>
            <td class="px-3 py-2"><RelativeTime :value="invitation.ExpiresAt" /></td>
            <td class="px-3 py-2">
              <!-- An expired invitation is still pending for the API: Resend renews it, Revoke cleans it up. -->
              <span
                v-if="invitation.Status === 'Pending' || invitation.Status === 'Expired'"
                class="flex justify-end gap-1"
              >
                <UTooltip v-if="invitation.Status === 'Pending'" text="Copy link">
                  <UButton
                    icon="i-lucide-link"
                    color="neutral"
                    variant="ghost"
                    size="xs"
                    aria-label="Copy link"
                    @click="copyLink(invitation)"
                  />
                </UTooltip>
                <RoleGate capability="manageAccess" :scope="accessPointKey.includes('.') ? 'project' : 'org'">
                  <UButton
                    label="Resend"
                    size="xs"
                    color="neutral"
                    variant="ghost"
                    @click="act('Invitation resent', () => resendInvitation(accessPointKey, invitation.Id!))"
                  />
                  <UButton label="Revoke" size="xs" color="error" variant="ghost" @click="revoking = invitation" />
                </RoleGate>
              </span>
            </td>
          </tr>
          <tr v-if="(invitations.data.value ?? []).length === 0">
            <td colspan="6" class="px-3 py-4 text-muted">No invitations.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <ConfirmDialog
      :open="!!revoking"
      :title="`Revoke the invitation of ${revoking?.Email}?`"
      description="The invitation link stops working."
      confirm-label="Revoke"
      @update:open="(value) => !value && (revoking = undefined)"
      @confirm="
        act('Invitation revoked', () => revokeInvitation(accessPointKey, revoking!.Id!)).then(
          () => (revoking = undefined),
        )
      "
    />
  </div>
</template>
