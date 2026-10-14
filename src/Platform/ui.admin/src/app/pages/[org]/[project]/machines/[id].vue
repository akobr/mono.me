<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useGetMachineAccess } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { scopeDescriptions } from '@/utils/machines'

// One machine access: scope, restriction, credential kind and certificate (with its lifetime and expiry). Reset and
// delete live in the list.
const route = useRoute()
const router = useRouter()
const context = useContextStore()

const id = computed(() => String((route.params as { id: string }).id))
const machine = useGetMachineAccess(
  () => context.org ?? '',
  () => context.project ?? '',
  id,
  { query: { meta: { silent: true } } },
)

function close() {
  void router.push(`/${context.org}/${context.project}/machines`)
}
</script>

<template>
  <DetailPanel id="machine-detail" :title="id" @close="close">
    <template #header="{ close: closePanel }">
      <div class="flex items-center gap-2 border-b border-default px-4 py-3">
        <UIcon name="i-lucide-computer" class="size-5 text-muted" />
        <h2 class="flex-1 truncate font-mono text-sm font-semibold">{{ id }}</h2>
        <DetailPanelActions />
        <UButton icon="i-lucide-x" color="neutral" variant="ghost" size="sm" aria-label="Close" @click="closePanel" />
      </div>
    </template>
    <div class="p-4">
      <LoadingState v-if="machine.isPending.value" />
      <ErrorState v-else-if="machine.isError.value" :error="machine.error.value" @retry="machine.refetch()" />
      <dl v-else class="grid grid-cols-[10rem_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
        <dt class="text-muted">Scope</dt>
        <dd>
          <UBadge :label="machine.data.value?.Scope" color="neutral" variant="outline" size="sm" />
          <p class="mt-1 text-xs text-muted">
            {{ machine.data.value?.Scope ? scopeDescriptions[machine.data.value.Scope] : '' }}
          </p>
        </dd>
        <dt class="text-muted">Annotation</dt>
        <dd class="font-mono text-xs">{{ machine.data.value?.AnnotationKey ?? 'any annotation' }}</dd>
        <dt class="text-muted">Credentials</dt>
        <dd>{{ machine.data.value?.CredentialKind }}</dd>
        <dt class="text-muted">Object id</dt>
        <dd class="font-mono text-xs break-all">{{ machine.data.value?.ObjectId ?? '–' }}</dd>
        <dt class="text-muted">Certificate</dt>
        <dd class="font-mono text-xs break-all">{{ machine.data.value?.CertificateThumbprint ?? '–' }}</dd>
        <template v-if="machine.data.value?.CertificateLifetimeDays">
          <dt class="text-muted">Certificate lifetime</dt>
          <dd>{{ machine.data.value.CertificateLifetimeDays }} days</dd>
        </template>
        <template v-if="machine.data.value?.CertificateNotAfter">
          <dt class="text-muted">Certificate expires</dt>
          <dd><RelativeTime :value="machine.data.value.CertificateNotAfter" /></dd>
        </template>
        <dt class="text-muted">Last renewal</dt>
        <dd><RelativeTime :value="machine.data.value?.LastRenewalAt" /></dd>
        <template v-if="machine.data.value?.TokenEndpoint">
          <dt class="text-muted">Token endpoint</dt>
          <dd class="font-mono text-xs break-all">{{ machine.data.value.TokenEndpoint }}</dd>
        </template>
      </dl>
    </div>
  </DetailPanel>
</template>
