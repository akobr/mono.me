<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { useNow } from '@vueuse/core'
import { issueSharedCertificate, revokeSharedCertificate, type SharedCertificate } from '@42for.net/2splatform.sdk'
import { useGetMachineAuthentication, useGetSharedCertificates } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { formatDate } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'
import { allowsSharedCertificates } from '@/utils/machines'
import type { OneTimeSecret } from '@/components/access/OneTimeSecretDialog.vue'

// Shared certificates of the project (Administrator, spec section 13), on the Machines page since 424-35: issue
// (shown once), revoke by typing the first 8 characters of the thumbprint, and a warning when one expires within 30
// days. Shared certificates follow the CertificateAndApiKey kind of the machine authentication policy: without it they
// can't be issued and don't sign in, while listing and revoking stay available.
const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const now = useNow({ interval: 60_000 })

const certificates = useGetSharedCertificates(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => !!context.project, meta: { silent: true } } },
)

const policy = useGetMachineAuthentication(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey, meta: { silent: true } },
})
// Unknown while the policy loads: keep issuing available, the API refuses it anyway.
const disabled = computed(() => !!policy.data.value && !allowsSharedCertificates(policy.data.value))

const issuing = ref(false)
const label = ref('')
const lifetime = ref(365)
const busy = ref(false)
const revoking = ref<SharedCertificate>()
const secret = ref<OneTimeSecret>()

const daysLeft = (certificate: SharedCertificate) =>
  certificate.NotAfter ? Math.floor((Date.parse(certificate.NotAfter) - now.value.getTime()) / 86_400_000) : undefined

async function issue() {
  busy.value = true
  try {
    const certificate = await issueSharedCertificate(context.org!, context.project!, {
      Label: label.value.trim(),
      LifetimeDays: lifetime.value,
    })
    issuing.value = false
    label.value = ''
    secret.value = {
      title: 'Shared certificate issued',
      id: certificate.Thumbprint,
      certificate: certificate.Certificate,
      certificatePassword: certificate.CertificatePassword,
      fileName: `${certificate.Label ?? 'shared'}.pfx`,
    }
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
  } catch (error) {
    toast.add({ title: 'The certificate was not issued', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

async function revoke() {
  busy.value = true
  try {
    await revokeSharedCertificate(context.org!, context.project!, revoking.value!.Thumbprint!)
    revoking.value = undefined
    toast.add({ title: 'Certificate revoked', color: 'success', icon: 'i-lucide-shield-off' })
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
  } catch (error) {
    toast.add({ title: 'Revoke failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex justify-end">
      <UButton label="Issue certificate" icon="i-lucide-plus" :disabled="disabled" @click="issuing = true" />
    </div>
    <UAlert
      v-if="disabled"
      color="info"
      variant="subtle"
      icon="i-lucide-shield-off"
      title="Shared certificates are disabled"
      description="Shared certificates belong to the CertificateAndApiKey credential kind, which the machine authentication policy of this project does not enable. New ones can't be issued and existing ones no longer sign in; enable the kind in Settings to use them again."
    />
    <LoadingState v-if="certificates.isPending.value" />
    <ErrorState
      v-else-if="certificates.isError.value"
      :error="certificates.error.value"
      @retry="certificates.refetch()"
    />
    <EmptyState
      v-else-if="!certificates.data.value?.length"
      icon="i-lucide-shield-check"
      title="No shared certificates"
      description="A shared certificate lets several machines authenticate with mTLS without one certificate per machine."
    />
    <div v-else class="overflow-x-auto rounded-md border border-default">
      <table class="w-full text-sm">
        <thead class="bg-elevated/50 text-xs text-muted">
          <tr>
            <th class="px-3 py-2 text-start font-medium">Label</th>
            <th class="px-3 py-2 text-start font-medium">Thumbprint</th>
            <th class="px-3 py-2 text-start font-medium">Valid from</th>
            <th class="px-3 py-2 text-start font-medium">Expires</th>
            <th class="px-3 py-2 text-start font-medium">Status</th>
            <th class="px-3 py-2" />
          </tr>
        </thead>
        <tbody class="divide-y divide-default">
          <tr v-for="certificate in certificates.data.value" :key="certificate.Thumbprint">
            <td class="px-3 py-2 font-medium">{{ certificate.Label }}</td>
            <td class="px-3 py-2 font-mono text-xs" :title="certificate.Thumbprint">
              {{ certificate.Thumbprint?.slice(0, 16) }}…
            </td>
            <td class="px-3 py-2 text-xs">
              {{ certificate.NotBefore ? formatDate(new Date(certificate.NotBefore)) : '–' }}
            </td>
            <td class="px-3 py-2 text-xs">
              {{ certificate.NotAfter ? formatDate(new Date(certificate.NotAfter)) : '–' }}
            </td>
            <td class="px-3 py-2">
              <UBadge v-if="certificate.IsRevoked" label="Revoked" color="neutral" variant="subtle" size="sm" />
              <UBadge
                v-else-if="(daysLeft(certificate) ?? 1) < 0"
                label="Expired"
                color="error"
                variant="subtle"
                size="sm"
              />
              <UBadge
                v-else-if="(daysLeft(certificate) ?? 99) <= 30"
                :label="`Expires in ${daysLeft(certificate)} d`"
                icon="i-lucide-triangle-alert"
                color="warning"
                variant="subtle"
                size="sm"
              />
              <UBadge v-else label="Valid" color="success" variant="subtle" size="sm" />
            </td>
            <td class="px-3 py-2 text-end">
              <UButton
                v-if="!certificate.IsRevoked"
                label="Revoke"
                size="xs"
                color="error"
                variant="ghost"
                @click="revoking = certificate"
              />
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <UModal v-model:open="issuing" title="Issue a shared certificate">
      <template #body>
        <form class="space-y-4" @submit.prevent="issue">
          <UFormField label="Label" required>
            <UInput v-model="label" class="w-full" placeholder="ci-pipeline" autofocus />
          </UFormField>
          <UFormField label="Lifetime (days)">
            <UInputNumber v-model="lifetime" :min="1" :max="3650" class="w-40" />
          </UFormField>
          <div class="flex justify-end gap-2">
            <UButton label="Cancel" color="neutral" variant="ghost" @click="issuing = false" />
            <UButton type="submit" label="Issue" :loading="busy" :disabled="!label.trim()" />
          </div>
        </form>
      </template>
    </UModal>
    <ConfirmDialog
      :open="!!revoking"
      :title="`Revoke ${revoking?.Label}?`"
      description="Machines using it can no longer authenticate."
      :confirm-text="revoking?.Thumbprint?.slice(0, 8)"
      confirm-label="Revoke"
      :loading="busy"
      @update:open="(value) => !value && (revoking = undefined)"
      @confirm="revoke"
    />
    <OneTimeSecretDialog :secret="secret" @close="secret = undefined" />
  </div>
</template>
