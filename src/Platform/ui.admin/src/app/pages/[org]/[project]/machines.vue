<script setup lang="ts">
import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { deleteMachineAccess, resetMachineAccess, type MachineAccess } from '@42for.net/2splatform.sdk'
import {
  useGetFederatedIssuers,
  useGetMachineAccesses,
  useGetMachineAuthentication,
  useGetSharedCertificates,
} from '@42for.net/2splatform.sdk/vue-query'
import { useBootState } from '@/boot/state'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { certificateDaysLeft, credentialKindLabel, hasSecret, isFederated, machineSecret } from '@/utils/machines'
import type { OneTimeSecret } from '@/components/access/OneTimeSecretDialog.vue'

// Machine access of the project (spec section 13). Create and Reset return credentials once; they go straight
// to OneTimeSecretDialog and are never cached. The project policy (readable by Contributors) gives the kinds and the
// default certificate lifetime of new machines (424-14). Administrators also see the shared certificates (424-35) and
// the trusted issuers of Federated machines (424-46) below the machines; Contributors don't, the API answers 403 to the
// certificates and only Administrators change issuers. A Federated machine has no secret: creating one shows how a
// workload exchanges its platform token (FederatedSetupDialog), and it has no Reset.
const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const canManage = useCan('machines')
const canAdmin = useCan('manageAccess')
const boot = useBootState()
const detail = useDetailPanel()

const machines = useGetMachineAccesses(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => !!context.project, meta: { silent: true } } },
)
const policy = useGetMachineAuthentication(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canManage.value, meta: { silent: true } },
})

const creating = ref(false)
const resetting = ref<MachineAccess>()
const deleting = ref<MachineAccess>()
const secret = ref<OneTimeSecret>()
const setup = ref<MachineAccess>()
const busy = ref(false)

async function refresh() {
  await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
}

function created(machine: MachineAccess) {
  if (isFederated(machine.CredentialKind)) {
    setup.value = machine
  } else {
    secret.value = machineSecret(machine, 'Machine access created', boot.value.config?.apiBaseUrl)
  }
  void refresh()
}

async function reset() {
  busy.value = true
  try {
    const machine = await resetMachineAccess(context.org!, context.project!, resetting.value!.Id!)
    resetting.value = undefined
    secret.value = machineSecret(machine, 'New credentials', boot.value.config?.apiBaseUrl)
    await refresh()
  } catch (error) {
    toast.add({ title: 'Reset failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

async function remove() {
  busy.value = true
  try {
    await deleteMachineAccess(context.org!, context.project!, deleting.value!.Id!)
    toast.add({ title: `Machine ${deleting.value!.Id} deleted`, color: 'success', icon: 'i-lucide-trash-2' })
    deleting.value = undefined
    await refresh()
  } catch (error) {
    toast.add({ title: 'Delete failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

const daysLeft = (machine: MachineAccess) => certificateDaysLeft(machine, policy.data.value?.CertificateLifetimeDays)

// The sections of Administrators, read here as well (the same queries as their tables) to know when they are listed.
const adminQuery = { enabled: () => !!context.project && canAdmin.value, meta: { silent: true } }
const certificates = useGetSharedCertificates(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: adminQuery },
)
const issuers = useGetFederatedIssuers(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: adminQuery },
)

// #shared-certificates (the old Certificates page redirects here) and #trusted-issuers (linked from New machine)
// scroll once the sections above them are listed.
useHashScroll(
  () => !machines.isPending.value && !(canAdmin.value && (certificates.isPending.value || issuers.isPending.value)),
)
</script>

<template>
  <UDashboardPanel id="machines" :class="{ hidden: detail.covers.value }">
    <template #header>
      <AppNavbar title="Machines">
        <template #right>
          <UButton v-if="canManage" label="New machine" icon="i-lucide-plus" @click="creating = true" />
        </template>
      </AppNavbar>
    </template>
    <template #body>
      <div class="space-y-6">
        <section class="space-y-2">
          <h2 class="text-xs font-semibold text-muted uppercase">Machine access</h2>
          <LoadingState v-if="machines.isPending.value" />
          <ErrorState v-else-if="machines.isError.value" :error="machines.error.value" @retry="machines.refetch()" />
          <EmptyState
            v-else-if="!machines.data.value?.length"
            icon="i-lucide-computer"
            title="No machine access"
            description="Machines (services, pipelines) read and write the project's annotations and configurations with their own credentials."
          >
            <UButton v-if="canManage" label="New machine" icon="i-lucide-plus" @click="creating = true" />
          </EmptyState>
          <div v-else class="overflow-x-auto rounded-md border border-default">
            <table class="w-full text-sm">
              <thead class="bg-elevated/50 text-xs text-muted">
                <tr>
                  <th class="px-3 py-2 text-start font-medium">Id</th>
                  <th class="px-3 py-2 text-start font-medium">Scope</th>
                  <th class="px-3 py-2 text-start font-medium">Annotation</th>
                  <th class="px-3 py-2 text-start font-medium">Credentials</th>
                  <th class="px-3 py-2 text-start font-medium">Certificate</th>
                  <th class="px-3 py-2 text-start font-medium">Renewed</th>
                  <th class="px-3 py-2" />
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                <tr v-for="machine in machines.data.value" :key="machine.Id">
                  <td class="px-3 py-2">
                    <RouterLink
                      :to="`/${context.org}/${context.project}/machines/${machine.Id}`"
                      class="font-mono text-xs text-primary hover:underline"
                    >
                      {{ machine.Id }}
                    </RouterLink>
                  </td>
                  <td class="px-3 py-2">
                    <UBadge :label="machine.Scope" color="neutral" variant="outline" size="sm" />
                  </td>
                  <td class="px-3 py-2 font-mono text-xs">{{ machine.AnnotationKey ?? 'any' }}</td>
                  <td class="px-3 py-2 text-xs">{{ credentialKindLabel(machine.CredentialKind) }}</td>
                  <td class="px-3 py-2">
                    <span v-if="machine.CertificateThumbprint" class="flex items-center gap-1">
                      <code class="font-mono text-xs" :title="machine.CertificateThumbprint"
                        >{{ machine.CertificateThumbprint.slice(0, 12) }}…</code
                      >
                      <UBadge
                        v-if="daysLeft(machine) !== undefined"
                        :label="daysLeft(machine)! < 0 ? 'expired' : `${daysLeft(machine)} d`"
                        :color="daysLeft(machine)! < 0 ? 'error' : daysLeft(machine)! <= 30 ? 'warning' : 'neutral'"
                        variant="subtle"
                        size="sm"
                      />
                    </span>
                    <UBadge
                      v-else-if="machine.UsesSharedCertificate"
                      label="shared"
                      icon="i-lucide-shield-check"
                      color="neutral"
                      variant="subtle"
                      size="sm"
                    />
                    <span v-else class="text-dimmed">–</span>
                  </td>
                  <td class="px-3 py-2"><RelativeTime :value="machine.LastRenewalAt" /></td>
                  <td class="px-3 py-2 text-end">
                    <span v-if="canManage" class="flex justify-end gap-1">
                      <!-- A Federated machine has no secret to reset; its rules change on its detail. -->
                      <UButton
                        v-if="hasSecret(machine.CredentialKind)"
                        label="Reset"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        @click="resetting = machine"
                      />
                      <UButton
                        icon="i-lucide-trash-2"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        :aria-label="`Delete ${machine.Id}`"
                        @click="deleting = machine"
                      />
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
        <section v-if="canAdmin" id="shared-certificates" tabindex="-1" class="scroll-mt-4 space-y-2 outline-none">
          <h2 class="text-xs font-semibold text-muted uppercase">Shared certificates</h2>
          <SharedCertificatesTable />
        </section>
        <section v-if="canAdmin" id="trusted-issuers" tabindex="-1" class="scroll-mt-4 space-y-2 outline-none">
          <h2 class="text-xs font-semibold text-muted uppercase">Trusted issuers</h2>
          <TrustedIssuersTable />
        </section>
      </div>

      <MachineCreateDialog v-model:open="creating" :policy="policy.data.value" @created="created" />
      <ConfirmDialog
        :open="!!resetting"
        :title="`Reset ${resetting?.Id}?`"
        description="Existing credentials of this machine stop working at once. New ones are shown once."
        confirm-label="Reset credentials"
        color="warning"
        :loading="busy"
        @update:open="(value) => !value && (resetting = undefined)"
        @confirm="reset"
      />
      <ConfirmDialog
        :open="!!deleting"
        :title="`Delete ${deleting?.Id}?`"
        description="The machine loses access immediately."
        :confirm-text="deleting?.Id"
        :loading="busy"
        @update:open="(value) => !value && (deleting = undefined)"
        @confirm="remove"
      />
      <OneTimeSecretDialog :secret="secret" @close="secret = undefined" />
      <FederatedSetupDialog :machine="setup" @close="setup = undefined" />
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
