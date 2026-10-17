<script setup lang="ts">
import { computed } from 'vue'
import type { MachineAccess } from '@42for.net/2splatform.sdk'
import { useGetFederatedIssuers } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { audienceDiffers, githubAudience } from '@/utils/federation'
import { apiBaseUrl } from '@/utils/machines'

/**
 * How a workload uses a Federated machine (424-46): shown after the machine is created, instead of the one-time secret
 * dialog, and from the machine's detail. A Federated machine has no secret: the workload gets a token of its platform
 * with the rule's audience, exchanges it at the project's token endpoint with the machine id as client_id, and sends
 * the short-lived Storyteller token as a bearer token. Nothing here needs to be kept secret.
 *
 * The API checks a token only against the audiences of its issuer's rules, so each snippet names the audience of the
 * rules it is for: the curl exchange lists them per trusted issuer when they differ, and the GitHub Actions step (only
 * for a machine with a rule of a GitHub issuer) uses that rule's. A rule whose audience is not the token endpoint is
 * flagged.
 */
const props = withDefaults(defineProps<{ machine?: MachineAccess; title?: string }>(), {
  machine: undefined,
  title: 'Federated machine created',
})
const emit = defineEmits<{ close: [] }>()

const context = useContextStore()

const open = computed(() => !!props.machine)
const id = computed(() => props.machine?.Id ?? '<machine-id>')
const endpoint = computed(() => props.machine?.TokenEndpoint ?? '<token-endpoint>')
const rules = computed(() => props.machine?.FederationRules ?? [])

// The platform of each rule comes from its trusted issuer (Contributors read them). Until they are known, or when they
// cannot be read, the GitHub step takes the rule whose audience is the token endpoint and no rule is flagged.
const issuers = useGetFederatedIssuers(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => open.value && !!context.project, meta: { silent: true } } },
)
const knownIssuers = computed(() => (issuers.isSuccess.value ? (issuers.data.value ?? []) : undefined))
const github = computed(() => (props.machine ? githubAudience(props.machine, knownIssuers.value) : undefined))
const differs = (index: number) =>
  audienceDiffers(rules.value[index]!, knownIssuers.value, props.machine?.TokenEndpoint)

/** The audience to request the platform token with: the one of every rule, or one line per issuer and audience. */
const audienceComment = computed(() => {
  const pairs = [...new Map(rules.value.map((rule) => [JSON.stringify([rule.Issuer, rule.Audience]), rule])).values()]
  if (new Set(pairs.map((rule) => rule.Audience)).size <= 1) {
    const issuer = new Set(pairs.map((rule) => rule.Issuer)).size > 1 ? 'a trusted issuer' : 'the trusted issuer'
    return `# PLATFORM_TOKEN: a token of ${issuer}, requested with the audience ${pairs[0]?.Audience ?? endpoint.value}\n`
  }
  return (
    '# PLATFORM_TOKEN: a token of a trusted issuer, requested with the audience of its rule:\n' +
    pairs.map((rule) => `#   ${rule.Issuer}: ${rule.Audience}\n`).join('')
  )
})

const exchange = 'urn:ietf:params:oauth:grant-type:token-exchange'
const jwtType = 'urn:ietf:params:oauth:token-type:jwt'

const snippets = computed(() => [
  {
    label: 'Token exchange (curl)',
    code:
      audienceComment.value +
      `STORYTELLER_TOKEN=$(curl -sf ${endpoint.value} \\\n` +
      `  -d grant_type=${exchange} \\\n` +
      `  -d client_id=${id.value} \\\n` +
      `  -d subject_token_type=${jwtType} \\\n` +
      `  --data-urlencode "subject_token=$PLATFORM_TOKEN" | jq -r .access_token)\n\n` +
      `curl -H "Authorization: Bearer $STORYTELLER_TOKEN" ${apiBaseUrl()}/v1/...`,
  },
  ...(github.value === undefined
    ? []
    : [
        {
          label: 'GitHub Actions step',
          code:
            `permissions:\n  id-token: write\n  contents: read\n\n` +
            `steps:\n` +
            `  - name: Get a Storyteller token\n` +
            `    env:\n` +
            `      TOKEN_ENDPOINT: ${endpoint.value}\n` +
            `      AUDIENCE: ${github.value}\n` +
            `      MACHINE_ID: ${id.value}\n` +
            `    run: |\n` +
            `      PLATFORM_TOKEN=$(curl -sf -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \\\n` +
            `        "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=$(jq -rn --arg a "$AUDIENCE" '$a|@uri')" | jq -r .value)\n` +
            `      STORYTELLER_TOKEN=$(curl -sf "$TOKEN_ENDPOINT" \\\n` +
            `        -d grant_type=${exchange} \\\n` +
            `        -d client_id=$MACHINE_ID \\\n` +
            `        -d subject_token_type=${jwtType} \\\n` +
            `        --data-urlencode "subject_token=$PLATFORM_TOKEN" | jq -r .access_token)\n` +
            `      echo "::add-mask::$STORYTELLER_TOKEN"\n` +
            `      echo "STORYTELLER_TOKEN=$STORYTELLER_TOKEN" >> "$GITHUB_ENV"`,
        },
      ]),
])
</script>

<template>
  <UModal
    :open="open"
    :title="title"
    description="This machine has no secret: its workload exchanges a token of a trusted issuer for a short-lived Storyteller token."
    :ui="{ content: 'max-w-2xl' }"
    @update:open="(value) => !value && emit('close')"
  >
    <template #body>
      <div v-if="machine" class="space-y-4">
        <div class="grid gap-3 text-sm sm:grid-cols-2">
          <UFormField label="Machine id (client_id)">
            <div class="flex items-center gap-1">
              <code class="min-w-0 flex-1 font-mono text-xs break-all">{{ machine.Id }}</code>
              <CopyButton :value="machine.Id ?? ''" />
            </div>
          </UFormField>
          <UFormField label="Token endpoint">
            <div class="flex items-center gap-1">
              <code class="min-w-0 flex-1 font-mono text-xs break-all">{{ endpoint }}</code>
              <CopyButton :value="endpoint" />
            </div>
          </UFormField>
        </div>

        <div v-if="rules.length" class="text-sm">
          <p class="font-medium">Accepted tokens</p>
          <ul class="mt-1 space-y-1 text-xs">
            <li v-for="(rule, index) in rules" :key="index" class="break-all">
              <span class="text-muted">Rule {{ index + 1 }}:</span>
              {{ `${rule.Issuer}, audience ${rule.Audience}, subject ${rule.Subject}` }}
              <span v-if="differs(index)" class="text-warning">
                (not the token endpoint: request the platform token with exactly this audience)
              </span>
            </li>
          </ul>
        </div>

        <div v-for="snippet in snippets" :key="snippet.label">
          <div class="flex items-center justify-between">
            <span class="text-xs font-medium">{{ snippet.label }}</span>
            <CopyButton :value="snippet.code" />
          </div>
          <!-- Focusable, so the long lines can be scrolled with the keyboard. -->
          <pre
            tabindex="0"
            role="region"
            :aria-label="snippet.label"
            class="overflow-auto rounded bg-elevated p-2 font-mono text-xs"
            >{{ snippet.code }}</pre>
        </div>
      </div>
    </template>
    <template #footer>
      <div class="flex w-full justify-end">
        <UButton label="Done" @click="emit('close')" />
      </div>
    </template>
  </UModal>
</template>
