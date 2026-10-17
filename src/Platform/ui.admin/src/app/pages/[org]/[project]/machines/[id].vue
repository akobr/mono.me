<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FederationRule, MachineAccess } from '@42for.net/2splatform.sdk'
import { useGetFederatedIssuers, useGetMachineAccess } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { audienceDiffers } from '@/utils/federation'
import { credentialKindLabel, isFederated, scopeDescriptions } from '@/utils/machines'

// One machine access: scope, restriction, credential kind and certificate (with its lifetime and expiry). Reset and
// delete live in the list. A Federated machine (424-46) shows its token endpoint and federation rules instead of a
// certificate; Contributors edit the rules here (FederationRulesDialog) and can show the setup again. A rule whose
// issuer was deleted, or whose audience is not the token endpoint (the UI presets it from the address it calls the API
// at, which a proxy or the API's public base URL can change), is flagged.
const route = useRoute()
const router = useRouter()
const context = useContextStore()
const canManage = useCan('machines')

const id = computed(() => String((route.params as { id: string }).id))
const machine = useGetMachineAccess(
  () => context.org ?? '',
  () => context.project ?? '',
  id,
  { query: { meta: { silent: true } } },
)
const federated = computed(() => isFederated(machine.data.value?.CredentialKind))

// The trusted issuers tell which rules name an issuer that was deleted (those rules match no token), and the platform
// of the others: all but Azure's request their tokens for the token endpoint.
const issuers = useGetFederatedIssuers(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => federated.value && canManage.value, meta: { silent: true } } },
)
const missingIssuer = (name?: string) =>
  issuers.isSuccess.value && !issuers.data.value?.some((issuer) => issuer.Name === name)
const otherAudience = (rule: FederationRule) =>
  issuers.isSuccess.value && audienceDiffers(rule, issuers.data.value, machine.data.value?.TokenEndpoint)

const editingRules = ref(false)
const setup = ref<MachineAccess>()

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
    <div class="space-y-6 p-4">
      <LoadingState v-if="machine.isPending.value" />
      <ErrorState v-else-if="machine.isError.value" :error="machine.error.value" @retry="machine.refetch()" />
      <template v-else>
        <dl class="grid grid-cols-[10rem_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
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
          <dd>{{ credentialKindLabel(machine.data.value?.CredentialKind) }}</dd>
          <dt class="text-muted">Object id</dt>
          <dd class="font-mono text-xs break-all">{{ machine.data.value?.ObjectId ?? '–' }}</dd>
          <template v-if="!federated">
            <dt class="text-muted">Certificate</dt>
            <dd v-if="machine.data.value?.UsesSharedCertificate">A shared certificate of the project</dd>
            <dd v-else class="font-mono text-xs break-all">{{ machine.data.value?.CertificateThumbprint ?? '–' }}</dd>
          </template>
          <template v-if="machine.data.value?.CertificateLifetimeDays">
            <dt class="text-muted">Certificate lifetime</dt>
            <dd>{{ machine.data.value.CertificateLifetimeDays }} days</dd>
          </template>
          <template v-if="machine.data.value?.CertificateNotAfter">
            <dt class="text-muted">Certificate expires</dt>
            <dd><RelativeTime :value="machine.data.value.CertificateNotAfter" /></dd>
          </template>
          <template v-if="!federated">
            <dt class="text-muted">Last renewal</dt>
            <dd><RelativeTime :value="machine.data.value?.LastRenewalAt" /></dd>
          </template>
          <template v-if="machine.data.value?.TokenEndpoint">
            <dt class="text-muted">Token endpoint</dt>
            <dd class="font-mono text-xs break-all">{{ machine.data.value.TokenEndpoint }}</dd>
          </template>
        </dl>

        <section v-if="federated" aria-labelledby="federation-rules" class="space-y-2">
          <div class="flex items-center justify-between gap-2">
            <h3 id="federation-rules" class="text-xs font-semibold text-muted uppercase">Federation rules</h3>
            <span class="flex gap-1">
              <UButton
                label="Setup"
                icon="i-lucide-terminal"
                size="xs"
                color="neutral"
                variant="ghost"
                @click="setup = machine.data.value"
              />
              <UButton
                v-if="canManage"
                label="Edit rules"
                icon="i-lucide-pencil"
                size="xs"
                color="neutral"
                variant="outline"
                @click="editingRules = true"
              />
            </span>
          </div>
          <p class="text-xs text-muted">A platform token is exchanged when it matches one of the rules.</p>
          <ul class="space-y-2">
            <li
              v-for="(rule, index) in machine.data.value?.FederationRules ?? []"
              :key="index"
              class="space-y-1 rounded-md border border-default p-3 text-sm"
            >
              <div class="flex items-center gap-2">
                <span class="font-medium">Rule {{ index + 1 }}</span>
                <UBadge :label="rule.Issuer" color="neutral" variant="subtle" size="sm" class="font-mono" />
                <UBadge
                  v-if="missingIssuer(rule.Issuer)"
                  label="issuer deleted"
                  icon="i-lucide-triangle-alert"
                  color="warning"
                  variant="subtle"
                  size="sm"
                />
              </div>
              <dl class="grid grid-cols-[6rem_minmax(0,1fr)] gap-x-3 gap-y-1 text-xs">
                <dt class="text-muted">Audience</dt>
                <dd class="font-mono break-all">
                  {{ rule.Audience }}
                  <p v-if="otherAudience(rule)" class="mt-0.5 font-sans text-warning">
                    Not the token endpoint: request the platform token with exactly this audience, or change it with
                    Edit rules.
                  </p>
                </dd>
                <dt class="text-muted">Subject</dt>
                <dd class="font-mono break-all">{{ rule.Subject }}</dd>
                <template v-for="(values, claim) in rule.Claims" :key="claim">
                  <dt class="font-mono break-all text-muted">{{ claim }}</dt>
                  <dd class="font-mono break-all">{{ values.join(' or ') }}</dd>
                </template>
              </dl>
            </li>
          </ul>
        </section>
      </template>
    </div>

    <FederationRulesDialog
      v-if="machine.data.value && federated"
      v-model:open="editingRules"
      :machine="machine.data.value"
    />
    <FederatedSetupDialog :machine="setup" :title="`Set up ${id}`" @close="setup = undefined" />
  </DetailPanel>
</template>
