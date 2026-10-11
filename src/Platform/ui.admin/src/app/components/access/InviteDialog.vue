<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { z } from 'zod'
import { createInvitation, getRole, hasErrorCode } from '@42for.net/2splatform.sdk'
import { invalidateAccess } from '@/queries/invalidation'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'
import { assignableRoles, type AssignableRole } from '@/utils/roles'

/**
 * Invite people (companion spec E3): one or more emails, a role and an expiry (7 days by default, 30 at most:
 * InvitationOptions.MaxExpiresInDays). Storyteller stores the invitation; WorkOS sends the email.
 */
const props = defineProps<{ accessPointKey: string }>()
const open = defineModel<boolean>('open', { default: false })

const session = useSessionStore()
const queryClient = useQueryClient()
const toast = useToast()

const emails = ref<string[]>([])
const role = ref<AssignableRole>('Reader')
const days = ref(7)
const sending = ref(false)
const failures = ref<{ email: string; message: string }[]>([])

watch(open, (value) => {
  if (value) {
    emails.value = []
    failures.value = []
  }
})

const emailSchema = z.email()
const invalid = computed(() => emails.value.filter((email) => !emailSchema.safeParse(email.trim()).success))
const roles = computed(() =>
  assignableRoles.filter((entry) => entry !== 'Owner' || getRole(session.accessMap, props.accessPointKey) === 'Owner'),
)

async function send() {
  sending.value = true
  failures.value = []
  let sent = 0
  for (const email of emails.value.map((entry) => entry.trim())) {
    try {
      await createInvitation(props.accessPointKey, { Email: email, Role: role.value, ExpiresInDays: days.value })
      sent++
    } catch (error) {
      failures.value.push({
        email,
        message: hasErrorCode(error, 'InvitationExists') ? 'A pending invitation already exists.' : errorMessage(error),
      })
    }
  }
  sending.value = false
  await invalidateAccess(queryClient, props.accessPointKey)
  if (sent) {
    toast.add({ title: `${sent} invitation${sent === 1 ? '' : 's'} sent`, color: 'success', icon: 'i-lucide-mail' })
  }
  if (!failures.value.length) {
    open.value = false
  } else {
    emails.value = failures.value.map((failure) => failure.email)
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="Invite people"
    :description="`To ${accessPointKey}. WorkOS sends the invitation email.`"
  >
    <template #body>
      <div class="space-y-4">
        <UFormField
          label="Emails"
          required
          help="Type an address and press Enter."
          :error="invalid.length ? `Not an email: ${invalid.join(', ')}` : undefined"
        >
          <UInputTags v-model="emails" placeholder="ada@example.com" class="w-full" autofocus />
        </UFormField>
        <div class="grid gap-4 sm:grid-cols-2">
          <UFormField label="Role">
            <USelect v-model="role" :items="roles" class="w-full" />
          </UFormField>
          <UFormField label="Expires after (days)">
            <UInputNumber v-model="days" :min="1" :max="30" class="w-full" />
          </UFormField>
        </div>
        <ul v-if="failures.length" class="space-y-0.5 text-xs text-error" role="alert">
          <li v-for="failure in failures" :key="failure.email">
            <strong>{{ failure.email }}</strong
            >: {{ failure.message }}
          </li>
        </ul>
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton
            label="Send invitations"
            icon="i-lucide-send"
            :loading="sending"
            :disabled="!emails.length || invalid.length > 0"
            @click="send"
          />
        </div>
      </div>
    </template>
  </UModal>
</template>
