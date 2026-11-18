<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { isNotFound, isStorytellerError } from '@42for.net/2splatform.sdk'
import { useGetResolvedConfiguration } from '@42for.net/2splatform.sdk/vue-query'
import { getStepUpSettings } from '@/boot/state'
import { isSignInTooOld, useStepUpStore } from '@/composables/useStepUp'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'

/**
 * The Resolved tab (spec section 11.2): bindings evaluated by the server. It may contain secrets, so it is blurred
 * until "Reveal", never cached (gcTime 0) and refetched on every visit. A 422 shows the binding error. With a sign-in
 * older than the API's window (424-264) the API leaves the secret bindings unresolved and says so only in a header
 * the browser cannot read cross-origin, so the client hint tells it: the alert offers to confirm it is you instead of
 * claiming the secrets are included. The page loads again after the return, and so does this read.
 */
const props = defineProps<{ annotationKey: string }>()

const scope = useScope()
const preferences = usePreferencesStore()
const canSeeSecrets = useCan('secrets')
const stepUp = useStepUpStore()
const revealed = ref(false)
const format = ref<DocumentFormat>(preferences.state.editorFormat)

const resolved = useGetResolvedConfiguration(scope.org, scope.project, scope.view, () => props.annotationKey, {
  query: { gcTime: 0, staleTime: 0, enabled: () => !!props.annotationKey, meta: { silent: true } },
})

// Whether the API withheld the secrets of the response shown: the token's auth_time older than the window itself when
// it answered (no margin: the hint before a write asks a minute early), asked again with every response. The browser
// knows only when the response arrived, by its own clock, so within half a minute of the window's end either answer may
// be wrong: the alert says the values may be hidden.
const edgeOfWindow = 30_000
const signInAge = ref<'recent' | 'edge' | 'stale'>('recent')
// Each response asks again; only the answer of the latest one is shown, however the checks overtake each other.
let classified = 0
watch(
  [canSeeSecrets, () => resolved.dataUpdatedAt.value],
  async ([can, answeredAt]) => {
    const run = ++classified
    const at = answeredAt || Date.now()
    const age = !can
      ? 'recent'
      : (await isSignInTooOld(at - edgeOfWindow))
        ? 'stale'
        : (await isSignInTooOld(at + edgeOfWindow))
          ? 'edge'
          : 'recent'
    if (run === classified) {
      signInAge.value = age
    }
  },
  { immediate: true },
)
const secretsWithheld = computed(() => canSeeSecrets.value && signInAge.value !== 'recent')
const withheldAlert = computed(() =>
  signInAge.value === 'stale'
    ? {
        title: 'Secret values are hidden',
        description:
          "Your sign-in is too old for secrets: they stay as their bindings until you confirm it's you. The response is never cached.",
      }
    : {
        title: 'Secret values may be hidden',
        description:
          "Your sign-in is close to the age limit for secrets, so they may stay as their bindings. Confirm it's you to be sure they are included. The response is never cached.",
      },
)
// The age check is only an estimate (the browser's clock against the provider's), so with a step-up the text says so.
const includedText = getStepUpSettings().enabled
  ? 'Bindings are evaluated with your role; secret values are included unless your sign-in is too old for them, which this browser can only estimate. The response is never cached.'
  : 'Bindings are evaluated with your role; secret values are included. The response is never cached.'
const confirmActions = [
  { label: "Confirm it's you", icon: 'i-lucide-log-in', color: 'warning' as const, onClick: () => void stepUp.ask() },
]

const bindingError = computed(() => {
  const error = resolved.error.value
  return isStorytellerError(error) && error.status === 422 ? error : undefined
})
</script>

<template>
  <div class="space-y-3 p-4">
    <UAlert
      v-if="secretsWithheld"
      color="warning"
      variant="subtle"
      icon="i-lucide-shield-alert"
      :title="withheldAlert.title"
      :description="withheldAlert.description"
      :actions="confirmActions"
    />
    <UAlert
      v-else
      color="warning"
      variant="subtle"
      icon="i-lucide-key-round"
      title="May contain secrets"
      :description="
        canSeeSecrets
          ? includedText
          : 'Secret values are left out for your role (ContributorWithSecrets is required to see them).'
      "
    />

    <LoadingState v-if="resolved.isPending.value" label="Evaluating bindings…" />
    <UAlert
      v-else-if="bindingError"
      color="error"
      variant="subtle"
      icon="i-lucide-function-square"
      title="A binding could not be evaluated"
      :description="bindingError.hint ? `${bindingError.message} ${bindingError.hint}` : bindingError.message"
    />
    <EmptyState v-else-if="isNotFound(resolved.error.value)" icon="i-lucide-file-x" title="No configuration" />
    <ErrorState v-else-if="resolved.isError.value" :error="resolved.error.value" @retry="resolved.refetch()" />
    <div v-else class="relative">
      <div :class="revealed ? '' : 'pointer-events-none blur-sm select-none'" :aria-hidden="!revealed">
        <DocumentEditor
          :model-value="resolved.data.value?.Content ?? {}"
          :format="format"
          readonly
          aria-label="Resolved configuration"
          @update:format="(value) => (format = value)"
        />
      </div>
      <div v-if="!revealed" class="absolute inset-0 flex items-center justify-center">
        <UButton label="Reveal" icon="i-lucide-eye" color="warning" @click="revealed = true" />
      </div>
    </div>
  </div>
</template>
