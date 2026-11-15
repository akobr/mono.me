<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useEventListener } from '@vueuse/core'
import { useStepUpStore } from '@/composables/useStepUp'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'

/**
 * The one confirmation of the step-up (424-264), however many requests asked for it: Sign in again goes to the
 * provider with max_age=0 and comes back to this route; Cancel leaves the screen's error as it is. After a step-up
 * that came back without a newer sign-in it says so instead of sending the person round again. A toast after a
 * return whose sign-in moved says that a change is not repeated. Back on the provider's page may restore this page
 * from the back/forward cache with the redirect still under way: the confirmation works again then.
 */
const stepUp = useStepUpStore()
const session = useSessionStore()
const route = useRoute()
const toast = useToast()

const failed = computed(() => stepUp.mode === 'failed')
const title = computed(() => (failed.value ? 'Your sign-in could not be confirmed' : "Confirm it's you"))
const description = computed(() =>
  failed.value
    ? 'Sign out and sign in again.'
    : `For your security, this needs a recent sign-in. Sign in again to continue; you come back to this page.${stepUp.unsaved ? ' Your unsaved changes on this page are lost.' : ''}`,
)

useEventListener(window, 'pageshow', (event: PageTransitionEvent) => {
  if (event.persisted) {
    stepUp.abandonRedirect()
  }
})

onMounted(async () => {
  if (await stepUp.takeReturnNotice()) {
    toast.add({
      id: 'step-up-return',
      title: 'You signed in again',
      description: 'Repeat the change if it did not finish.',
      color: 'success',
      icon: 'i-lucide-shield-check',
    })
  }
})
</script>

<template>
  <UModal
    :open="stepUp.open"
    :title="title"
    :description="description"
    :ui="{ footer: 'justify-end' }"
    @update:open="(open) => !open && !stepUp.redirecting && stepUp.cancel()"
  >
    <template v-if="stepUp.failure" #body>
      <UAlert color="error" variant="subtle" icon="i-lucide-circle-alert" :description="errorMessage(stepUp.failure)" />
    </template>
    <template #footer>
      <template v-if="failed">
        <UButton label="Close" color="neutral" variant="ghost" @click="stepUp.cancel()" />
        <UButton label="Sign out" icon="i-lucide-log-out" color="primary" autofocus @click="session.signOut()" />
      </template>
      <template v-else>
        <UButton
          label="Cancel"
          color="neutral"
          variant="ghost"
          :disabled="stepUp.redirecting"
          @click="stepUp.cancel()"
        />
        <UButton
          label="Sign in again"
          icon="i-lucide-log-in"
          color="primary"
          autofocus
          :loading="stepUp.redirecting"
          @click="stepUp.confirm(route.fullPath)"
        />
      </template>
    </template>
  </UModal>
</template>
