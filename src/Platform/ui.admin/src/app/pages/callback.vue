<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getAuthService } from '@/boot/state'

definePage({ meta: { public: true, layout: 'bare' } })

// authkit-js completes the code exchange while the app boots; this page only routes onwards.
const route = useRoute()
const router = useRouter()

onMounted(() => {
  const auth = getAuthService()
  const error = route.query.error_description ?? route.query.error

  if (error || !auth?.getUser()) {
    void router.replace({
      path: '/login',
      query: { error: typeof error === 'string' ? error : 'The sign-in did not complete.' },
    })
    return
  }

  void router.replace(auth.consumeReturnTo() ?? '/')
})
</script>

<template>
  <LoadingState label="Signing you in…" />
</template>
