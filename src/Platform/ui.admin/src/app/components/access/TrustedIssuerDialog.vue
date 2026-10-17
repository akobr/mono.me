<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { setFederatedIssuer, type FederatedIssuer, type FederatedIssuerKeySource } from '@42for.net/2splatform.sdk'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import {
  claimMap,
  claimRowError,
  claimRows,
  issuerPlatform,
  issuerTemplates,
  keySourceDescriptions,
  keySourceLabels,
  placeholderIn,
  type ClaimRow,
  type FederationPlatform,
} from '@/utils/federation'

/**
 * Adds or edits a trusted issuer of the project (Administrator, 424-46). A template per platform fills the issuer, the
 * key source, the longest accepted token lifetime and the required claims, and tells what to fill in; `<placeholders>`
 * must be replaced before saving. The issuer of an existing trusted issuer is read-only: its rules were written for it.
 * The API validates the rest; its 400 (an invalid issuer, the 20-issuer limit) and 409 (rules of machines still
 * reference a deleted issuer of that name) messages show in the dialog.
 */
const props = defineProps<{ issuer?: FederatedIssuer; names?: string[] }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [issuer: FederatedIssuer] }>()

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()

const namePattern = /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/
const jwksPlaceholder = '{ "keys": [ { "kty": "RSA", "kid": "…", "n": "…", "e": "AQAB" } ] }'

const editing = computed(() => !!props.issuer)
const platform = ref<FederationPlatform>('github')
const name = ref('')
const issuerUrl = ref('')
const keySource = ref<FederatedIssuerKeySource>('Discovery')
const jwksUri = ref('')
const jwks = ref('')
const lifetime = ref<number | null>(null)
const requiredClaims = ref<ClaimRow[]>([])
const description = ref('')
const saving = ref(false)
const failure = ref<string>()

const template = computed(() => issuerTemplates.find((entry) => entry.platform === platform.value)!)
const templateItems = issuerTemplates.map((entry) => ({ label: entry.label, value: entry.platform, icon: entry.icon }))
const keySourceItems = (Object.keys(keySourceLabels) as FederatedIssuerKeySource[]).map((value) => ({
  label: keySourceLabels[value],
  value,
  description: keySourceDescriptions[value],
}))

/** The template's name, or with a number when the project already has a trusted issuer of that name. */
function suggestedName(base: string): string {
  const taken = new Set(props.names ?? [])
  if (!base || !taken.has(base)) {
    return base
  }
  let number = 2
  while (taken.has(`${base}-${number}`)) {
    number++
  }
  return `${base}-${number}`
}

function applyTemplate(value: FederationPlatform) {
  const previous = template.value
  platform.value = value
  const next = template.value
  // A name the user typed stays; one that came from the previous template follows the new one.
  if (!name.value || name.value === suggestedName(previous.name)) {
    name.value = suggestedName(next.name)
  }
  issuerUrl.value = next.issuer
  keySource.value = next.keySource
  jwksUri.value = ''
  jwks.value = ''
  lifetime.value = next.maxTokenLifetimeSeconds
  requiredClaims.value = next.requiredClaims.map((row) => ({ ...row }))
}

watch(open, (value) => {
  if (!value) {
    return
  }
  failure.value = undefined
  const issuer = props.issuer
  if (issuer) {
    platform.value = issuerPlatform(issuer)
    name.value = issuer.Name ?? ''
    issuerUrl.value = issuer.Issuer ?? ''
    keySource.value = issuer.KeySource ?? 'Discovery'
    jwksUri.value = issuer.JwksUri ?? ''
    jwks.value = issuer.Jwks ?? ''
    lifetime.value = issuer.MaxTokenLifetimeSeconds ?? null
    requiredClaims.value = claimRows(issuer.RequiredClaims)
    description.value = issuer.Description ?? ''
  } else {
    name.value = ''
    description.value = ''
    applyTemplate('github')
  }
})

const nameError = computed(() => {
  const value = name.value.trim()
  if (editing.value || !value) {
    return undefined
  }
  if (!namePattern.test(value)) {
    return 'Use 1 to 63 lower-case letters, digits or hyphens, starting and ending with a letter or a digit.'
  }
  return props.names?.includes(value) ? `The trusted issuer ${value} exists already; edit it in the list.` : undefined
})
const issuerPlaceholder = computed(() => (editing.value ? undefined : placeholderIn(issuerUrl.value)))
const issuerError = computed(() =>
  issuerPlaceholder.value ? `Replace ${issuerPlaceholder.value} with the value of your platform.` : undefined,
)

const complete = computed(
  () =>
    !!name.value.trim() &&
    !nameError.value &&
    !!issuerUrl.value.trim() &&
    !issuerError.value &&
    (keySource.value !== 'JwksUri' || !!jwksUri.value.trim()) &&
    (keySource.value !== 'Jwks' || !!jwks.value.trim()) &&
    requiredClaims.value.every((row) => !claimRowError(row)),
)

async function save() {
  saving.value = true
  failure.value = undefined
  const issuerName = name.value.trim()
  try {
    const stored = await setFederatedIssuer(context.org!, context.project!, issuerName, {
      Name: issuerName,
      // An existing issuer is sent as it is stored: the API compares it ordinally and refuses any change.
      Issuer: editing.value ? props.issuer!.Issuer : issuerUrl.value.trim(),
      KeySource: keySource.value,
      JwksUri: keySource.value === 'JwksUri' ? jwksUri.value.trim() : undefined,
      Jwks: keySource.value === 'Jwks' ? jwks.value.trim() : undefined,
      MaxTokenLifetimeSeconds: lifetime.value ?? null,
      RequiredClaims: claimMap(requiredClaims.value),
      Description: description.value.trim() || undefined,
    })
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
    toast.add({ title: `Trusted issuer ${issuerName} saved`, color: 'success', icon: 'i-lucide-check' })
    open.value = false
    emit('saved', stored)
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    :title="editing ? `Edit trusted issuer ${issuer?.Name}` : 'Add a trusted issuer'"
    description="Federated machines of the project exchange tokens of this issuer for Storyteller tokens."
    :ui="{ content: 'max-w-2xl' }"
  >
    <template #body>
      <form id="trusted-issuer" class="space-y-4" @submit.prevent="complete && save()">
        <UFormField v-if="!editing" label="Template">
          <USelect
            :model-value="platform"
            :items="templateItems"
            class="w-full"
            @update:model-value="(value) => applyTemplate(value as FederationPlatform)"
          />
        </UFormField>
        <UAlert
          color="neutral"
          variant="subtle"
          icon="i-lucide-info"
          :title="template.label"
          :description="template.help"
        />

        <div class="grid gap-4 sm:grid-cols-2">
          <UFormField label="Name" required :error="nameError" help="Rules of machines name the issuer by it.">
            <UInput
              v-model="name"
              class="w-full"
              :ui="{ base: 'font-mono' }"
              :disabled="editing"
              placeholder="github"
              autocomplete="off"
            />
          </UFormField>
          <UFormField label="Key source" :help="keySourceDescriptions[keySource]">
            <USelect v-model="keySource" :items="keySourceItems" class="w-full" />
          </UFormField>
        </div>

        <UFormField
          label="Issuer"
          required
          :error="issuerError"
          :help="
            editing
              ? 'The issuer cannot change: the rules were written for it. Add a new trusted issuer instead.'
              : 'The exact iss of the platform’s tokens; case and a trailing / matter.'
          "
        >
          <UInput
            v-model="issuerUrl"
            class="w-full"
            :ui="{ base: 'font-mono' }"
            :readonly="editing"
            :placeholder="template.issuerPlaceholder ?? 'https://issuer.example.com'"
            autocomplete="off"
          />
        </UFormField>

        <UFormField v-if="keySource === 'JwksUri'" label="JWKS URI" required>
          <UInput
            v-model="jwksUri"
            class="w-full"
            :ui="{ base: 'font-mono' }"
            placeholder="https://keys.example.com/openid/v1/jwks"
            autocomplete="off"
          />
        </UFormField>
        <UFormField
          v-if="keySource === 'Jwks'"
          label="JWKS"
          required
          help="The JSON Web Key Set with 1 to 10 public RSA or EC keys, each with a kid; for example the output of kubectl get --raw /openid/v1/jwks."
        >
          <UTextarea
            v-model="jwks"
            class="w-full"
            :ui="{ base: 'font-mono text-xs' }"
            :rows="5"
            autocomplete="off"
            :placeholder="jwksPlaceholder"
          />
        </UFormField>

        <UFormField
          label="Max token lifetime (seconds)"
          help="The longest accepted exp − iat of a platform token, 60 to 86,400. Empty: 3,600."
        >
          <UInputNumber v-model="lifetime" :min="60" :max="86400" placeholder="3600" class="w-48" />
        </UFormField>

        <ClaimRowsEditor
          v-model="requiredClaims"
          label="Required claims"
          help="Every token of this issuer must carry these claims, whatever the rule: the bound on what the rules of Contributors admit."
        />

        <UFormField label="Description">
          <UInput v-model="description" class="w-full" maxlength="256" autocomplete="off" />
        </UFormField>

        <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
      </form>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
        <UButton
          type="submit"
          form="trusted-issuer"
          :label="editing ? 'Save' : 'Add issuer'"
          icon="i-lucide-check"
          :loading="saving"
          :disabled="!complete"
        />
      </div>
    </template>
  </UModal>
</template>
