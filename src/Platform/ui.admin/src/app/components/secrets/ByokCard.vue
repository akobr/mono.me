<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { getByokSetup, setKeyValuePolicy, verifyByok, type ByokSetupInfo, type KeyValuePolicy } from '@42for.net/2splatform.sdk'
import { invalidateKeyValuePolicy } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

/**
 * Bring your own key (spec 29.6): the consent link and the role to grant, the key URI and tenant, the verification
 * and the health of the customer key.
 */
const props = defineProps<{ policy: KeyValuePolicy; editable: boolean }>()

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()

const setup = ref<ByokSetupInfo>()
const setupError = ref<string>()
const keyUri = ref('')
const tenantId = ref('')
const saving = ref(false)
const verifying = ref(false)
const failure = ref<string>()

watch(
  () => props.policy.Byok,
  (byok) => {
    keyUri.value = byok?.KeyUri ?? ''
    tenantId.value = byok?.TenantId ?? ''
  },
  { immediate: true },
)

async function loadSetup() {
  if (!context.org || !context.project || setup.value) {
    return
  }
  try {
    setup.value = await getByokSetup(context.org, context.project)
  } catch (error) {
    setupError.value = errorMessage(error)
  }
}

const azCommand = computed(() =>
  setup.value
    ? `az role assignment create --assignee <service principal object id> --role "${setup.value.RequiredRole}" --scope <resource id of the key vault or key>`
    : '',
)

const dirty = computed(() => keyUri.value !== (props.policy.Byok?.KeyUri ?? '') || tenantId.value !== (props.policy.Byok?.TenantId ?? ''))

async function saveKey() {
  if (!context.org || !context.project) {
    return
  }
  saving.value = true
  failure.value = undefined
  try {
    await setKeyValuePolicy(context.org, context.project, {
      Byok: { KeyUri: keyUri.value.trim(), TenantId: tenantId.value.trim() },
      ExpectedVersion: props.policy.Version,
    })
    await invalidateKeyValuePolicy(queryClient, context.org, context.project)
    toast.add({ title: 'Customer key saved; verify it next', color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    saving.value = false
  }
}

async function verify() {
  if (!context.org || !context.project) {
    return
  }
  verifying.value = true
  failure.value = undefined
  try {
    const policy = await verifyByok(context.org, context.project)
    await invalidateKeyValuePolicy(queryClient, context.org, context.project)
    toast.add({
      title: `Customer key ${policy.Byok?.Health === 'Healthy' ? 'verified' : policy.Byok?.Health ?? 'checked'}`,
      color: policy.Byok?.Health === 'Healthy' ? 'success' : 'warning',
      icon: 'i-lucide-key-square',
    })
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    verifying.value = false
  }
}

const health = computed(() => props.policy.Byok?.Health)
const healthColor = computed(() => (health.value === 'Healthy' ? 'success' : health.value === 'Unavailable' ? 'error' : 'neutral'))
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="font-semibold">Bring your own key</h2>
        <UBadge v-if="policy.Byok" :label="health ?? 'Unknown'" :color="healthColor" variant="subtle" size="sm" icon="i-lucide-key-square" />
      </div>
    </template>
    <div class="space-y-4 text-sm">
      <p class="text-muted">
        Your key stays in your Key Vault and tenant; the platform wraps and unwraps data keys with it through a
        multi-tenant application. Disabling the key makes every BYOK value unreadable within minutes.
      </p>

      <section class="space-y-2">
        <h3 class="font-medium">1. Consent and role</h3>
        <UButton v-if="!setup && !setupError" label="Show the setup" color="neutral" variant="outline" size="sm" @click="loadSetup" />
        <UAlert v-if="setupError" color="warning" variant="subtle" icon="i-lucide-circle-alert" :description="setupError" />
        <div v-if="setup" class="space-y-2">
          <p>An administrator of your tenant consents to the platform's application (id <code class="font-mono text-xs">{{ setup.ClientId }}</code>):</p>
          <div class="flex items-center gap-2">
            <a :href="setup.ConsentUrl" target="_blank" rel="noopener" class="truncate text-primary underline">{{ setup.ConsentUrl }}</a>
            <CopyButton :value="setup.ConsentUrl ?? ''" />
          </div>
          <p>Then grant its service principal <strong>{{ setup.RequiredRole }}</strong> on the key, and let the platform reach the vault (a private endpoint, or a firewall rule for its egress IP):</p>
          <div class="flex items-start gap-2">
            <code class="min-w-0 flex-1 whitespace-pre-wrap break-all rounded-md bg-elevated/50 p-2 font-mono text-xs">{{ azCommand }}</code>
            <CopyButton :value="azCommand" />
          </div>
        </div>
      </section>

      <section class="space-y-2">
        <h3 class="font-medium">2. The key</h3>
        <UFormField label="Key URI (versionless)">
          <UInput v-model="keyUri" class="w-full font-mono" placeholder="https://my-vault.vault.azure.net/keys/storyteller-kek" :disabled="!editable" />
        </UFormField>
        <UFormField label="Tenant id">
          <UInput v-model="tenantId" class="w-full max-w-md font-mono" placeholder="00000000-0000-0000-0000-000000000000" :disabled="!editable" />
        </UFormField>
        <div v-if="editable" class="flex justify-end">
          <UButton label="Save key" :disabled="!dirty || !keyUri.trim() || !tenantId.trim()" :loading="saving" @click="saveKey" />
        </div>
      </section>

      <section class="space-y-2">
        <h3 class="font-medium">3. Verify</h3>
        <dl v-if="policy.Byok" class="grid grid-cols-[9rem_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs">
          <dt class="text-muted">Health</dt>
          <dd>{{ policy.Byok.Health ?? 'Unknown' }}</dd>
          <dt class="text-muted">Verified</dt>
          <dd>{{ policy.Byok.VerifiedAt ? formatDateTime(new Date(policy.Byok.VerifiedAt)) : 'not yet' }}</dd>
          <dt class="text-muted">Key version</dt>
          <dd class="font-mono">{{ policy.Byok.CurrentKeyVersion ?? '—' }}</dd>
          <dt class="text-muted">Last check</dt>
          <dd>{{ policy.Byok.LastCheckedAt ? formatDateTime(new Date(policy.Byok.LastCheckedAt)) : '—' }}</dd>
          <template v-if="policy.Byok.LastError">
            <dt class="text-muted">Last error</dt>
            <dd class="text-error">{{ policy.Byok.LastError }}</dd>
          </template>
        </dl>
        <p v-else class="text-xs text-muted">Save the key first.</p>
        <UButton v-if="editable && policy.Byok" label="Verify" icon="i-lucide-shield-check" color="neutral" variant="outline" size="sm" :loading="verifying" @click="verify" />
      </section>

      <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
    </div>
  </UCard>
</template>
