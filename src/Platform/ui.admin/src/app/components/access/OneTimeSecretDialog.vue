<script setup lang="ts">
import { computed, ref, watch } from 'vue'

export interface OneTimeSecret {
  title: string
  /** Shown as an identifier, not secret (machine id, certificate thumbprint). */
  id?: string
  apiKey?: string
  /** Base64 PKCS#12 (.pfx). */
  certificate?: string
  certificatePassword?: string
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
  if (secret.apiKey) {
    result.push({ label: 'curl', code: `curl -H "Authorization: ApiKey ${secret.apiKey}" ${base}/v1/...` })
    result.push({ label: '.env', code: `STORYTELLER_API_URL=${base}\nSTORYTELLER_API_KEY=${secret.apiKey}` })
  }
  if (secret.tokenEndpoint) {
    result.push({
      label: 'Client credentials',
      code: `curl -X POST ${secret.tokenEndpoint} \\\n  -d grant_type=client_credentials -d scope=${secret.tokenScope ?? ''}`,
    })
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
          description="Closing this dialog discards them; a reset issues new ones."
        />

        <div v-if="secret.id" class="text-sm">
          <span class="text-muted">Id</span>
          <code class="ms-2 font-mono">{{ secret.id }}</code>
        </div>

        <UFormField v-if="secret.apiKey" label="API key">
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
