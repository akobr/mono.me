<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { MachineAccess } from '@42for.net/2splatform.sdk'

export interface OneTimeSecret {
  title: string
  /** Shown as an identifier, not secret (machine id, certificate thumbprint; the client ID for ClientCredentials). */
  id?: string
  /** The API key, or the client secret for ClientCredentials (MachineAccess.AccessKey). */
  apiKey?: string
  /** Decides the labels and snippets: a client secret is exchanged for a token, never sent as an API key. */
  credentialKind?: MachineAccess['CredentialKind']
  /** Base64 PKCS#12 (.pfx). */
  certificate?: string
  certificatePassword?: string
  /** A CertificateAndApiKey machine without a certificate of its own: it presents a shared certificate of the project. */
  sharedCertificate?: boolean
  tokenEndpoint?: string
  tokenScope?: string
  fileName?: string
  baseUrl?: string
}

/**
 * Credentials that the API shows exactly once (machine create and reset, certificate issue; spec section 13).
 * They live only in this dialog: never in the query cache, a store or the logs. The dialog cannot be dismissed
 * until the user confirms they stored them.
 */
const props = defineProps<{ secret?: OneTimeSecret }>()
const emit = defineEmits<{ close: [] }>()

const stored = ref(false)
watch(
  () => props.secret,
  () => (stored.value = false),
)

const open = computed(() => !!props.secret)
const clientCredentials = computed(() => props.secret?.credentialKind === 'ClientCredentials')
// A reset never issues a certificate again (424-82): a Certificate machine has no Reset, a CertificateAndApiKey reset
// replaces only the API key, and a shared certificate (no kind) is replaced by issuing another one.
const discardHint = computed(() => {
  const secret = props.secret
  if (!secret?.certificate) {
    // A CertificateAndApiKey reset response, or a machine on a shared certificate: only the API key is shown.
    return secret?.credentialKind === 'CertificateAndApiKey'
      ? 'Closing this dialog discards them; a reset issues a new API key, and the certificate stays valid.'
      : 'Closing this dialog discards them; a reset issues new ones.'
  }
  if (!secret.credentialKind) {
    return 'Closing this dialog discards them; to replace the certificate, issue another one.'
  }
  return secret.apiKey
    ? 'Closing this dialog discards them; a reset issues a new API key but not a new certificate: to replace the certificate, create a new machine.'
    : 'Closing this dialog discards them; to replace the certificate, create a new machine.'
})

function downloadPfx() {
  if (!props.secret?.certificate) {
    return
  }
  const bytes = Uint8Array.from(atob(props.secret.certificate), (char) => char.charCodeAt(0))
  const url = URL.createObjectURL(new Blob([bytes], { type: 'application/x-pkcs12' }))
  const link = document.createElement('a')
  link.href = url
  link.download = props.secret.fileName ?? 'certificate.pfx'
  link.click()
  URL.revokeObjectURL(url)
}

const snippets = computed(() => {
  const secret = props.secret
  if (!secret) {
    return []
  }
  const base = secret.baseUrl ?? 'https://<api-host>/api'
  const result: { label: string; code: string }[] = []
  if (clientCredentials.value) {
    // A reset response omits the token endpoint and scope (MachineAccess: create only), hence the placeholders.
    result.push({
      label: 'Token request',
      code:
        `curl -X POST ${secret.tokenEndpoint ?? '<token-endpoint>'} \\\n` +
        `  -d grant_type=client_credentials -d client_id=${secret.id ?? '<client-id>'} \\\n` +
        `  -d client_secret=${secret.apiKey ?? '<client-secret>'} -d scope=${secret.tokenScope ?? '<scope>'}`,
    })
    result.push({ label: 'curl', code: `curl -H "Authorization: Bearer <access_token>" ${base}/v1/...` })
  } else if (secret.apiKey) {
    result.push({ label: 'curl', code: `curl -H "Authorization: ApiKey ${secret.apiKey}" ${base}/v1/...` })
    result.push({ label: '.env', code: `STORYTELLER_ENDPOINT=${base}\nSTORYTELLER_API_KEY=${secret.apiKey}` })
  }
  return result
})

function close() {
  if (stored.value) {
    emit('close')
  }
}
</script>

<template>
  <UModal
    class="ph-no-capture"
    :open="open"
    :title="secret?.title"
    description="These credentials are shown only once. Store them now."
    :dismissible="false"
    :close="false"
    :ui="{ content: 'max-w-2xl' }"
  >
    <template #body>
      <div v-if="secret" class="space-y-4">
        <UAlert
          color="warning"
          variant="subtle"
          icon="i-lucide-key-round"
          title="Shown only once"
          :description="discardHint"
        />

        <div v-if="secret.id" class="text-sm">
          <span class="text-muted">{{ clientCredentials ? 'Client ID' : 'Id' }}</span>
          <code class="ms-2 font-mono">{{ secret.id }}</code>
        </div>

        <UFormField v-if="secret.apiKey" :label="clientCredentials ? 'Client secret' : 'API key'">
          <div class="flex items-center gap-2">
            <code class="min-w-0 flex-1 truncate rounded bg-elevated px-2 py-1.5 font-mono text-xs">{{
              secret.apiKey
            }}</code>
            <CopyButton :value="secret.apiKey" label="Copy" secret size="sm" />
          </div>
        </UFormField>

        <UFormField v-if="secret.certificate" label="Certificate">
          <UButton
            label="Download .pfx"
            icon="i-lucide-download"
            color="neutral"
            variant="outline"
            @click="downloadPfx"
          />
        </UFormField>

        <UAlert
          v-if="secret.sharedCertificate"
          color="info"
          variant="subtle"
          icon="i-lucide-shield-check"
          title="Uses a shared certificate"
          description="No certificate was issued for this machine. It signs in with one of the project's shared certificates (Machines page) together with this API key."
        />

        <UFormField v-if="secret.certificatePassword" label="Certificate password">
          <div class="flex items-center gap-2">
            <code class="min-w-0 flex-1 truncate rounded bg-elevated px-2 py-1.5 font-mono text-xs">{{
              secret.certificatePassword
            }}</code>
            <CopyButton :value="secret.certificatePassword" label="Copy" secret size="sm" />
          </div>
        </UFormField>

        <div v-if="secret.tokenEndpoint" class="grid gap-2 text-sm sm:grid-cols-2">
          <UFormField label="Token endpoint"
            ><code class="font-mono text-xs break-all">{{ secret.tokenEndpoint }}</code></UFormField
          >
          <UFormField label="Token scope"
            ><code class="font-mono text-xs break-all">{{ secret.tokenScope }}</code></UFormField
          >
        </div>

        <details v-if="snippets.length" class="text-sm">
          <summary class="cursor-pointer text-muted">Ready-to-paste snippets</summary>
          <div v-for="snippet in snippets" :key="snippet.label" class="mt-2">
            <div class="flex items-center justify-between">
              <span class="text-xs font-medium">{{ snippet.label }}</span>
              <CopyButton :value="snippet.code" secret />
            </div>
            <pre class="overflow-auto rounded bg-elevated p-2 font-mono text-xs">{{ snippet.code }}</pre>
          </div>
        </details>

        <UCheckbox v-model="stored" label="I have stored these credentials" />
      </div>
    </template>
    <template #footer>
      <div class="flex w-full justify-end">
        <UButton label="Done" :disabled="!stored" @click="close" />
      </div>
    </template>
  </UModal>
</template>
