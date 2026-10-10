<script setup lang="ts">
import { computed, ref } from 'vue'
import { isNotFound, isStorytellerError } from '@42for.net/2splatform.sdk'
import { useGetResolvedConfiguration } from '@42for.net/2splatform.sdk/vue-query'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'

/**
 * The Resolved tab (spec section 11.2): bindings evaluated by the server. It may contain secrets, so it is blurred
 * until "Reveal", never cached (gcTime 0) and refetched on every visit. A 422 shows the binding error.
 */
const props = defineProps<{ annotationKey: string }>()

const scope = useScope()
const preferences = usePreferencesStore()
const canSeeSecrets = useCan('secrets')
const revealed = ref(false)
const format = ref<DocumentFormat>(preferences.state.editorFormat)

const resolved = useGetResolvedConfiguration(scope.org, scope.project, scope.view, () => props.annotationKey, {
  query: { gcTime: 0, staleTime: 0, enabled: () => !!props.annotationKey, meta: { silent: true } },
})

const bindingError = computed(() => {
  const error = resolved.error.value
  return isStorytellerError(error) && error.status === 422 ? error : undefined
})
</script>

<template>
  <div class="space-y-3 p-4">
    <UAlert
      color="warning"
      variant="subtle"
      icon="i-lucide-key-round"
      title="May contain secrets"
      :description="
        canSeeSecrets
          ? 'Bindings are evaluated with your role; secret values are included. The response is never cached.'
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
