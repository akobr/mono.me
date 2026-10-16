<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { addRecipient, removeRecipient, updateRecipient, type ClientEncryptionRecipient } from '@42for.net/2splatform.sdk'
import { invalidateKeyValuePolicy } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

/**
 * The recipients of client-encrypted values (spec 29.6): name, id, key size and state; add by pasting a PEM or JWK
 * public key; disable or remove, with the warnings about re-encryption.
 */
defineProps<{ recipients: ClientEncryptionRecipient[]; editable: boolean }>()

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const confirm = useConfirm()

const adding = ref(false)
const name = ref('')
const id = ref('')
const publicKey = ref('')
const busy = ref<string>()
const failure = ref<string>()

const bits = (recipient: ClientEncryptionRecipient) => {
  const n = recipient.PublicKey?.N
  if (!n) {
    return '?'
  }
  const bytes = Math.floor((n.length * 6) / 8)
  return String(Math.round(bytes / 128) * 1024 || bytes * 8)
}

async function refresh() {
  if (context.org && context.project) {
    await invalidateKeyValuePolicy(queryClient, context.org, context.project)
  }
}

async function add() {
  if (!context.org || !context.project) {
    return
  }
  busy.value = 'add'
  failure.value = undefined
  try {
    const recipient = await addRecipient(context.org, context.project, { Id: id.value.trim() || undefined, Name: name.value.trim(), PublicKey: publicKey.value.trim() })
    adding.value = false
    name.value = ''
    id.value = ''
    publicKey.value = ''
    await refresh()
    toast.add({
      title: `Recipient ${recipient.Id} added`,
      description: 'Existing HYOK values are not encrypted for it; re-encrypt them with the CLI (sform story kv reencrypt) using a current recipient\'s private key.',
      color: 'success',
      icon: 'i-lucide-user-round-plus',
    })
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    busy.value = undefined
  }
}

async function setEnabled(recipient: ClientEncryptionRecipient, enabled: boolean) {
  if (!context.org || !context.project || !recipient.Id) {
    return
  }
  busy.value = recipient.Id
  try {
    await updateRecipient(context.org, context.project, recipient.Id, { IsEnabled: enabled })
    await refresh()
  } catch (error) {
    toast.add({ title: 'The recipient was not changed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = undefined
  }
}

async function remove(recipient: ClientEncryptionRecipient) {
  if (!context.org || !context.project || !recipient.Id) {
    return
  }
  const confirmed = await confirm({
    title: `Remove the recipient ${recipient.Name ?? recipient.Id}?`,
    description: 'Values already encrypted for it stay readable with its private key until they are encrypted again.',
    confirmLabel: 'Remove',
    color: 'error',
  })
  if (!confirmed) {
    return
  }
  busy.value = recipient.Id
  try {
    await removeRecipient(context.org, context.project, recipient.Id)
    await refresh()
    toast.add({ title: `Recipient ${recipient.Id} removed`, color: 'success', icon: 'i-lucide-user-round-minus' })
  } catch (error) {
    toast.add({ title: 'The recipient was not removed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = undefined
  }
}

const canAdd = computed(() => name.value.trim().length > 0 && publicKey.value.trim().length > 0)
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-center justify-between">
        <h3 class="font-semibold">Recipients (HYOK)</h3>
        <UButton v-if="editable && !adding" label="Add recipient" icon="i-lucide-plus" size="sm" @click="adding = true" />
      </div>
    </template>
    <div class="space-y-4 text-sm">
      <p class="text-muted">
        The RSA public keys (3072 bits or more) your applications encrypt client-side values for. The platform never
        holds a private key.
      </p>

      <p v-if="recipients.length === 0" class="text-muted">No recipients yet.</p>
      <div v-else class="overflow-x-auto rounded-md border border-default">
        <table class="w-full text-sm">
          <thead class="bg-elevated/50 text-xs text-muted">
            <tr>
              <th class="px-3 py-2 text-start font-medium">Name</th>
              <th class="px-3 py-2 text-start font-medium">Id</th>
              <th class="px-3 py-2 text-start font-medium">Key</th>
              <th class="px-3 py-2 text-start font-medium">Added</th>
              <th class="px-3 py-2 text-start font-medium">State</th>
              <th class="px-3 py-2" />
            </tr>
          </thead>
          <tbody class="divide-y divide-default">
            <tr v-for="recipient in recipients" :key="recipient.Id">
              <td class="px-3 py-2 font-medium">{{ recipient.Name }}</td>
              <td class="px-3 py-2 font-mono text-xs">{{ recipient.Id }}</td>
              <td class="px-3 py-2 text-xs text-muted">RSA {{ bits(recipient) }} bits</td>
              <td class="px-3 py-2 text-xs text-muted">{{ recipient.Created?.At ? formatDateTime(new Date(recipient.Created.At)) : '' }}</td>
              <td class="px-3 py-2">
                <UBadge :label="recipient.IsEnabled === false ? 'disabled' : 'enabled'" :color="recipient.IsEnabled === false ? 'neutral' : 'success'" variant="subtle" size="sm" />
              </td>
              <td class="px-3 py-2 text-end">
                <template v-if="editable">
                  <UButton
                    :label="recipient.IsEnabled === false ? 'Enable' : 'Disable'"
                    color="neutral"
                    variant="ghost"
                    size="xs"
                    :loading="busy === recipient.Id"
                    @click="setEnabled(recipient, recipient.IsEnabled === false)"
                  />
                  <UButton label="Remove" color="error" variant="ghost" size="xs" :loading="busy === recipient.Id" @click="remove(recipient)" />
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <form v-if="adding" class="space-y-3 rounded-md border border-default p-3" @submit.prevent="add">
        <div class="grid gap-3 sm:grid-cols-2">
          <UFormField label="Name" required>
            <UInput v-model="name" class="w-full" placeholder="invoicing-api" />
          </UFormField>
          <UFormField label="Id (optional)" help="The JWE kid; generated when empty.">
            <UInput v-model="id" class="w-full font-mono" placeholder="invoicing-api" />
          </UFormField>
        </div>
        <UFormField label="Public key" required help="PEM (-----BEGIN PUBLIC KEY-----) or a JWK with n and e.">
          <UTextarea v-model="publicKey" class="w-full font-mono" :rows="4" />
        </UFormField>
        <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="adding = false" />
          <UButton type="submit" label="Add" :disabled="!canAdd" :loading="busy === 'add'" />
        </div>
      </form>

      <p class="text-xs text-muted">
        Adding a recipient does not give it access to existing values, and removing one does not take access back;
        the values have to be encrypted again by a holder of a current recipient's private key.
      </p>
    </div>
  </UCard>
</template>
