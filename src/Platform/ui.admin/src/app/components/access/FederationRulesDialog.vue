<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { setMachineFederationRules, type MachineAccess } from '@42for.net/2splatform.sdk'
import { useGetFederatedIssuers } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import {
  isRuleComplete,
  newRuleAudience,
  projectTokenEndpoint,
  ruleDraft,
  toFederationRule,
  type RuleDraft,
} from '@/utils/federation'
import { apiBaseUrl } from '@/utils/machines'

/**
 * Replaces the federation rules of a Federated machine (Contributor, 424-46; PUT …/federation-rules). The new rules
 * apply to new token exchanges; Storyteller tokens already issued stay valid until they expire. The API's 400 messages
 * (a rule names the field it fails on) show in the dialog.
 */
const props = defineProps<{ machine: MachineAccess }>()
const open = defineModel<boolean>('open', { default: false })

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()

const issuers = useGetFederatedIssuers(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => open.value && !!context.project, meta: { silent: true } } },
)

// New rules take the audience the machine's rules use already, so one machine never mixes the token endpoint New
// machine preset (from the address the UI calls the API at) with the one the API names; else the API's.
const audience = computed(() =>
  newRuleAudience(
    props.machine.FederationRules,
    issuers.data.value,
    props.machine.TokenEndpoint ?? projectTokenEndpoint(apiBaseUrl(), context.org ?? '', context.project ?? ''),
  ),
)
const rules = ref<RuleDraft[]>([])
const saving = ref(false)
const failure = ref<string>()

watch(open, (value) => {
  if (value) {
    failure.value = undefined
    const stored = props.machine.FederationRules ?? []
    rules.value = stored.length ? stored.map((rule) => ruleDraft(rule)) : [ruleDraft(undefined, audience.value)]
  }
})

const complete = computed(() => rules.value.length > 0 && rules.value.every(isRuleComplete))

async function save() {
  saving.value = true
  failure.value = undefined
  try {
    await setMachineFederationRules(
      context.org!,
      context.project!,
      props.machine.Id!,
      rules.value.map(toFederationRule),
    )
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
    toast.add({ title: 'Federation rules saved', color: 'success', icon: 'i-lucide-check' })
    open.value = false
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
    title="Federation rules"
    :description="`A platform token must match one of the rules to be exchanged for ${machine.Id}. Changes apply to new exchanges.`"
    :ui="{ content: 'max-w-2xl' }"
  >
    <template #body>
      <div class="space-y-4">
        <LoadingState v-if="issuers.isPending.value" />
        <ErrorState v-else-if="issuers.isError.value" :error="issuers.error.value" @retry="issuers.refetch()" />
        <FederationRuleEditor v-else v-model="rules" :issuers="issuers.data.value ?? []" :audience="audience" />
        <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
      </div>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
        <UButton label="Save rules" icon="i-lucide-check" :loading="saving" :disabled="!complete" @click="save" />
      </div>
    </template>
  </UModal>
</template>
