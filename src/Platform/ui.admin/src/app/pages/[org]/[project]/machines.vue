<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { deleteMachineAccess, resetMachineAccess, type MachineAccess } from '@42for.net/2splatform.sdk'
import { useGetAccessPoint, useGetMachineAccesses } from '@42for.net/2splatform.sdk/vue-query'
import { useBootState } from '@/boot/state'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { certificateDaysLeft, issuesCertificates, machineSecret } from '@/utils/machines'
import type { OneTimeSecret } from '@/components/access/OneTimeSecretDialog.vue'

// Machine access of the project (spec section 13). Create and Reset return credentials once; they go straight
// to OneTimeSecretDialog and are never cached.
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
const accessPoint = useGetAccessPoint(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canAdmin.value, meta: { silent: true } },
})
const policy = computed(() => accessPoint.data.value?.MachineAuthentication)

const creating = ref(false)
const resetting = ref<MachineAccess>()
const deleting = ref<MachineAccess>()
const secret = ref<OneTimeSecret>()
const busy = ref(false)

async function refresh() {
  await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
}

function created(machine: MachineAccess) {
  secret.value = machineSecret(machine, 'Machine access created', boot.value.config?.apiBaseUrl)
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

const daysLeft = (machine: MachineAccess) => certificateDaysLeft(machine, policy.value?.CertificateLifetimeDays)
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
              <td class="px-3 py-2"><UBadge :label="machine.Scope" color="neutral" variant="outline" size="sm" /></td>
              <td class="px-3 py-2 font-mono text-xs">{{ machine.AnnotationKey ?? 'any' }}</td>
              <td class="px-3 py-2 text-xs">{{ machine.CredentialKind }}</td>
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
                <span v-else class="text-dimmed">–</span>
              </td>
              <td class="px-3 py-2"><RelativeTime :value="machine.LastRenewalAt" /></td>
              <td class="px-3 py-2 text-end">
                <span v-if="canManage" class="flex justify-end gap-1">
                  <UButton label="Reset" size="xs" color="neutral" variant="ghost" @click="resetting = machine" />
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

      <MachineCreateDialog
        v-model:open="creating"
        :issues-certificates="issuesCertificates(policy)"
        @created="created"
      />
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
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
