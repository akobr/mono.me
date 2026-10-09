<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useSessionStore } from '@/stores/session'
import { safeReturnTo } from '@/utils/return-to'

definePage({ meta: { public: true, layout: 'bare' } })

const route = useRoute()
const session = useSessionStore()
session.refreshUser()

const first = (value: unknown) => (Array.isArray(value) ? value[0] : value)
const error = computed(() => {
  const description = first(route.query.error_description) ?? first(route.query.error)
  return typeof description === 'string' ? description : undefined
})
const returnTo = computed(() => safeReturnTo(first(route.query.returnTo)) ?? '/')
const signingIn = ref(false)

async function signIn() {
  signingIn.value = true
  try {
    await session.signIn(returnTo.value)
  } finally {
    signingIn.value = false
  }
}
</script>

<template>
  <UCard>
    <div class="space-y-5">
      <div class="space-y-1">
        <h1 class="text-xl font-semibold text-highlighted">Sign in to 2S Admin</h1>
        <p class="text-sm text-muted">
          Manage organizations, projects, annotations and configurations of the 2S Platform.
        </p>
      </div>

      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-circle-alert"
        title="Sign-in failed"
        :description="error"
      />

      <template v-if="session.user">
        <p class="text-sm">
          You are signed in as <strong>{{ session.user.name }}</strong
          >.
        </p>
        <UButton block :to="returnTo" label="Continue" trailing-icon="i-lucide-arrow-right" />
      </template>
      <UButton
        v-else
        block
        size="lg"
        label="Continue with AuthKit"
        icon="i-lucide-log-in"
        :loading="signingIn"
        @click="signIn"
      />
    </div>
  </UCard>
</template>
