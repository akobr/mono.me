<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from '@/stores/session'

definePage({ meta: { public: true, layout: 'bare' } })

// The landing page of the invitation email (spec section 13): /invitations/accept?invitation_token=…&id=…
// A signed-out user signs in (or up) with the WorkOS invitation token; then the invitation list opens.
const route = useRoute()
const router = useRouter()
const session = useSessionStore()
session.refreshUser()

const first = (value: unknown) => (Array.isArray(value) ? value[0] : value)
const token = computed(() => {
  const value = first(route.query.invitation_token)
  return typeof value === 'string' && value ? value : undefined
})
const redirecting = ref(false)

async function continueWithInvitation() {
  redirecting.value = true
  await session.signIn('/invitations', { invitationToken: token.value })
}

onMounted(() => {
  if (session.user) {
    void router.replace('/invitations')
  }
})
</script>

<template>
  <UCard>
    <div class="space-y-4">
      <div class="space-y-1">
        <h1 class="text-xl font-semibold text-highlighted">You were invited to 2S Platform</h1>
        <p class="text-sm text-muted">
          Sign in, or create your account, with the email address the invitation was sent to.
        </p>
      </div>
      <UButton
        block
        size="lg"
        label="Continue"
        icon="i-lucide-log-in"
        :loading="redirecting"
        @click="continueWithInvitation"
      />
    </div>
  </UCard>
</template>
