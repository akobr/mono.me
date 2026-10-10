<script setup lang="ts">
import { ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getCertificateAuthority,
  setMachineAuthentication,
  type MachineAuthenticationPolicy,
} from '@42for.net/2splatform.sdk'
import { useGetAccessPoint } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { credentialKindDescriptions, issuesCertificates } from '@/utils/machines'

// Project settings (spec section 13): the machine authentication policy (Administrator), the CA certificate
// download (PEM; the SDK fetcher returns non-JSON bodies as text) and project information.
const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const role = useRole()
const canAdmin = useCan('manageAccess')

const accessPoint = useGetAccessPoint(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canAdmin.value, meta: { silent: true } },
})

const policy = ref<MachineAuthenticationPolicy>({ CredentialKind: 'ApiKey', CertificateLifetimeDays: 90 })
watch(
  () => accessPoint.data.value?.MachineAuthentication,
  (value) => value && (policy.value = { ...value }),
  { immediate: true },
)

const saving = ref(false)
const kinds = Object.entries(credentialKindDescriptions).map(([value]) => ({ label: value, value }))

async function savePolicy() {
  saving.value = true
  try {
    await setMachineAuthentication(context.projectKey!, {
      CredentialKind: policy.value.CredentialKind,
      CertificateLifetimeDays: issuesCertificates(policy.value) ? policy.value.CertificateLifetimeDays : null,
    })
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
    toast.add({ title: 'Machine policy saved', color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    toast.add({ title: 'The policy was not saved', description: errorMessage(error), color: 'error' })
  } finally {
    saving.value = false
  }
}

async function downloadCa() {
  try {
    const pem = await getCertificateAuthority()
    const url = URL.createObjectURL(new Blob([pem], { type: 'application/x-pem-file' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'storyteller-ca.pem'
    link.click()
    URL.revokeObjectURL(url)
  } catch (error) {
    toast.add({ title: 'The CA certificate could not be downloaded', description: errorMessage(error), color: 'error' })
  }
}
</script>

<template>
  <UDashboardPanel id="settings">
    <template #header>
      <AppNavbar title="Settings" />
    </template>
    <template #body>
      <div class="mx-auto w-full max-w-3xl space-y-6">
        <UCard>
          <template #header><h2 class="font-semibold">Project</h2></template>
          <dl class="grid grid-cols-[10rem_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
            <dt class="text-muted">Access point</dt>
            <dd class="font-mono">{{ context.projectKey }}</dd>
            <dt class="text-muted">Your role</dt>
            <dd><RoleBadge :role="role.projectRole.value" /></dd>
            <template v-if="accessPoint.data.value">
              <dt class="text-muted">Members</dt>
              <dd>{{ Object.keys(accessPoint.data.value.AccessMap ?? {}).length }}</dd>
            </template>
          </dl>
        </UCard>

        <UCard>
          <template #header>
            <div class="flex items-center justify-between">
              <h2 class="font-semibold">Machine authentication</h2>
              <UBadge
                v-if="!canAdmin"
                label="Administrator"
                icon="i-lucide-lock"
                color="neutral"
                variant="subtle"
                size="sm"
              />
            </div>
          </template>
          <p v-if="!canAdmin" class="text-sm text-muted">The policy is visible and editable for Administrators.</p>
          <LoadingState v-else-if="accessPoint.isPending.value" />
          <ErrorState
            v-else-if="accessPoint.isError.value"
            :error="accessPoint.error.value"
            compact
            @retry="accessPoint.refetch()"
          />
          <form v-else class="space-y-4" @submit.prevent="savePolicy">
            <UFormField
              label="Credential kind"
              :help="policy.CredentialKind ? credentialKindDescriptions[policy.CredentialKind] : undefined"
            >
              <USelect v-model="policy.CredentialKind" :items="kinds" class="w-full max-w-sm" />
            </UFormField>
            <UFormField v-if="issuesCertificates(policy)" label="Certificate lifetime (days)">
              <UInputNumber v-model="policy.CertificateLifetimeDays" :min="1" :max="825" class="w-40" />
            </UFormField>
            <p class="text-xs text-muted">The policy applies to machines created or reset after the change.</p>
            <div class="flex justify-end">
              <UButton type="submit" label="Save policy" :loading="saving" />
            </div>
          </form>
        </UCard>

        <UCard>
          <template #header><h2 class="font-semibold">Certificate authority</h2></template>
          <p class="mb-3 text-sm text-muted">
            The CA that signs machine and shared certificates; servers that verify mTLS clients trust it.
          </p>
          <UButton
            label="Download CA certificate (PEM)"
            icon="i-lucide-download"
            color="neutral"
            variant="outline"
            @click="downloadCa"
          />
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
