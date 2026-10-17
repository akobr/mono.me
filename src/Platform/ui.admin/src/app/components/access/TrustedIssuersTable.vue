<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { deleteFederatedIssuer, type FederatedIssuer } from '@42for.net/2splatform.sdk'
import { useGetFederatedIssuers, useGetMachineAuthentication } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { formatLifetime, keySourceLabels } from '@/utils/federation'

// Trusted issuers of the project (Administrator, 424-46), on the Machines page next to the shared certificates: the
// OIDC issuers whose tokens Federated machines exchange for Storyteller tokens. Add and edit go through
// TrustedIssuerDialog; delete is the kill switch and is allowed while rules use the issuer. Like the shared
// certificates, the list stays available while the machine authentication policy does not enable Federated.
const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()

const issuers = useGetFederatedIssuers(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => !!context.project, meta: { silent: true } } },
)
const policy = useGetMachineAuthentication(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey, meta: { silent: true } },
})
const disabled = computed(() => !!policy.data.value && !policy.data.value.EnabledCredentialKinds?.includes('Federated'))
const names = computed(() => (issuers.data.value ?? []).map((issuer) => issuer.Name ?? ''))

const dialogOpen = ref(false)
const editing = ref<FederatedIssuer>()
const deleting = ref<FederatedIssuer>()
const busy = ref(false)

function add() {
  editing.value = undefined
  dialogOpen.value = true
}

function edit(issuer: FederatedIssuer) {
  editing.value = issuer
  dialogOpen.value = true
}

async function remove() {
  busy.value = true
  try {
    await deleteFederatedIssuer(context.org!, context.project!, deleting.value!.Name!)
    toast.add({ title: `Trusted issuer ${deleting.value!.Name} deleted`, color: 'success', icon: 'i-lucide-trash-2' })
    deleting.value = undefined
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
  } catch (error) {
    toast.add({ title: 'Delete failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex justify-end">
      <UButton label="Add trusted issuer" icon="i-lucide-plus" @click="add" />
    </div>
    <UAlert
      v-if="disabled"
      color="info"
      variant="subtle"
      icon="i-lucide-shield-off"
      title="Federated machines are disabled"
      description="The machine authentication policy of this project does not enable the Federated credential kind, so no token of these issuers is exchanged. Enable the kind in Settings to use them."
    />
    <LoadingState v-if="issuers.isPending.value" />
    <ErrorState v-else-if="issuers.isError.value" :error="issuers.error.value" @retry="issuers.refetch()" />
    <EmptyState
      v-else-if="!issuers.data.value?.length"
      icon="i-lucide-badge-check"
      title="No trusted issuers"
      description="A trusted issuer lets Federated machines exchange a token of their platform (GitHub Actions, Kubernetes, Vercel, Azure, AWS, Google Cloud) for a Storyteller token, without a stored secret."
    />
    <div v-else class="overflow-x-auto rounded-md border border-default">
      <table class="w-full text-sm">
        <thead class="bg-elevated/50 text-xs text-muted">
          <tr>
            <th class="px-3 py-2 text-start font-medium">Name</th>
            <th class="px-3 py-2 text-start font-medium">Issuer</th>
            <th class="px-3 py-2 text-start font-medium">Key source</th>
            <th class="px-3 py-2 text-start font-medium">Max lifetime</th>
            <th class="px-3 py-2 text-start font-medium">Required claims</th>
            <th class="px-3 py-2" />
          </tr>
        </thead>
        <tbody class="divide-y divide-default">
          <tr v-for="issuer in issuers.data.value" :key="issuer.Name">
            <td class="px-3 py-2">
              <span class="font-mono text-xs font-medium">{{ issuer.Name }}</span>
              <p v-if="issuer.Description" class="text-xs text-muted">{{ issuer.Description }}</p>
            </td>
            <td class="px-3 py-2 font-mono text-xs break-all">{{ issuer.Issuer }}</td>
            <td class="px-3 py-2 text-xs">{{ keySourceLabels[issuer.KeySource ?? 'Discovery'] }}</td>
            <td class="px-3 py-2 text-xs">{{ formatLifetime(issuer.MaxTokenLifetimeSeconds) }}</td>
            <td class="px-3 py-2 text-xs">
              <ul v-if="Object.keys(issuer.RequiredClaims ?? {}).length" class="space-y-0.5">
                <li v-for="(values, claim) in issuer.RequiredClaims" :key="claim">
                  <code class="font-mono">{{ claim }}</code> = <code class="font-mono">{{ values.join(' or ') }}</code>
                </li>
              </ul>
              <span v-else class="text-dimmed">–</span>
            </td>
            <td class="px-3 py-2 text-end">
              <span class="flex justify-end gap-1">
                <UButton
                  label="Edit"
                  size="xs"
                  color="neutral"
                  variant="ghost"
                  :aria-label="`Edit ${issuer.Name}`"
                  @click="edit(issuer)"
                />
                <UButton
                  icon="i-lucide-trash-2"
                  size="xs"
                  color="neutral"
                  variant="ghost"
                  :aria-label="`Delete ${issuer.Name}`"
                  @click="deleting = issuer"
                />
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <TrustedIssuerDialog v-model:open="dialogOpen" :issuer="editing" :names="names" />
    <ConfirmDialog
      :open="!!deleting"
      :title="`Delete ${deleting?.Name}?`"
      description="Rules that use it stop matching new exchanges within a minute; Storyteller tokens already issued stay valid until they expire. Its name can't be used again while rules of machines still name it."
      :confirm-text="deleting?.Name"
      :loading="busy"
      @update:open="(value) => !value && (deleting = undefined)"
      @confirm="remove"
    />
  </div>
</template>
