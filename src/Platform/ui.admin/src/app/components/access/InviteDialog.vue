<script setup lang="ts">
import { computed, ref, useTemplateRef, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { createInvitation, getRole, hasErrorCode } from '@42for.net/2splatform.sdk'
import { invalidateAccess } from '@/queries/invalidation'
import { useSessionStore } from '@/stores/session'
import { emailDelimiter, isEmail, normalizeEmails } from '@/utils/emails'
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
const empty = ref(false)
const failures = ref<{ email: string; message: string }[]>([])
const tagsInput = useTemplateRef<{ inputRef?: HTMLInputElement | null }>('tagsInput')

watch(open, (value) => {
  if (value) {
    emails.value = []
    failures.value = []
    empty.value = false
  }
})

// Every commit of the tags input (a typed address, a pasted list, the text left on blur) goes through
// normalizeEmails (424-41): a list becomes one tag per address, and duplicates go, also those in another case. With
// `duplicate` on, reka accepts every commit and clears the input; this setter drops what is already there.
const emailTags = computed({
  get: () => emails.value,
  set: (value: string[]) => {
    emails.value = normalizeEmails(value)
    empty.value = false
  },
})

const invalid = computed(() => emails.value.filter((email) => !isEmail(email)))
const roles = computed(() =>
  assignableRoles.filter((entry) => entry !== 'Owner' || getRole(session.accessMap, props.accessPointKey) === 'Owner'),
)
const fieldError = computed(() =>
  invalid.value.length
    ? `Not an email: ${invalid.value.join(', ')}`
    : empty.value
      ? 'Enter at least one email address.'
      : undefined,
)

/**
 * A pasted list becomes tags at once. A paste without a separator stays a normal paste, so a part of an address pasted
 * into a half-typed one joins it. Reka's own `addOnPaste` would split without these rules.
 */
function onPaste(event: ClipboardEvent) {
  const text = event.clipboardData?.getData('text/plain') ?? ''
  if (!/[\s,;<]/.test(text)) {
    return
  }
  event.preventDefault()
  const input = event.target as HTMLInputElement
  const before = input.value.slice(0, input.selectionStart ?? input.value.length)
  const after = input.value.slice(input.selectionEnd ?? input.value.length)
  input.value = ''
  emailTags.value = [...emails.value, before + text + after]
}

/** Commits the text still in the input, typed but neither followed by a separator nor by Enter. */
function commitTyped() {
  const input = tagsInput.value?.inputRef
  if (input?.value.trim()) {
    emailTags.value = [...emails.value, input.value]
  }
  if (input) {
    input.value = ''
  }
}

async function send() {
  commitTyped()
  if (!emails.value.length) {
    empty.value = true
    return
  }
  if (invalid.value.length) {
    return
  }
  sending.value = true
  failures.value = []
  let sent = 0
  for (const email of emails.value) {
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
    :description="`To ${accessPointKey}. Everyone gets the same role and expiry; WorkOS sends the emails.`"
  >
    <template #body>
      <div class="space-y-4">
        <UFormField
          label="Emails"
          required
          help="Separate addresses with spaces or commas, or paste a list."
          :error="fieldError"
        >
          <UInputTags
            ref="tagsInput"
            v-model="emailTags"
            :delimiter="emailDelimiter"
            duplicate
            add-on-blur
            placeholder="ada@example.com"
            class="w-full"
            autofocus
            @paste="onPaste"
          >
            <template #item-text="{ item }">
              <span :class="isEmail(item) ? undefined : 'text-error'">{{ item }}</span>
            </template>
          </UInputTags>
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
            :disabled="invalid.length > 0"
            @click="send"
          />
        </div>
      </div>
    </template>
  </UModal>
</template>
